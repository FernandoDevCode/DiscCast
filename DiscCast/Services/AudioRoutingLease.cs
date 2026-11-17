using System.IO;
namespace DiscCast.Services;

// Shared playback leases and exclusive system capture. OS handles are released on crash.
internal sealed class AudioRoutingLease : IDisposable
{
    private readonly FileStream handle;
    private AudioRoutingLease(FileStream handle) => this.handle = handle;
    public static AudioRoutingLease Acquire(bool systemCapture)
    {
        string path = Path.Combine(Path.GetTempPath(), "DiscCast.audio-routing.lock");
        try
        {
            if (!File.Exists(path)) { using var initial = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite); }
            return new(new FileStream(path, FileMode.Open, systemCapture ? FileAccess.ReadWrite : FileAccess.Read,
                systemCapture ? FileShare.None : FileShare.Read));
        }
        catch (IOException ex) { throw new InvalidOperationException("Há reprodução local ou captura do PC em outro DiscCast. Encerre essa atividade para evitar retorno de áudio.", ex); }
    }
    public void Dispose() => handle.Dispose();
}
