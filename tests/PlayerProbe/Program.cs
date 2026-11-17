using LibVLCSharp.Shared;

if (args.Length < 1 || args.Length > 2) { Console.Error.WriteLine("Uso: PlayerProbe rtsp://viewer:senha@host:porta/sala [--audio]"); return 2; }
try
{
    Core.Initialize();
    using var vlc = new LibVLC("--rtsp-tcp", "--vout=dummy", "--aout=dummy", "--network-caching=200");
    using var player = new MediaPlayer(vlc);
    using var media = new Media(vlc, new Uri(args[0]));
    var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    player.Playing += (_, _) => started.TrySetResult(true);
    player.EncounteredError += (_, _) => started.TrySetException(new InvalidOperationException(vlc.LastLibVLCError ?? "Falha de reprodução LibVLC"));
    if (!player.Play(media)) throw new InvalidOperationException("LibVLC recusou iniciar a reprodução.");
    await started.Task.WaitAsync(TimeSpan.FromSeconds(12));
    await Task.Delay(2000);
    if (!player.IsPlaying) throw new InvalidOperationException("LibVLC interrompeu a reprodução.");
    var tracks = media.Tracks;
    if (tracks == null || !tracks.Any(t => t.TrackType == TrackType.Video)) throw new InvalidOperationException("LibVLC não identificou a faixa de vídeo.");
    if (args.Length == 2 && args[1] == "--audio" && !tracks.Any(t => t.TrackType == TrackType.Audio)) throw new InvalidOperationException("LibVLC não identificou a faixa de áudio.");
    Console.WriteLine($"OK: LibVLC reproduziu RTSP por pelo menos dois segundos; {tracks.Length} faixas, {player.VoutCount} saída(s) de vídeo.");
    player.Stop();
    return 0;
}
catch (Exception ex) { Console.Error.WriteLine(ex); return 2; }
