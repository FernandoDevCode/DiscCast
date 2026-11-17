using System.Diagnostics;
using System.Runtime.InteropServices;
using DiscCast.Contracts;

namespace DiscCast.Services;

internal sealed record ProcessInfo(int Id, int ParentId, string Name);
internal static class ProcessCatalog
{
    internal static Dictionary<int, ProcessInfo> Snapshot()
    {
        IntPtr snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot == new IntPtr(-1)) throw new InvalidOperationException("Não foi possível verificar os processos de áudio.");
        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            var result = new Dictionary<int, ProcessInfo>();
            if (!Process32First(snapshot, ref entry)) throw new InvalidOperationException("A lista de processos está indisponível.");
            do
            {
                result[(int)entry.ProcessId] = new((int)entry.ProcessId, (int)entry.ParentProcessId, entry.ExeFile);
                entry.Size = (uint)Marshal.SizeOf<ProcessEntry>();
            } while (Process32Next(snapshot, ref entry));
            return result;
        }
        finally { CloseHandle(snapshot); }
    }

    internal static bool InTree(IReadOnlyDictionary<int, ProcessInfo> processes, int root, int pid)
    {
        var seen = new HashSet<int>();
        while (pid != 0 && seen.Add(pid))
        {
            if (pid == root) return true;
            if (!processes.TryGetValue(pid, out var process)) break;
            pid = process.ParentId;
        }
        return false;
    }

    internal static bool IsDiscord(ProcessInfo p) => p.Name.StartsWith("Discord", StringComparison.OrdinalIgnoreCase);
    internal static bool IsDiscCast(ProcessInfo p) => p.Id == Environment.ProcessId || p.Name.Equals("DiscCast.exe", StringComparison.OrdinalIgnoreCase);

    internal static void ValidateApplication(IReadOnlyDictionary<int, ProcessInfo> processes, int pid)
    {
        if (!processes.ContainsKey(pid)) throw new InvalidOperationException("O aplicativo selecionado foi fechado.");
        if (processes.Values.Any(p => (IsDiscord(p) || IsDiscCast(p)) && InTree(processes, pid, p.Id)))
            throw new InvalidOperationException("Escolha um aplicativo cuja árvore não inclua Discord nem DiscCast.");
    }

    internal static int DiscordRoot(IReadOnlyDictionary<int, ProcessInfo> processes)
    {
        var discord = processes.Values.Where(IsDiscord).ToArray();
        if (discord.Length == 0) throw new InvalidOperationException("Abra o Discord desktop antes de usar “PC sem Discord”.");
        var roots = discord.Where(p => !discord.Any(other => other.Id != p.Id && InTree(processes, other.Id, p.Id))).ToArray();
        if (roots.Length != 1) throw new InvalidOperationException("Há mais de uma instância independente do Discord. Feche as extras para excluir todo o áudio do Discord.");
        return roots[0].Id;
    }

    public static IReadOnlyList<AudioApplication> Applications()
    {
        var snapshot = Snapshot();
        var result = new List<AudioApplication>();
        foreach (var p in snapshot.Values.Where(p => p.Id > 0 && !IsDiscord(p) && !IsDiscCast(p)))
        {
            try
            {
                using var process = Process.GetProcessById(p.Id);
                if (process.SessionId != Process.GetCurrentProcess().SessionId) continue;
                var title = process.MainWindowTitle;
                if (string.IsNullOrWhiteSpace(title)) continue;
                result.Add(new AudioApplication($"{System.IO.Path.GetFileNameWithoutExtension(p.Name)} — {title}", p.Id));
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }
        return result.OrderBy(p => p.Name).ToArray();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size, Usage, ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId, Threads, ParentProcessId;
        public int Priority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExeFile;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
