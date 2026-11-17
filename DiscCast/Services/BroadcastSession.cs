using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using DiscCast.Contracts;

namespace DiscCast.Services;

public sealed class BroadcastSession : IBroadcastSession
{
    private readonly string toolsDirectory;
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private Process? server, encoder;
    private ProcessAudioCapture? audio;
    private AudioCapturePlan? audioPlan;
    private CancellationTokenSource? cancellation;
    private Task? audioTask;
    private string? configPath;
    private string lastError = "";
    private readonly object logLock = new();
    private string? pendingFailure;
    private int generation;
    public BroadcastStatus Status { get; private set; } = new(BroadcastState.Idle, "Pronto para transmitir");
    public event Action<BroadcastStatus>? Changed;

    public BroadcastSession(string? toolsDirectory = null) => this.toolsDirectory = toolsDirectory ?? Path.Combine(AppContext.BaseDirectory, "tools");
    private void SetStatus(BroadcastStatus value) { Status = value; Changed?.Invoke(value); }

    public async Task StartAsync(BroadcastRequest request, CancellationToken token = default)
    {
        await lifecycle.WaitAsync(token);
        try
        {
            if (Status.State is BroadcastState.Starting or BroadcastState.Live) return;
            int current = ++generation;
            lastError = "";
            pendingFailure = null;
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            var ct = cancellation.Token;
            SetStatus(new(BroadcastState.Starting, "Preparando a transmissão…"));
            string ffmpeg = Path.Combine(toolsDirectory, "ffmpeg.exe"), mtx = Path.Combine(toolsDirectory, "mediamtx.exe");
            if (!File.Exists(ffmpeg) || !File.Exists(mtx)) throw new FileNotFoundException("FFmpeg ou MediaMTX ausente. Execute build.ps1 e mantenha a pasta portátil completa.");
            string room = Secret(12), publishPass = Secret(18), readPass = Secret(18), ip = LocalAddress();
            var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            configPath = Path.Combine(Path.GetTempPath(), $"disccast-{room}.yml");
            await File.WriteAllTextAsync(configPath, $"logLevel: info\nlogDestinations: [stdout]\nrtsp: true\nrtspAddress: {ip}:8554\nrtspTransports: [tcp]\nrtmp: false\nhls: false\nwebrtc: false\nsrt: false\nmoq: false\napi: false\nmetrics: false\npprof: false\nplayback: false\nauthMethod: internal\nauthInternalUsers:\n  - user: publisher\n    pass: {publishPass}\n    ips: [{ip}]\n    permissions:\n      - action: publish\n        path: {room}\n  - user: viewer\n    pass: {readPass}\n    ips: []\n    permissions:\n      - action: read\n        path: {room}\npaths:\n  {room}:\n    source: publisher\n", ct);
            string Redact(string line) => line.Replace(publishPass, "[senha]").Replace(readPass, "[senha]");
            server = ChildProcess.Start(mtx, [configPath], log: line =>
            {
                if (line.Contains("is publishing to path", StringComparison.OrdinalIgnoreCase)) published.TrySetResult();
                if (line.Contains("ERR", StringComparison.OrdinalIgnoreCase)) lastError = Redact(line);
            });
            server.Exited += (_, _) => UnexpectedExit(current, "MediaMTX encerrou");
            await Task.Delay(500, ct);
            if (server.HasExited) throw new InvalidOperationException("MediaMTX não iniciou. " + lastError);
            string audioMessage = "Sem áudio";
            if (request.Audio.Enabled)
            {
                try
                {
                    audioPlan = AudioCapturePlan.Create(request.Audio);
                    audio = await ProcessAudioCapture.CreateAsync(audioPlan.ProcessId, audioPlan.LoopbackMode);
                    audioMessage = audioPlan.Description;
                }
                catch (Exception ex)
                {
                    audio?.Dispose(); audio = null;
                    audioPlan?.Dispose(); audioPlan = null;
                    audioMessage = "Sem áudio: " + ex.Message;
                }
            }
            ct.ThrowIfCancellationRequested();
            bool nvenc = await NvencAvailable(ffmpeg, ct);
            var args = EncoderArguments(request, audio, nvenc, $"rtsp://publisher:{publishPass}@{ip}:8554/{room}");
            encoder = ChildProcess.Start(ffmpeg, args, audio != null, line =>
            {
                lock (logLock) { lastError = (lastError + "\n" + Redact(line)).Trim(); if (lastError.Length > 4000) lastError = lastError[^4000..]; }
            });
            encoder.Exited += (_, _) => UnexpectedExit(current, "FFmpeg encerrou");
            if (audio != null)
            {
                var capture = audio; var output = encoder.StandardInput.BaseStream; var plan = audioPlan!;
                audioTask = Task.Run(() => capture.PumpAsync(output, ct, plan.IsAllowed), ct);
                _ = audioTask.ContinueWith(task =>
                {
                    if (task.IsFaulted) _ = FailAsync(current, "Falha no filtro de áudio: " + task.Exception!.GetBaseException().Message);
                }, TaskScheduler.Default);
            }
            await published.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
            if (encoder.HasExited || server.HasExited) throw new InvalidOperationException(lastError);
            SetStatus(new(BroadcastState.Live, audioMessage + (ip == "127.0.0.1" ? " · Sem IP privado: acesso somente neste PC." : ""),
                $"rtsp://viewer:{readPass}@{ip}:8554/{room}", nvenc ? "NVIDIA NVENC" : "CPU · H.264", audio != null));
        }
        catch (Exception ex)
        {
            bool cancelled = ex is OperationCanceledException && pendingFailure == null;
            await CleanupAsync();
            SetStatus(new(cancelled ? BroadcastState.Idle : BroadcastState.Failed,
                cancelled ? "Transmissão cancelada" : "Não foi possível transmitir: " + (pendingFailure ?? (ex is TimeoutException ? "o servidor não confirmou a publicação. " + lastError : ex.Message))));
        }
        finally { lifecycle.Release(); }
    }

    private void UnexpectedExit(int current, string component)
    {
        if (cancellation?.IsCancellationRequested == false) _ = FailAsync(current, component + ". " + lastError);
    }
    private async Task FailAsync(int current, string message)
    {
        if (current != generation || cancellation?.IsCancellationRequested != false) return;
        pendingFailure = message;
        cancellation.Cancel();
        await lifecycle.WaitAsync();
        try
        {
            if (current != generation) return;
            await CleanupAsync();
            SetStatus(new(BroadcastState.Failed, message));
        }
        finally { lifecycle.Release(); }
    }
    public async Task StopAsync()
    {
        cancellation?.Cancel();
        await lifecycle.WaitAsync();
        try
        {
            generation++;
            SetStatus(new(BroadcastState.Stopping, "Encerrando transmissão…"));
            await CleanupAsync();
            SetStatus(new(BroadcastState.Idle, "Transmissão encerrada"));
        }
        finally { lifecycle.Release(); }
    }
    private async Task CleanupAsync()
    {
        cancellation?.Cancel();
        var encoderToStop = encoder; encoder = null;
        await ChildProcess.TerminateAsync(encoderToStop);
        if (audioTask != null) { try { await audioTask; } catch { } audioTask = null; }
        audio?.Dispose(); audio = null;
        audioPlan?.Dispose(); audioPlan = null;
        var serverToStop = server; server = null;
        await ChildProcess.TerminateAsync(serverToStop);
        cancellation?.Dispose(); cancellation = null;
        if (configPath != null) { try { File.Delete(configPath); } catch (IOException) { } configPath = null; }
    }
    public async ValueTask DisposeAsync() { await StopAsync(); lifecycle.Dispose(); }
    private static string Secret(int bytes) => Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();
    private static string LocalAddress() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(a => a.Address)
        .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && IsPrivate(a.GetAddressBytes()))?.ToString() ?? "127.0.0.1";
    private static bool IsPrivate(byte[] b) => b[0] == 10 || b[0] == 192 && b[1] == 168 || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 100 && b[1] is >= 64 and <= 127;

    private static async Task<bool> NvencAvailable(string ffmpeg, CancellationToken cancellation)
    {
        using var p = ChildProcess.Start(ffmpeg, ["-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "color=s=128x128:r=1", "-frames:v", "1", "-c:v", "h264_nvenc", "-f", "null", "-"]);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try { await p.WaitForExitAsync(timeout.Token); return p.ExitCode == 0; }
        catch (OperationCanceledException) { if (!p.HasExited) { p.Kill(true); await p.WaitForExitAsync(); } cancellation.ThrowIfCancellationRequested(); return false; }
    }
    private static List<string> EncoderArguments(BroadcastRequest request, ProcessAudioCapture? audio, bool nvenc, string url)
    {
        var p = request.Profile;
        var args = new List<string> { "-hide_banner", "-loglevel", "warning", "-f", "gdigrab", "-framerate", p.FramesPerSecond.ToString() };
        if (request.Source.Kind == VideoSourceKind.Monitor)
        {
            var parts = request.Source.Input.Split('|');
            args.AddRange(["-offset_x", parts[1], "-offset_y", parts[2], "-video_size", $"{parts[3]}x{parts[4]}", "-i", "desktop"]);
        }
        else args.AddRange(["-i", request.Source.Input]);
        if (audio != null) args.AddRange(["-f", audio.FfmpegFormat, "-ar", audio.SampleRate.ToString(), "-ac", audio.Channels.ToString(), "-i", "pipe:0"]);
        args.AddRange(["-vf", $"scale={p.Width}:{p.Height}:force_original_aspect_ratio=decrease,pad={p.Width}:{p.Height}:(ow-iw)/2:(oh-ih)/2", "-r", p.FramesPerSecond.ToString(), "-pix_fmt", "yuv420p", "-c:v", nvenc ? "h264_nvenc" : "libx264", "-preset", nvenc ? "p4" : "veryfast", "-tune", nvenc ? "ll" : "zerolatency", "-g", (p.FramesPerSecond * 2).ToString(), "-bf", "0"]);
        if (audio != null) args.AddRange(["-c:a", "aac", "-b:a", "128k", "-af", "aresample=async=1"]);
        else args.Add("-an");
        args.AddRange(["-f", "rtsp", "-rtsp_transport", "tcp", url]);
        return args;
    }
}
