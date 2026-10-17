// DPI Override Tool
// Shows running programs plus every program that already has a DPI override,
// and sets "Override high DPI scaling behavior" for ALL USERS (HKLM).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("DPI Override")]
[assembly: System.Reflection.AssemblyProduct("DPI Override")]
[assembly: System.Reflection.AssemblyVersion("2.0.0.0")]

static class Compat
{
    public const string LayersPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
    static readonly string[] DpiFlags = { "HIGHDPIAWARE", "DPIUNAWARE", "GDIDPISCALING" };

    public static readonly string[] Modes = { "Application", "System", "System (Enhanced)", "Not set" };

    static string[] FlagsFor(string mode)
    {
        switch (mode)
        {
            case "Application": return new[] { "HIGHDPIAWARE" };
            case "System": return new[] { "DPIUNAWARE" };
            case "System (Enhanced)": return new[] { "GDIDPISCALING", "DPIUNAWARE" };
            default: return new string[0];
        }
    }

    static bool IsSigil(string t) { return t == "~" || t == "$" || t == "#"; }

    static string[] Tokens(string v)
    {
        return (v ?? "").Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
    }

    public static string ModeOf(string value)
    {
        var t = new HashSet<string>(Tokens(value), StringComparer.OrdinalIgnoreCase);
        if (t.Contains("HIGHDPIAWARE")) return "Application";
        if (t.Contains("GDIDPISCALING") && t.Contains("DPIUNAWARE")) return "System (Enhanced)";
        if (t.Contains("DPIUNAWARE")) return "System";
        return "Not set";
    }

    // Returns the new registry value, or null when the value should be deleted.
    public static string Rewrite(string old, string mode)
    {
        var tokens = Tokens(old);
        var sigils = tokens.Where(IsSigil).ToList();
        var other = tokens.Where(t => !IsSigil(t) && !DpiFlags.Contains(t, StringComparer.OrdinalIgnoreCase)).ToList();
        other.AddRange(FlagsFor(mode));
        if (other.Count == 0) return null;
        if (!sigils.Contains("~")) sigils.Add("~");
        return string.Join(" ", sigils.Concat(other));
    }

    public static RegistryKey Open(RegistryHive hive, bool write)
    {
        var b = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        return write ? b.CreateSubKey(LayersPath) : b.OpenSubKey(LayersPath);
    }

    // All value names (exe paths) in a hive with their raw values
    public static Dictionary<string, string> ReadAll(RegistryHive hive)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using (var k = Open(hive, false))
        {
            if (k == null) return d;
            foreach (var n in k.GetValueNames())
            {
                if (string.IsNullOrEmpty(n)) continue;
                d[n] = Convert.ToString(k.GetValue(n));
            }
        }
        return d;
    }

    public static void Set(RegistryHive hive, string exe, string mode)
    {
        using (var k = Open(hive, true))
        {
            var old = Convert.ToString(k.GetValue(exe));
            var nv = Rewrite(old, mode);
            if (nv != null) k.SetValue(exe, nv, RegistryValueKind.String);
            else if (!string.IsNullOrEmpty(old) || k.GetValue(exe) != null) k.DeleteValue(exe, false);
        }
    }

    public static int SelfTest()
    {
        var cases = new[] {
            new[] { "~ WIN7RTM DPIUNAWARE", "Application", "~ WIN7RTM HIGHDPIAWARE" },
            new[] { "~ HIGHDPIAWARE", "Not set", null },
            new[] { "", "Application", "~ HIGHDPIAWARE" },
            new[] { null, "System (Enhanced)", "~ GDIDPISCALING DPIUNAWARE" },
            new[] { "$ ~ RUNASADMIN", "System", "$ ~ RUNASADMIN DPIUNAWARE" },
            new[] { "~ highdpiaware RUNASADMIN", "Not set", "~ RUNASADMIN" },
        };
        int fail = 0;
        foreach (var c in cases)
        {
            var r = Rewrite(c[0], c[1]);
            bool ok = r == c[2];
            if (!ok) fail++;
            Console.WriteLine((ok ? "OK   " : "FAIL ") + "[" + c[0] + "] " + c[1] + " -> [" + r + "]");
        }
        foreach (var v in new[] { "~ HIGHDPIAWARE", "~ DPIUNAWARE", "~ GDIDPISCALING DPIUNAWARE", "~ RUNASADMIN", "~ highdpiaware" })
            Console.WriteLine("mode [" + v + "] = " + ModeOf(v));
        return fail;
    }
}

static class Procs
{
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder name, ref int size);

    public static string PathOf(int pid)
    {
        IntPtr h = OpenProcess(0x1000 /* QUERY_LIMITED_INFORMATION */, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString(0, size) : null;
        }
        finally { CloseHandle(h); }
    }

    // exe path -> process ids of all running instances
    public static Dictionary<string, List<int>> Running()
    {
        var map = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        int self = Process.GetCurrentProcess().Id;
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == self) continue;
                var path = PathOf(p.Id);
                if (string.IsNullOrEmpty(path)) continue;
                List<int> pids;
                if (!map.TryGetValue(path, out pids)) { pids = new List<int>(); map[path] = pids; }
                pids.Add(p.Id);
            }
            catch { }
            finally { p.Dispose(); }
        }
        return map;
    }
}

// Groups related programs by their install folder, e.g. everything under "C:\Riot Games\League of Legends".
static class Grouping
{
    static bool IsSep(char c) { return c == '\\' || c == '/'; }

    static int NextSep(string p, int from)
    {
        for (int i = from; i < p.Length; i++) if (IsSep(p[i])) return i;
        return -1;
    }

    // Segments of a path as [start, end) pairs: [0] = root ("C:", "\\server\share" or "" for "/..."),
    // the last one is the file name
    static List<int[]> Segments(string p)
    {
        var segs = new List<int[]>();
        int i;
        if (p.Length >= 2 && IsSep(p[0]) && IsSep(p[1]))
        {
            int a = NextSep(p, 2);
            int b = a < 0 ? -1 : NextSep(p, a + 1);
            i = b < 0 ? p.Length : b;
        }
        else { i = NextSep(p, 0); if (i < 0) i = p.Length; }
        segs.Add(new[] { 0, i });
        while (i < p.Length)
        {
            int start = i + 1, end = NextSep(p, start);
            if (end < 0) end = p.Length;
            if (end > start) segs.Add(new[] { start, end });
            i = end;
        }
        return segs;
    }

    // Number of leading folders that hold many unrelated programs (Program Files, AppData\Local, Steam's library, ...)
    static int ContainerDepth(string[] s, int dirs)
    {
        Func<int, string, bool> at = (k, v) => k < dirs && string.Equals(s[k], v, StringComparison.OrdinalIgnoreCase);
        int i = 1;
        if (at(i, "Program Files") || at(i, "Program Files (x86)") || at(i, "ProgramData")) { i++; if (at(i, "WindowsApps")) i++; }
        else if (at(i, "Users") && i + 1 < dirs)
        {
            i += 2;
            if (at(i, "AppData"))
            {
                i++;
                if (at(i, "Local")) { i++; if (at(i, "Programs")) i++; }
                else if (at(i, "Roaming") || at(i, "LocalLow")) i++;
            }
            else if (at(i, "Desktop") || at(i, "Downloads") || at(i, "Documents")) i++;
        }
        else if (at(i, "Windows")) i++;
        for (int k = i; k + 1 < dirs; k++)
            if (at(k, "steamapps") && at(k + 1, "common")) i = k + 2;
        return i;
    }

    class Item { public string Path; public string[] Names; public List<int[]> Segs; public int Dirs; }

    static string Prefix(Item it, int count) { return it.Path.Substring(0, it.Segs[count - 1][1]); }

    // Splits a bucket of programs that share their first `depth` segments. A bucket becomes a group when one
    // of its programs sits directly in its folder; otherwise it's split by the next folder level.
    static void Split(List<Item> items, int depth, Dictionary<string, string> result)
    {
        if (items.Count < 2) return;
        if (items.Any(it => it.Dirs == depth))
        {
            string root = Prefix(items[0], depth);
            foreach (var it in items) result[it.Path] = root;
            return;
        }
        foreach (var sub in items.GroupBy(it => it.Names[depth], StringComparer.OrdinalIgnoreCase))
            Split(sub.ToList(), depth + 1, result);
    }

    static string Norm(string dir) { return dir.TrimEnd(new[] { '\\', '/' }); }

    // exe path -> group folder, for programs that share an install folder with at least one other program.
    // Folders the user added as a whole are used as groups as they are.
    public static Dictionary<string, string> Roots(IEnumerable<string> paths, IEnumerable<string> fixedRoots)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var roots = fixedRoots.Select(Norm).Where(r => r.Length > 0).OrderByDescending(r => r.Length).ToList();
        var fixedGroups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var buckets = new Dictionary<string, List<Item>>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string root = roots.FirstOrDefault(r => p.Length > r.Length + 1 && IsSep(p[r.Length]) &&
                                                    p.StartsWith(r, StringComparison.OrdinalIgnoreCase));
            if (root != null)
            {
                List<string> l;
                if (!fixedGroups.TryGetValue(root, out l)) fixedGroups[root] = l = new List<string>();
                l.Add(p);
                continue;
            }
            var segs = Segments(p);
            var it = new Item { Path = p, Segs = segs, Names = segs.Select(s => p.Substring(s[0], s[1] - s[0])).ToArray(), Dirs = segs.Count - 1 };
            int c = ContainerDepth(it.Names, it.Dirs);
            if (it.Dirs <= c) continue;          // directly inside Program Files, Downloads, ...: unrelated programs
            string key = Prefix(it, c + 1);
            List<Item> b;
            if (!buckets.TryGetValue(key, out b)) buckets[key] = b = new List<Item>();
            b.Add(it);
        }
        foreach (var b in buckets.Values) Split(b, ContainerDepth(b[0].Names, b[0].Dirs) + 1, result);
        foreach (var kv in fixedGroups)
            if (kv.Value.Count > 1) foreach (var p in kv.Value) result[p] = kv.Key;
        return result;
    }

    // File name and folder of a path; unlike System.IO.Path these also split Windows paths when testing under Mono
    public static string FileName(string path)
    {
        int i = path.LastIndexOfAny(new[] { '\\', '/' });
        return i < 0 ? path : path.Substring(i + 1);
    }

    public static string FolderOf(string path)
    {
        int i = path.LastIndexOfAny(new[] { '\\', '/' });
        return i <= 0 ? path : path.Substring(0, i);
    }

    // Path below a group folder, e.g. "Game\League of Legends.exe"
    public static string Relative(string path, string root)
    {
        root = Norm(root);
        return path.Length > root.Length + 1 && path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? path.Substring(root.Length + 1) : FileName(path);
    }

    public static int Depth(string path) { return Segments(path).Count; }

    // Last folder name of a group root, e.g. "League of Legends"
    public static string NameOf(string root)
    {
        var segs = Segments(root);
        var last = segs[segs.Count - 1];
        string n = root.Substring(last[0], last[1] - last[0]);
        return n.Length > 0 ? n : root;
    }

    // Every .exe in a folder and its subfolders (skips folders it can't read and junctions)
    public static List<string> FindExes(string dir, int max)
    {
        var found = new List<string>();
        var todo = new List<string> { dir };     // not Stack<T>: Mono has it in mscorlib, .NET Framework in System.dll
        while (todo.Count > 0 && found.Count < max)
        {
            string d = todo[todo.Count - 1];
            todo.RemoveAt(todo.Count - 1);
            try
            {
                foreach (var f in Directory.GetFiles(d))
                    if (f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) { found.Add(f); if (found.Count >= max) break; }
                foreach (var sub in Directory.GetDirectories(d))
                {
                    try { if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0) continue; }
                    catch { continue; }
                    todo.Add(sub);
                }
            }
            catch (Exception ex) { Program.Log("scan skipped " + d + ": " + ex.Message); }
        }
        found.Sort(StringComparer.OrdinalIgnoreCase);
        return found;
    }

    public static int SelfTest()
    {
        var paths = new[] {
            @"C:\Riot Games\League of Legends\LeagueClient.exe",
            @"C:\Riot Games\League of Legends\LeagueClientUx.exe",
            @"C:\Riot Games\League of Legends\Game\League of Legends.exe",
            @"C:\Riot Games\Riot Client\RiotClientServices.exe",
            @"C:\Riot Games\Riot Client\UX\RiotClientUx.exe",
            @"C:\Program Files (x86)\Steam\steam.exe",
            @"C:\Program Files (x86)\Steam\bin\cef\cef.win7x64\steamwebhelper.exe",
            @"C:\Program Files (x86)\Steam\steamapps\common\Portal 2\portal2.exe",
            @"C:\Program Files (x86)\Steam\steamapps\common\Portal 2\bin\vconsole2.exe",
            @"C:\Program Files (x86)\Steam\steamapps\common\Celeste\Celeste.exe",
            @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Microsoft\EdgeWebView\Application\msedgewebview2.exe",
            @"C:\Users\tobi\Downloads\setup.exe",
            @"C:\Users\tobi\Downloads\other-setup.exe",
            @"C:\Users\tobi\AppData\Local\Discord\Update.exe",
            @"C:\Users\tobi\AppData\Local\Discord\app-1.0\Discord.exe",
            @"C:\Users\tobi\AppData\Local\Programs\Notes\notes.exe",
            @"D:\Tools\a.exe",
            @"D:\Tools\b.exe",
            @"D:\Mods\x\y\z.exe",
            @"D:\Mods\x\y\w.exe",
        };
        var expect = new Dictionary<string, string> {
            { paths[0], @"C:\Riot Games\League of Legends" }, { paths[1], @"C:\Riot Games\League of Legends" },
            { paths[2], @"C:\Riot Games\League of Legends" },
            { paths[3], @"C:\Riot Games\Riot Client" }, { paths[4], @"C:\Riot Games\Riot Client" },
            { paths[5], @"C:\Program Files (x86)\Steam" }, { paths[6], @"C:\Program Files (x86)\Steam" },
            { paths[7], @"C:\Program Files (x86)\Steam\steamapps\common\Portal 2" },
            { paths[8], @"C:\Program Files (x86)\Steam\steamapps\common\Portal 2" },
            { paths[9], null }, { paths[10], null }, { paths[11], null }, { paths[12], null }, { paths[13], null },
            { paths[14], @"C:\Users\tobi\AppData\Local\Discord" }, { paths[15], @"C:\Users\tobi\AppData\Local\Discord" },
            { paths[16], null },
            { paths[17], @"D:\Tools" }, { paths[18], @"D:\Tools" },
            { paths[19], @"D:\Mods\x\y" }, { paths[20], @"D:\Mods\x\y" },
        };
        int fail = 0;
        var r = Roots(paths, new string[0]);
        foreach (var kv in expect)
        {
            string got; r.TryGetValue(kv.Key, out got);
            bool ok = got == kv.Value;
            if (!ok) fail++;
            Console.WriteLine((ok ? "OK   " : "FAIL ") + "group [" + kv.Key + "] -> [" + got + "]");
        }
        // A folder added as a whole is one group, even across what would otherwise be separate groups
        r = Roots(paths, new[] { @"C:\Riot Games\" });
        bool fx = r[paths[0]] == @"C:\Riot Games" && r[paths[4]] == @"C:\Riot Games";
        if (!fx) fail++;
        Console.WriteLine((fx ? "OK   " : "FAIL ") + "fixed root C:\\Riot Games");
        bool nm = NameOf(@"C:\Riot Games\League of Legends") == "League of Legends" && NameOf(@"\\nas\games") == @"\\nas\games";
        if (!nm) fail++;
        Console.WriteLine((nm ? "OK   " : "FAIL ") + "group names");
        return fail;
    }
}

class Entry
{
    public string Path, Name, Display, AllUsers, CurrentUser;
    public bool System;
    public List<int> Pids = new List<int>();
    public Awareness Rendering = Awareness.Unknown;
    public bool Running { get { return Pids.Count > 0; } }
}

// Programs that share an install folder, shown as one expandable row
class Group
{
    public string Root, Name;
    public List<Entry> Members = new List<Entry>();
    public Entry Lead;   // the program whose icon the row shows
    public string Key { get { return "group:" + Root; } }
    public int RunningCount { get { return Members.Count(m => m.Running); } }
}

static class Program
{
    [DllImport("user32.dll")]
    static extern bool SetProcessDPIAware();

    static bool IsAdmin()
    {
        var id = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    static string LogPath;
    public static bool Demo;
    public static List<string> StartPaths = new List<string>();   // .exe files or folders to add on start
    public static void Log(string msg)
    {
        try { File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss.fff") + "  [" + (IsAdminSafe() ? "admin" : "user") + "] " + msg + Environment.NewLine); } catch { }
    }
    static bool IsAdminSafe() { try { return IsAdmin(); } catch { return false; } }

    static void Crash(Exception ex)
    {
        Log("CRASH: " + ex);
        try { MessageBox.Show("DPI Override hit an error:\n\n" + ex.Message + "\n\nDetails were written to:\n" + LogPath, "DPI Override", MessageBoxButtons.OK, MessageBoxIcon.Error); } catch { }
    }

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--selftest") return Compat.SelfTest() + Grouping.SelfTest();
        Demo = args.Contains("--demo");
        // full paths, because the elevated copy starts in System32
        StartPaths = args.Where(a => !a.StartsWith("--")).Select(a => { try { return System.IO.Path.GetFullPath(a); } catch { return a; } }).ToList();
        try { LogPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.ExecutablePath), "DpiOverride.log"); }
        catch { LogPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DpiOverride.log"); }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (s, e) => Crash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (s, e) => Crash(e.ExceptionObject as Exception ?? new Exception("unknown"));
        Log("start, .NET " + Environment.Version + ", OS " + Environment.OSVersion + ", 64bit=" + Environment.Is64BitProcess);
        try { return Run(); }
        catch (Exception ex) { Crash(ex); return 1; }
    }

    static int Run()
    {
        bool onWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;

        // Writing the all-users setting needs admin rights: relaunch elevated (UAC prompt)
        if (onWindows && !IsAdmin())
        {
            try
            {
                Log("not admin, relaunching elevated");
                string args = string.Join(" ", StartPaths.Select(a => "\"" + a + (a.EndsWith("\\", StringComparison.Ordinal) ? "\\" : "") + "\""));
                Process.Start(new ProcessStartInfo(Application.ExecutablePath, args) { Verb = "runas", UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log("elevation failed: " + ex.Message);
                MessageBox.Show("Administrator rights are needed to change the setting for all users.",
                                "DPI Override", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            return 0;
        }
        if (onWindows) { try { SetProcessDPIAware(); } catch { } }
        Log("creating window");
        var form = new MainForm();
        Log("showing window");
        Application.Run(form);
        Log("closed");
        return 0;
    }
}
