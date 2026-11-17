using DiscCast;
using DiscCast.Contracts;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

if (args.Contains("--tone"))
{
    var data = ToneWave();
    var pin = GCHandle.Alloc(data, GCHandleType.Pinned);
    try
    {
        if (!PlaySound(pin.AddrOfPinnedObject(), IntPtr.Zero, 0x0004 | 0x0001 | 0x0008)) return 3;
        Console.WriteLine("TONE_READY");
        await Task.Delay(20000);
    }
    finally { PlaySound(IntPtr.Zero, IntPtr.Zero, 0); pin.Free(); }
    return 0;
}
try
{
    using var emitter = Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, ArgumentList = { "--tone" } })!;
    try
    {
        if (await emitter.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)) != "TONE_READY") throw new Exception("O emissor de áudio não iniciou.");
        double included = await Measure(emitter.Id, ProcessLoopbackMode.IncludeTree);
        double excluded = await Measure(emitter.Id, ProcessLoopbackMode.ExcludeTree);
        Console.WriteLine($"997 Hz amplitude: include={included:F6}; exclude={excluded:F6}");
        if (included < 0.005) throw new Exception("O sinal de teste não apareceu na captura. Teste inconclusivo neste dispositivo.");
        if (excluded >= included * 0.05) throw new Exception("A exclusão não suprimiu o sinal por pelo menos 26 dB.");
        Console.WriteLine("OK: sinal real incluído e excluído pelo WASAPI Process Loopback (processo de teste, não Discord real).");
    }
    finally { if (!emitter.HasExited) emitter.Kill(true); await emitter.WaitForExitAsync(); }
    return 0;
}
catch (Exception ex) { Console.Error.WriteLine(ex); return 2; }

static async Task<double> Measure(int pid, ProcessLoopbackMode mode)
{
    using var capture = await ProcessAudioCapture.CreateAsync(pid, mode);
    using var memory = new MemoryStream();
    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    await Task.Run(() => capture.PumpAsync(memory, stop.Token)).WaitAsync(TimeSpan.FromSeconds(6));
    byte[] bytes = memory.ToArray();
    int frames = bytes.Length / capture.BlockAlign;
    if (frames == 0) throw new Exception("Nenhuma amostra recebida.");
    double real = 0, imag = 0;
    for (int i = 0; i < frames; i++)
    {
        int offset = i * capture.BlockAlign;
        double sample = capture.FfmpegFormat switch { "f32le" => BitConverter.ToSingle(bytes, offset), "s16le" => BitConverter.ToInt16(bytes, offset) / 32768.0, "s32le" => BitConverter.ToInt32(bytes, offset) / 2147483648.0, _ => throw new Exception("Formato do teste não suportado") };
        double phase = 2 * Math.PI * 997 * i / capture.SampleRate;
        real += sample * Math.Cos(phase); imag += sample * Math.Sin(phase);
    }
    return 2 * Math.Sqrt(real * real + imag * imag) / frames;
}
static byte[] ToneWave()
{
    using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
    const int rate = 48000, samples = rate;
    writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples * 2); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
    writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
    writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(samples * 2);
    for (int i = 0; i < samples; i++) writer.Write((short)(Math.Sin(2 * Math.PI * 997 * i / rate) * 8192));
    return stream.ToArray();
}
[DllImport("winmm.dll", SetLastError = true)] static extern bool PlaySound(IntPtr sound, IntPtr module, uint flags);
