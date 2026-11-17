namespace DiscCast.Contracts;

public enum AudioMode { Application, SystemExceptDiscord }
public enum ProcessLoopbackMode { IncludeTree = 0, ExcludeTree = 1 }
public enum BroadcastState { Idle, Starting, Live, Stopping, Failed }
public enum VideoSourceKind { Monitor, Window }

public sealed record VideoSource(string Label, VideoSourceKind Kind, string Input, int Width, int Height, int ProcessId = 0)
{
    public override string ToString() => Label;
}
public sealed record AudioApplication(string Name, int ProcessId)
{
    public override string ToString() => $"{Name}  ·  PID {ProcessId}";
}
public sealed record AudioSelection(bool Enabled, AudioMode Mode, int? ProcessId);
public sealed record StreamProfile(int Width, int Height, int FramesPerSecond)
{
    public static StreamProfile Standard { get; } = new(1280, 720, 30);
    public static StreamProfile High { get; } = new(1920, 1080, 60);
    public string Label => $"{Height}p · {FramesPerSecond} FPS";
}
public sealed record BroadcastRequest(VideoSource Source, AudioSelection Audio, StreamProfile Profile, bool Internet = false);
public sealed record BroadcastStatus(BroadcastState State, string Message, string? RoomAddress = null, string? Encoder = null, bool HasAudio = false);

public interface IBroadcastSession : IAsyncDisposable
{
    BroadcastStatus Status { get; }
    event Action<BroadcastStatus>? Changed;
    Task StartAsync(BroadcastRequest request, CancellationToken cancellation = default);
    Task StopAsync();
}
