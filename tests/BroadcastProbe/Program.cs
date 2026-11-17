using DiscCast.Contracts;
using DiscCast.Services;
using System.Diagnostics;
using System.IO;

string tools = Path.GetFullPath("dist/DiscCastPortable-v4/tools");
var b = Screen.PrimaryScreen!.Bounds;
Console.WriteLine($"Capture bounds: {b}");
var source = new VideoSource("Desktop de teste", VideoSourceKind.Monitor, $"desktop|{b.X}|{b.Y}|{b.Width}|{b.Height}", b.Width, b.Height);
try
{
    await using var session = new BroadcastSession(tools);
    session.Changed += status => Console.WriteLine($"{status.State}: {status.Message}");
    for (int cycle = 0; cycle < 2; cycle++)
    {
        // First: the exact no-audio path that previously read stderr twice.
        // Second: unavailable audio must still publish video, never fall back to global capture.
        var audio = cycle == 0 ? new AudioSelection(false, AudioMode.Application, null) : new AudioSelection(true, AudioMode.Application, int.MaxValue);
        await session.StartAsync(new(source, audio, StreamProfile.Standard));
        if (session.Status.State != BroadcastState.Live) throw new Exception("O serviço não chegou a Live.");
        if (session.Status.HasAudio) throw new Exception("O fallback deveria ser vídeo sem áudio.");
        var info = new ProcessStartInfo(Path.Combine(tools, "ffmpeg.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-hide_banner", "-loglevel", "error", "-rtsp_transport", "tcp", "-i", session.Status.RoomAddress!, "-frames:v", "5", "-f", "null", "-" }) info.ArgumentList.Add(arg);
        using var viewer = Process.Start(info)!;
        var log = viewer.StandardError.ReadToEndAsync();
        try { await viewer.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); }
        catch { if (!viewer.HasExited) viewer.Kill(true); throw; }
        if (viewer.ExitCode != 0) throw new Exception("Leitura RTSP falhou: " + await log);
        await session.StopAsync();
        if (session.Status.State != BroadcastState.Idle || session.Status.RoomAddress != null) throw new Exception("A sala não foi limpa.");
    }
    Console.WriteLine("OK: dois ciclos reais do serviço, leitura de vídeo e fallback seguro sem áudio.");
    return 0;
}
catch (Exception ex) { Console.Error.WriteLine(ex); return 2; }
