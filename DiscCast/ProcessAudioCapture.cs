using System.Runtime.InteropServices;
using System.IO;
using DiscCast.Contracts;

namespace DiscCast;

// Windows process loopback API. No endpoint/system-loopback capture is used here.
internal sealed class ProcessAudioCapture : IDisposable
{
    private const string Device = "VAD\\Process_Loopback";
    private const int Loopback = 0x00020000;
    private readonly IAudioClient client;
    private readonly IAudioCaptureClient capture;
    private bool started;
    public int SampleRate { get; }
    public int Channels { get; }
    public int BlockAlign { get; }
    public string FfmpegFormat { get; }

    private ProcessAudioCapture(IAudioClient client, IAudioCaptureClient capture, int sampleRate, int channels, int blockAlign, string format)
    { this.client = client; this.capture = capture; SampleRate = sampleRate; Channels = channels; BlockAlign = blockAlign; FfmpegFormat = format; }

    public static async Task<ProcessAudioCapture> CreateAsync(int pid, ProcessLoopbackMode mode = ProcessLoopbackMode.IncludeTree)
    {
        if (Environment.OSVersion.Version.Build < 20348)
            throw new NotSupportedException("O WASAPI Process Loopback requer build 20348 ou posterior. Neste Windows, a transmissão fica sem áudio para evitar capturar outros aplicativos.");
        var parameters = new ActivationParams { ActivationType = 1, Pid = (uint)pid, Mode = (int)mode };
        IntPtr blob = Marshal.AllocHGlobal(Marshal.SizeOf<ActivationParams>());
        IntPtr prop = Marshal.AllocHGlobal(Marshal.SizeOf<PropVariant>());
        try
        {
            Marshal.StructureToPtr(parameters, blob, false);
            Marshal.StructureToPtr(new PropVariant { Type = 65, Size = Marshal.SizeOf<ActivationParams>(), Data = blob }, prop, false);
            var callback = new Completion();
            Guid iid = typeof(IAudioClient).GUID;
            int hr = ActivateAudioInterfaceAsync(Device, ref iid, prop, callback, out var operation);
            Marshal.ThrowExceptionForHR(hr);
            try
            {
                var result = await callback.Result.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Marshal.ThrowExceptionForHR(result.HResult);
                if (result.Client == null) throw new InvalidOperationException("A API não retornou um cliente de áudio.");
                var client = result.Client;
                // The virtual process-loopback client returns E_NOTIMPL for GetMixFormat.
                // Query only the format of the default render endpoint; no endpoint audio is captured.
                IntPtr formatPtr = DefaultRenderMixFormat();
                int channels, rate, align; string ffFormat;
                try
                {
                    ushort tag = (ushort)Marshal.ReadInt16(formatPtr, 0);
                    channels = (ushort)Marshal.ReadInt16(formatPtr, 2);
                    rate = Marshal.ReadInt32(formatPtr, 4);
                    align = (ushort)Marshal.ReadInt16(formatPtr, 12);
                    int bits = (ushort)Marshal.ReadInt16(formatPtr, 14);
                    if (tag == 0xFFFE) tag = (ushort)Marshal.ReadInt32(formatPtr, 24);
                    ffFormat = (tag, bits) switch { (3, 32) => "f32le", (1, 16) => "s16le", (1, 24) => "s24le", (1, 32) => "s32le", _ => throw new NotSupportedException($"Formato WASAPI não suportado: tipo {tag}, {bits} bits.") };
                    Marshal.ThrowExceptionForHR(client.Initialize(0, Loopback, 0, 0, formatPtr, IntPtr.Zero));
                }
                finally { Marshal.FreeCoTaskMem(formatPtr); }
                Guid captureId = typeof(IAudioCaptureClient).GUID;
                Marshal.ThrowExceptionForHR(client.GetService(ref captureId, out var captureObj));
                return new ProcessAudioCapture(client, (IAudioCaptureClient)captureObj, rate, channels, align, ffFormat);
            }
            finally { if (operation != IntPtr.Zero) Marshal.Release(operation); }
        }
        finally { Marshal.FreeHGlobal(prop); Marshal.FreeHGlobal(blob); }
    }

    public async Task PumpAsync(Stream destination, CancellationToken cancellation, Func<bool>? processTreeAllowed = null)
    {
        Marshal.ThrowExceptionForHR(client.Start()); started = true;
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                if (processTreeAllowed != null && !processTreeAllowed()) throw new InvalidOperationException("O filtro de áudio deixou de ser válido.");
                Marshal.ThrowExceptionForHR(capture.GetNextPacketSize(out uint packet));
                while (packet > 0)
                {
                    Marshal.ThrowExceptionForHR(capture.GetBuffer(out IntPtr data, out uint frames, out uint flags, out _, out _));
                    byte[] buffer;
                    try
                    {
                        int bytes = checked((int)frames * BlockAlign);
                        buffer = new byte[bytes];
                        if ((flags & 2) == 0) Marshal.Copy(data, buffer, 0, bytes);
                    }
                    finally { Marshal.ThrowExceptionForHR(capture.ReleaseBuffer(frames)); }
                    // Release the WASAPI buffer before asynchronous pipe I/O.
                    if (processTreeAllowed != null && !processTreeAllowed()) throw new InvalidOperationException("O filtro de áudio deixou de ser válido.");
                    await destination.WriteAsync(buffer, cancellation);
                    Marshal.ThrowExceptionForHR(capture.GetNextPacketSize(out packet));
                }
                await Task.Delay(10, cancellation);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally { if (started) { client.Stop(); started = false; } }
    }

    public void Dispose()
    {
        if (started) { client.Stop(); started = false; }
        Marshal.ReleaseComObject(capture); Marshal.ReleaseComObject(client);
    }

    private static IntPtr DefaultRenderMixFormat()
    {
        Guid classId = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
        Guid interfaceId = typeof(IMMDeviceEnumerator).GUID;
        Marshal.ThrowExceptionForHR(CoCreateInstance(ref classId, IntPtr.Zero, 1, ref interfaceId, out var pointer));
        try
        {
            var enumerator = (IMMDeviceEnumerator)Marshal.GetObjectForIUnknown(pointer);
            try
            {
                Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0, 0, out var device));
                try
                {
                    Guid clientId = typeof(IAudioClient).GUID;
                    Marshal.ThrowExceptionForHR(device.Activate(ref clientId, 1, IntPtr.Zero, out var obj));
                    var endpointClient = (IAudioClient)obj;
                    try { Marshal.ThrowExceptionForHR(endpointClient.GetMixFormat(out var format)); return format; }
                    finally { Marshal.ReleaseComObject(endpointClient); }
                }
                finally { Marshal.ReleaseComObject(device); }
            }
            finally { Marshal.ReleaseComObject(enumerator); }
        }
        finally { Marshal.Release(pointer); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct ActivationParams { public int ActivationType; public uint Pid; public int Mode; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct PropVariant
    { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public int Size; [FieldOffset(16)] public IntPtr Data; }

    [DllImport("Mmdevapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int ActivateAudioInterfaceAsync(string deviceInterfacePath, ref Guid riid, IntPtr activationParams, ICompletionHandler completionHandler, out IntPtr activationOperation);
    [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid, out IntPtr result);

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, int mask, out IntPtr collection);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object result);
        [PreserveSig] int OpenPropertyStore(int access, out IntPtr store);
        [PreserveSig] int GetId(out IntPtr id);
        [PreserveSig] int GetState(out uint state);
    }

    [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICompletionHandler { [PreserveSig] int ActivateCompleted(IActivationOperation operation); }
    [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivationOperation { [PreserveSig] int GetActivateResult(out int activateResult, out IntPtr activatedInterface); }
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class Completion : ICompletionHandler
    {
        public TaskCompletionSource<(int HResult, IAudioClient? Client)> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ActivateCompleted(IActivationOperation operation)
        {
            try
            {
                Marshal.ThrowExceptionForHR(operation.GetActivateResult(out int hr, out IntPtr pointer));
                IAudioClient? client = null;
                if (pointer != IntPtr.Zero) { try { client = (IAudioClient)Marshal.GetObjectForIUnknown(pointer); } finally { Marshal.Release(pointer); } }
                Result.TrySetResult((hr, client));
            }
            catch (Exception ex) { Result.TrySetException(ex); }
            return 0;
        }
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
        [PreserveSig] int GetBufferSize(out uint frames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint padding);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start(); [PreserveSig] int Stop(); [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr handle);
        [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }
    [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
        [PreserveSig] int ReleaseBuffer(uint frames);
        [PreserveSig] int GetNextPacketSize(out uint frames);
    }
}
