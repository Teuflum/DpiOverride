// Windows-specific helpers: DPI awareness of running processes and restarting programs.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

enum Awareness
{
    Unknown = -1,      // not running, or Windows didn't tell us
    Unaware = 0,       // Windows stretches the bitmap -> blurry when scaling > 100%
    GdiScaled = 1,     // unaware, but Windows re-renders GDI text ("System (Enhanced)")
    System = 2,        // sharp on the main display, blurry on displays with a different scale
    PerMonitor = 3,    // sharp everywhere
    PerMonitorV2 = 4,  // sharp everywhere
}

static class DpiInfo
{
    const uint QUERY_INFORMATION = 0x0400, QUERY_LIMITED = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr h);
    [DllImport("user32.dll")]
    static extern IntPtr GetDpiAwarenessContextForProcess(IntPtr hProcess);        // Windows 10 1803+
    [DllImport("user32.dll")]
    static extern int GetAwarenessFromDpiAwarenessContext(IntPtr ctx);
    [DllImport("user32.dll")]
    static extern bool AreDpiAwarenessContextsEqual(IntPtr a, IntPtr b);
    [DllImport("shcore.dll")]
    static extern int GetProcessDpiAwareness(IntPtr hProcess, out int value);       // Windows 8.1+

    // Monitors
    delegate bool MonitorEnumProc(IntPtr hMon, IntPtr hdc, IntPtr rect, IntPtr data);
    [DllImport("user32.dll")]
    static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
    [DllImport("shcore.dll")]
    static extern int GetDpiForMonitor(IntPtr hMon, int type, out uint dpiX, out uint dpiY);

    static bool? newApi;

    public static Awareness Of(int pid)
    {
        IntPtr h = OpenProcess(QUERY_INFORMATION | QUERY_LIMITED, false, pid);
        if (h == IntPtr.Zero) h = OpenProcess(QUERY_LIMITED, false, pid);
        if (h == IntPtr.Zero) return Awareness.Unknown;
        try
        {
            if (newApi != false)
            {
                try
                {
                    IntPtr ctx = GetDpiAwarenessContextForProcess(h);
                    newApi = true;
                    if (ctx == IntPtr.Zero) return Awareness.Unknown;
                    if (AreDpiAwarenessContextsEqual(ctx, (IntPtr)(-4))) return Awareness.PerMonitorV2;
                    if (AreDpiAwarenessContextsEqual(ctx, (IntPtr)(-5))) return Awareness.GdiScaled;
                    switch (GetAwarenessFromDpiAwarenessContext(ctx))
                    {
                        case 0: return Awareness.Unaware;
                        case 1: return Awareness.System;
                        case 2: return Awareness.PerMonitor;
                        default: return Awareness.Unknown;
                    }
                }
                catch (EntryPointNotFoundException) { newApi = false; }
            }
            int v;
            if (GetProcessDpiAwareness(h, out v) != 0) return Awareness.Unknown;
            return v == 0 ? Awareness.Unaware : v == 1 ? Awareness.System : v == 2 ? Awareness.PerMonitor : Awareness.Unknown;
        }
        catch { return Awareness.Unknown; }
        finally { CloseHandle(h); }
    }

    // Best awareness across all instances (helper processes without windows often report "unaware")
    public static Awareness Best(IEnumerable<int> pids)
    {
        var best = Awareness.Unknown;
        foreach (var pid in pids)
        {
            var a = Of(pid);
            if (a > best) best = a;
        }
        return best;
    }

    // DPI of every connected monitor (96 = 100 %)
    public static List<int> MonitorDpis()
    {
        var list = new List<int>();
        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (m, hdc, r, d) =>
            {
                uint x, y;
                if (GetDpiForMonitor(m, 0 /* effective */, out x, out y) == 0) list.Add((int)x);
                return true;
            }, IntPtr.Zero);
        }
        catch { }
        return list;
    }
}

static class Restarter
{
    const uint QUERY_INFORMATION = 0x0400, QUERY_LIMITED = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr h);
    [DllImport("ntdll.dll")]
    static extern int NtQueryInformationProcess(IntPtr h, int infoClass, IntPtr buffer, int length, out int returnLength);
    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool OpenProcessToken(IntPtr h, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool GetTokenInformation(IntPtr token, int cls, out int info, int len, out int retLen);
    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool DuplicateTokenEx(IntPtr token, uint access, IntPtr attrs, int impersonationLevel, int tokenType, out IntPtr newToken);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CreateProcessWithTokenW(IntPtr token, uint logonFlags, string appName, StringBuilder cmdLine,
        uint flags, IntPtr env, string curDir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
    [DllImport("user32.dll")]
    static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int pid);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct STARTUPINFO
    {
        public int cb; public string lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    // Full command line of a process (Windows 8.1+, ProcessCommandLineInformation = 60)
    public static string CommandLineOf(int pid)
    {
        IntPtr h = OpenProcess(QUERY_LIMITED, false, pid);
        if (h == IntPtr.Zero) return null;
        IntPtr buf = IntPtr.Zero;
        try
        {
            int len;
            NtQueryInformationProcess(h, 60, IntPtr.Zero, 0, out len);
            if (len <= 0) return null;
            buf = Marshal.AllocHGlobal(len);
            if (NtQueryInformationProcess(h, 60, buf, len, out len) != 0) return null;
            int chars = Marshal.ReadInt16(buf) / 2;                 // UNICODE_STRING.Length (bytes)
            IntPtr str = Marshal.ReadIntPtr(buf, IntPtr.Size);       // UNICODE_STRING.Buffer
            return Marshal.PtrToStringUni(str, chars);
        }
        catch { return null; }
        finally { if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf); CloseHandle(h); }
    }

    // Everything after the program path in a command line
    public static string ArgumentsOf(string cmdLine)
    {
        if (string.IsNullOrEmpty(cmdLine)) return "";
        string s = cmdLine.TrimStart();
        int end;
        if (s.StartsWith("\"")) { end = s.IndexOf('"', 1); end = end < 0 ? s.Length : end + 1; }
        else { end = s.IndexOf(' '); if (end < 0) end = s.Length; }
        return s.Substring(end).Trim();
    }

    static bool IsElevated(int pid)
    {
        IntPtr h = OpenProcess(QUERY_LIMITED, false, pid), tok;
        if (h == IntPtr.Zero) return false;
        try
        {
            if (!OpenProcessToken(h, 0x0008 /* TOKEN_QUERY */, out tok)) return false;
            try { int elevated, len; return GetTokenInformation(tok, 20 /* TokenElevation */, out elevated, 4, out len) && elevated != 0; }
            finally { CloseHandle(tok); }
        }
        finally { CloseHandle(h); }
    }

    // Starts a program as the normal (non-elevated) desktop user by borrowing Explorer's token
    static bool StartUnelevated(string exe, string args, out string error)
    {
        error = null;
        int shellPid;
        IntPtr shell = GetShellWindow();
        if (shell == IntPtr.Zero || GetWindowThreadProcessId(shell, out shellPid) == 0) { error = "no desktop shell"; return false; }
        IntPtr hShell = OpenProcess(QUERY_INFORMATION, false, shellPid), tok = IntPtr.Zero, primary = IntPtr.Zero;
        if (hShell == IntPtr.Zero) { error = "can't open shell process"; return false; }
        try
        {
            if (!OpenProcessToken(hShell, 0x0002 /* TOKEN_DUPLICATE */, out tok)) { error = "OpenProcessToken " + Marshal.GetLastWin32Error(); return false; }
            if (!DuplicateTokenEx(tok, 0x018B, IntPtr.Zero, 2 /* impersonation */, 1 /* primary */, out primary)) { error = "DuplicateTokenEx " + Marshal.GetLastWin32Error(); return false; }
            var si = new STARTUPINFO(); si.cb = Marshal.SizeOf(typeof(STARTUPINFO));
            PROCESS_INFORMATION pi;
            var cmd = new StringBuilder("\"" + exe + "\"" + (args.Length > 0 ? " " + args : ""), 32768);
            if (!CreateProcessWithTokenW(primary, 0, exe, cmd, 0, IntPtr.Zero, Path.GetDirectoryName(exe), ref si, out pi))
            { error = "CreateProcessWithTokenW " + Marshal.GetLastWin32Error(); return false; }
            CloseHandle(pi.hThread); CloseHandle(pi.hProcess);
            return true;
        }
        finally
        {
            if (primary != IntPtr.Zero) CloseHandle(primary);
            if (tok != IntPtr.Zero) CloseHandle(tok);
            CloseHandle(hShell);
        }
    }

    // Closes every instance of the program (politely first, then forcefully) and starts it again
    // with the original arguments. Returns null on success, otherwise an error message.
    public static string Restart(string exe, List<int> pids)
    {
        if (pids.Count == 0) return "not running";

        // Remember how the main instance was started: prefer one with a window
        int main = pids[0];
        foreach (var pid in pids)
        {
            try { using (var p = Process.GetProcessById(pid)) if (p.MainWindowHandle != IntPtr.Zero) { main = pid; break; } }
            catch { }
        }
        string args = ArgumentsOf(CommandLineOf(main));
        bool elevated = IsElevated(main);
        Program.Log("restart " + exe + " args=[" + args + "] elevated=" + elevated);

        var procs = new List<Process>();
        foreach (var pid in pids) { try { procs.Add(Process.GetProcessById(pid)); } catch { } }
        try
        {
            foreach (var p in procs) { try { p.CloseMainWindow(); } catch { } }
            var deadline = DateTime.Now.AddSeconds(6);
            foreach (var p in procs)
            {
                try
                {
                    int left = (int)Math.Max(0, (deadline - DateTime.Now).TotalMilliseconds);
                    if (!p.WaitForExit(left)) { p.Kill(); p.WaitForExit(3000); }
                }
                catch { }
            }
            if (procs.Any(p => { try { return !p.HasExited; } catch { return false; } }))
                return "it didn't close";
        }
        finally { foreach (var p in procs) p.Dispose(); }

        Thread.Sleep(400);
        string error = null;
        if (!elevated && StartUnelevated(exe, args, out error)) return null;
        if (error != null) Program.Log("unelevated start failed (" + error + "), starting directly");
        try
        {
            Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) });
            return null;
        }
        catch (Exception ex) { return "couldn't start it again: " + ex.Message; }
    }
}
