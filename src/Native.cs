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
    [DllImport("user32.dll")]
    static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);                  // Windows 10 1607+

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

    // Processes that show a visible top-level window
    delegate bool WindowEnumProc(IntPtr hWnd, IntPtr data);
    [DllImport("user32.dll")]
    static extern bool EnumWindows(WindowEnumProc proc, IntPtr data);
    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int pid);

    public static HashSet<int> WindowOwners()
    {
        var set = new HashSet<int>();
        try
        {
            EnumWindows((w, d) =>
            {
                int pid;
                if (IsWindowVisible(w) && GetWindowThreadProcessId(w, out pid) != 0) set.Add(pid);
                return true;
            }, IntPtr.Zero);
        }
        catch { }
        return set;
    }

    // DPI of every connected monitor (96 = 100 %). This process is only system aware, which makes Windows report
    // the main display's DPI for every monitor, so the thread switches to per-monitor awareness while it asks.
    public static List<int> MonitorDpis()
    {
        var list = new List<int>();
        IntPtr old = IntPtr.Zero;
        try { old = SetThreadDpiAwarenessContext((IntPtr)(-4) /* PER_MONITOR_AWARE_V2 */); }
        catch (EntryPointNotFoundException) { }
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
        finally { if (old != IntPtr.Zero) SetThreadDpiAwarenessContext(old); }
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

    // Process id of the process that started this one, or 0
    static int ParentOf(int pid)
    {
        IntPtr h = OpenProcess(QUERY_LIMITED, false, pid);
        if (h == IntPtr.Zero) return 0;
        int size = IntPtr.Size * 6;                                  // PROCESS_BASIC_INFORMATION
        IntPtr buf = Marshal.AllocHGlobal(size);
        try
        {
            int len;
            if (NtQueryInformationProcess(h, 0 /* ProcessBasicInformation */, buf, size, out len) != 0) return 0;
            return (int)Marshal.ReadIntPtr(buf, IntPtr.Size * 5).ToInt64();   // InheritedFromUniqueProcessId
        }
        catch { return 0; }
        finally { Marshal.FreeHGlobal(buf); CloseHandle(h); }
    }

    // The instance whose command line restarts the program: one that wasn't started by another instance
    // (helpers like "--type=renderer" are), preferring one with a window
    internal static int MainOf(List<int> pids)
    {
        var roots = pids.Where(pid => !pids.Contains(ParentOf(pid))).ToList();
        if (roots.Count == 0) roots = pids;
        foreach (var pid in roots)
        {
            try { using (var p = Process.GetProcessById(pid)) if (p.MainWindowHandle != IntPtr.Zero) return pid; }
            catch { }
        }
        return roots[0];
    }

    public class Job
    {
        public string Exe, Display;
        public List<int> Pids;
        internal int Main, Parent;
        internal string Args;
        internal bool Elevated;
    }

    // Closes every instance of the programs (politely first, then forcefully) and starts them again with their
    // original arguments. A program that was started by another one in the list (e.g. a game client's helper)
    // is left for that program to start again. So is a background helper (no window, no processes of its own,
    // e.g. DiscordSystemHelper) when the list has a real program: those start their helpers themselves, with
    // fresh arguments. Returns the errors as "name: message".
    public static List<string> RestartAll(List<Job> jobs)
    {
        var errors = new List<string>();
        jobs = jobs.Where(j => j.Pids.Count > 0).ToList();
        var starts = Plan(jobs);

        var procs = new List<KeyValuePair<Job, Process>>();
        foreach (var j in jobs)
            foreach (var pid in j.Pids) { try { procs.Add(new KeyValuePair<Job, Process>(j, Process.GetProcessById(pid))); } catch { } }
        var failed = new HashSet<Job>();
        try
        {
            foreach (var kv in procs) { try { kv.Value.CloseMainWindow(); } catch { } }
            var deadline = DateTime.Now.AddSeconds(6);
            foreach (var kv in procs)
            {
                try
                {
                    int left = (int)Math.Max(0, (deadline - DateTime.Now).TotalMilliseconds);
                    if (!kv.Value.WaitForExit(left)) { kv.Value.Kill(); kv.Value.WaitForExit(3000); }
                }
                catch { }
            }
            foreach (var kv in procs)
            {
                bool alive;
                try { alive = !kv.Value.HasExited; } catch { alive = false; }
                if (alive && failed.Add(kv.Key)) errors.Add(kv.Key.Display + ": it didn't close");
            }
        }
        finally { foreach (var kv in procs) kv.Value.Dispose(); }

        Thread.Sleep(400);
        foreach (var j in starts)
        {
            if (failed.Contains(j)) continue;
            string error = null;
            if (!j.Elevated && StartUnelevated(j.Exe, j.Args, out error)) continue;
            if (error != null) Program.Log("unelevated start failed (" + error + "), starting directly");
            try { Process.Start(new ProcessStartInfo(j.Exe, j.Args) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(j.Exe) }); }
            catch (Exception ex) { errors.Add(j.Display + ": couldn't start it again: " + ex.Message); }
        }
        return errors;
    }

    // Reads how each program was started and returns the ones to start again (see RestartAll)
    internal static List<Job> Plan(List<Job> jobs)
    {
        var windows = DpiInfo.WindowOwners();
        var primary = new HashSet<Job>();
        foreach (var j in jobs)
        {
            j.Main = MainOf(j.Pids);
            j.Args = ArgumentsOf(CommandLineOf(j.Main));
            j.Elevated = IsElevated(j.Main);
            j.Parent = ParentOf(j.Main);
            if (j.Pids.Any(windows.Contains) || j.Pids.Any(pid => pid != j.Main && ParentOf(pid) == j.Main)) primary.Add(j);
        }
        foreach (var j in jobs)
            if (jobs.Any(o => o != j && j.Pids.Contains(o.Parent))) primary.Add(j);   // it starts another program in the list
        var starts = jobs.Where(j => !jobs.Any(o => o != j && o.Pids.Contains(j.Parent))).ToList();
        var helpers = starts.Any(primary.Contains) ? starts.Where(j => !primary.Contains(j)).ToList() : new List<Job>();
        starts = starts.Except(helpers).ToList();
        foreach (var j in jobs)
            Program.Log("restart " + j.Exe + " args=[" + j.Args + "] elevated=" + j.Elevated +
                        (starts.Contains(j) ? "" : helpers.Contains(j) ? " (background helper, not starting it)"
                                                                       : " (started by another program in the list, not starting it)"));
        return starts;
    }
}

// Files dropped from Explorer. The app runs elevated, and Windows blocks OLE drag and drop from the normal-user
// Explorer into it. The older WM_DROPFILES message still works when the window lets it (and the two messages that
// carry the file list) through the elevation filter.
static class FileDrop
{
    public const int WM_DROPFILES = 0x0233;

    [DllImport("shell32.dll")]
    static extern void DragAcceptFiles(IntPtr hWnd, bool accept);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern uint DragQueryFile(IntPtr hDrop, uint index, StringBuilder file, int size);
    [DllImport("shell32.dll")]
    static extern void DragFinish(IntPtr hDrop);
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool ChangeWindowMessageFilterEx(IntPtr hWnd, uint msg, uint action, IntPtr info);   // Windows 7+

    public static void Enable(IntPtr hWnd)
    {
        try
        {
            foreach (uint msg in new uint[] { WM_DROPFILES, 0x004A /* WM_COPYDATA */, 0x0049 /* WM_COPYGLOBALDATA */ })
                if (!ChangeWindowMessageFilterEx(hWnd, msg, 1 /* MSGFLT_ALLOW */, IntPtr.Zero))
                    Program.Log("message filter for 0x" + msg.ToString("X") + " failed: " + Marshal.GetLastWin32Error());
        }
        catch (EntryPointNotFoundException) { }
        DragAcceptFiles(hWnd, true);
    }

    // The dropped paths; frees the drop handle
    public static List<string> Paths(IntPtr hDrop)
    {
        var paths = new List<string>();
        try
        {
            uint count = DragQueryFile(hDrop, 0xFFFFFFFF, null, 0);
            for (uint i = 0; i < count; i++)
            {
                var sb = new StringBuilder((int)DragQueryFile(hDrop, i, null, 0) + 1);
                DragQueryFile(hDrop, i, sb, sb.Capacity);
                paths.Add(sb.ToString());
            }
        }
        finally { DragFinish(hDrop); }
        return paths;
    }
}

// Target of a Windows shortcut (.lnk)
static class Shortcut
{
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    class ShellLink { }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int size, IntPtr findData, uint flags);
    }

    // The file or folder the shortcut points to, or null (e.g. shortcuts to Store apps or installer-advertised programs)
    public static string Target(string lnk)
    {
        object link = null;
        try
        {
            link = new ShellLink();
            ((System.Runtime.InteropServices.ComTypes.IPersistFile)link).Load(lnk, 0 /* STGM_READ */);
            var sb = new StringBuilder(32768);
            ((IShellLinkW)link).GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
            string target = Environment.ExpandEnvironmentVariables(sb.ToString());
            return target.Length > 0 ? target : null;
        }
        catch (Exception ex)
        {
            Program.Log("couldn't read shortcut " + lnk + ": " + ex.Message);
            return null;
        }
        finally { if (link != null) Marshal.ReleaseComObject(link); }
    }
}

// Folder picker: the modern Explorer-style dialog, or the classic tree dialog when that isn't available
static class FolderPicker
{
    [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
    class FileOpenDialog { }

    [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IFileDialog
    {
        [PreserveSig] int Show(IntPtr parent);                       // IModalWindow
        void SetFileTypes(uint count, IntPtr specs);
        void SetFileTypeIndex(uint index);
        void GetFileTypeIndex(out uint index);
        void Advise(IntPtr events, out uint cookie);
        void Unadvise(uint cookie);
        void SetOptions(uint fos);
        void GetOptions(out uint fos);
        void SetDefaultFolder(IShellItem item);
        void SetFolder(IShellItem item);
        void GetFolder(out IShellItem item);
        void GetCurrentSelection(out IShellItem item);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        void GetResult(out IShellItem item);
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint sigdn, [MarshalAs(UnmanagedType.LPWStr)] out string name);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IShellItem item);

    // Returns the chosen folder, or null when cancelled
    public static string Pick(System.Windows.Forms.IWin32Window owner, string title, string start)
    {
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            try { return PickModern(owner, title, start); }
            catch (Exception ex) { Program.Log("modern folder dialog failed: " + ex.Message); }
        }
        using (var d = new System.Windows.Forms.FolderBrowserDialog { Description = title, ShowNewFolderButton = false })
        {
            if (!string.IsNullOrEmpty(start) && Directory.Exists(start)) d.SelectedPath = start;
            return d.ShowDialog(owner) == System.Windows.Forms.DialogResult.OK ? d.SelectedPath : null;
        }
    }

    static string PickModern(System.Windows.Forms.IWin32Window owner, string title, string start)
    {
        var dlg = (IFileDialog)new FileOpenDialog();
        try
        {
            uint fos;
            dlg.GetOptions(out fos);
            dlg.SetOptions(fos | 0x20 /* PICKFOLDERS */ | 0x40 /* FORCEFILESYSTEM */ | 0x800 /* PATHMUSTEXIST */);
            dlg.SetTitle(title);
            if (!string.IsNullOrEmpty(start) && Directory.Exists(start))
            {
                try
                {
                    IShellItem folder;
                    SHCreateItemFromParsingName(start, IntPtr.Zero, typeof(IShellItem).GUID, out folder);
                    dlg.SetFolder(folder);
                }
                catch { }
            }
            int hr = dlg.Show(owner != null ? owner.Handle : IntPtr.Zero);
            if (hr == unchecked((int)0x800704C7)) return null;           // cancelled
            if (hr != 0) throw new COMException("folder dialog", hr);
            IShellItem item;
            dlg.GetResult(out item);
            string path;
            item.GetDisplayName(0x80058000 /* SIGDN_FILESYSPATH */, out path);
            return path;
        }
        finally { Marshal.ReleaseComObject(dlg); }
    }
}
