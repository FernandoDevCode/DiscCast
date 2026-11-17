using System.Diagnostics;
using System.IO;

namespace DiscCast.Services;

internal static class ChildProcess
{
    public static Process Start(string file, IEnumerable<string> arguments, bool input = false, Action<string>? log = null)
    {
        var info = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = input,
            RedirectStandardError = true, RedirectStandardOutput = true, WorkingDirectory = Path.GetDirectoryName(file)! };
        foreach (var arg in arguments) info.ArgumentList.Add(arg);
        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        process.ErrorDataReceived += (_, line) => { if (!string.IsNullOrWhiteSpace(line.Data)) log?.Invoke(line.Data); };
        process.OutputDataReceived += (_, line) => { if (!string.IsNullOrWhiteSpace(line.Data)) log?.Invoke(line.Data); };
        try
        {
            if (!process.Start()) throw new InvalidOperationException("Não foi possível iniciar " + Path.GetFileName(file));
            // Exactly one reader for each redirected stream, independent of whether stdin is used.
            process.BeginErrorReadLine(); process.BeginOutputReadLine();
            return process;
        }
        catch { process.Dispose(); throw; }
    }
    public static async Task TerminateAsync(Process? process)
    {
        if (process == null) return;
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        catch (InvalidOperationException) { }
        finally { process.Dispose(); }
    }
}
