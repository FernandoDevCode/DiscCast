using System.Diagnostics;
using DiscCast.Contracts;

namespace DiscCast.Services;

internal sealed class AudioCapturePlan : IDisposable
{
    private readonly Process target;
    private readonly AudioRoutingLease? lease;
    private readonly AudioMode mode;
    private DateTime nextCheck;
    public int ProcessId { get; }
    public ProcessLoopbackMode LoopbackMode { get; }
    public string Description => mode == AudioMode.Application ? "Áudio do aplicativo" : "Áudio do PC · Discord excluído";

    private AudioCapturePlan(AudioMode mode, int pid, AudioRoutingLease? lease)
    {
        this.mode = mode; ProcessId = pid; this.lease = lease;
        target = Process.GetProcessById(pid);
        _ = target.Handle; // Retain a handle to this process, not a subsequently reused PID.
        LoopbackMode = mode == AudioMode.Application ? ProcessLoopbackMode.IncludeTree : ProcessLoopbackMode.ExcludeTree;
    }
    public static AudioCapturePlan Create(AudioSelection selection)
    {
        if (Environment.OSVersion.Version.Build < 20348) throw new NotSupportedException("Este Windows não oferece o filtro de áudio necessário (build mínimo 20348). A transmissão seguirá sem áudio.");
        var processes = ProcessCatalog.Snapshot();
        if (selection.Mode == AudioMode.Application)
        {
            int pid = selection.ProcessId ?? throw new InvalidOperationException("Selecione o aplicativo de áudio.");
            ProcessCatalog.ValidateApplication(processes, pid);
            return new(selection.Mode, pid, null);
        }
        int discord = ProcessCatalog.DiscordRoot(processes);
        var lease = AudioRoutingLease.Acquire(systemCapture: true);
        try { return new(selection.Mode, discord, lease); }
        catch { lease.Dispose(); throw; }
    }
    public bool IsAllowed()
    {
        if (target.HasExited) throw new InvalidOperationException(mode == AudioMode.Application ? "O aplicativo de áudio foi fechado." : "Discord foi fechado ou reiniciado. Reinicie a transmissão para renovar a exclusão.");
        if (DateTime.UtcNow < nextCheck) return true;
        nextCheck = DateTime.UtcNow.AddMilliseconds(100);
        var processes = ProcessCatalog.Snapshot();
        if (mode == AudioMode.Application) ProcessCatalog.ValidateApplication(processes, ProcessId);
        else if (ProcessCatalog.DiscordRoot(processes) != ProcessId) throw new InvalidOperationException("Os processos do Discord mudaram. Reinicie a transmissão para renovar a exclusão.");
        return true;
    }
    public void Dispose() { target.Dispose(); lease?.Dispose(); }
}
