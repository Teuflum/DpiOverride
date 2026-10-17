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

    // The raw value for a program, or null when there's none
    public static string Raw(RegistryHive hive, string exe)
    {
        using (var k = Open(hive, false))
        {
            if (k == null) return null;
            var v = k.GetValue(exe);
            return v == null ? null : Convert.ToString(v);
        }
    }

    // Puts a raw value back exactly as it was; null deletes it
    public static void SetRaw(RegistryHive hive, string exe, string value)
    {
        using (var k = Open(hive, true))
        {
            if (value != null) k.SetValue(exe, value, RegistryValueKind.String);
            else k.DeleteValue(exe, false);
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

// One registry change that can be undone: the exact values before and after
class UndoStep
{
    public class Change
    {
        public RegistryHive Hive;
        public string Exe, Before, After;
    }

    public string Label;
    public List<Change> Changes = new List<Change>();

    // Remembers the current values of these programs in both hives, before they're changed
    public static UndoStep Begin(string label, IEnumerable<string> exes)
    {
        var step = new UndoStep { Label = label };
        foreach (var exe in exes.Distinct(StringComparer.OrdinalIgnoreCase))
            foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
                step.Changes.Add(new Change { Hive = hive, Exe = exe, Before = Compat.Raw(hive, exe) });
        return step;
    }

    // Reads the values after the change and keeps only the ones that changed; false when nothing did
    public bool End()
    {
        foreach (var c in Changes) c.After = Compat.Raw(c.Hive, c.Exe);
        Changes = Changes.Where(c => c.Before != c.After).ToList();
        return Changes.Count > 0;
    }

    // Puts every value back; returns the errors
    public List<string> Revert()
    {
        var errors = new List<string>();
        foreach (var c in Changes)
        {
            try { Compat.SetRaw(c.Hive, c.Exe, c.Before); }
            catch (Exception ex) { errors.Add(Grouping.FileName(c.Exe) + ": " + ex.Message); }
        }
        return errors;
    }
}

// Backups of the DPI overrides as a .reg file, so a backup can also be restored by double-clicking it.
// Reading one only looks at the two Layers sections, so a .reg file with other keys can't change anything else.
static class Backup
{
    public const string LmKey = @"HKEY_LOCAL_MACHINE\" + Compat.LayersPath;
    public const string CuKey = @"HKEY_CURRENT_USER\" + Compat.LayersPath;

    public class Item
    {
        public bool AllUsers;          // HKLM, otherwise HKCU
        public string Exe, Value;
    }

    static string Quote(string s) { return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""; }

    // Values with a DPI mode, both hives
    public static string Write(IEnumerable<Item> items)
    {
        var sb = new StringBuilder("Windows Registry Editor Version 5.00\r\n");
        foreach (bool lm in new[] { true, false })
        {
            var part = items.Where(i => i.AllUsers == lm).OrderBy(i => i.Exe, StringComparer.OrdinalIgnoreCase).ToList();
            if (part.Count == 0) continue;
            sb.Append("\r\n[" + (lm ? LmKey : CuKey) + "]\r\n");
            foreach (var i in part) sb.Append(Quote(i.Exe) + "=" + Quote(i.Value) + "\r\n");
        }
        return sb.ToString();
    }

    // Reads a quoted .reg string starting at s[at]; returns null when it isn't one
    static string Unquote(string s, ref int at)
    {
        if (at >= s.Length || s[at] != '"') return null;
        var sb = new StringBuilder();
        for (int i = at + 1; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\\' && i + 1 < s.Length) { sb.Append(s[++i]); continue; }
            if (c == '"') { at = i + 1; return sb.ToString(); }
            sb.Append(c);
        }
        return null;
    }

    // String values from the Layers sections that set a DPI mode
    public static List<Item> Read(string text)
    {
        var items = new List<Item>();
        bool? lm = null;
        foreach (var raw in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            string line = raw.Trim();
            if (line.StartsWith("["))
            {
                string key = line.Trim('[', ']');
                lm = string.Equals(key, LmKey, StringComparison.OrdinalIgnoreCase) ? true
                   : string.Equals(key, CuKey, StringComparison.OrdinalIgnoreCase) ? false : (bool?)null;
                continue;
            }
            if (lm == null) continue;
            int at = 0;
            string exe = Unquote(line, ref at);
            if (exe == null || at >= line.Length || line[at] != '=') continue;
            at++;
            string value = Unquote(line, ref at);
            if (value == null || exe.Length == 0 || Compat.ModeOf(value) == "Not set") continue;
            items.Add(new Item { AllUsers = lm.Value, Exe = exe, Value = value });
        }
        return items;
    }

    public static int SelfTest()
    {
        var items = new List<Item> {
            new Item { AllUsers = true, Exe = @"C:\Games\My ""Quoted"" Game\game.exe", Value = "~ RUNASADMIN HIGHDPIAWARE" },
            new Item { AllUsers = true, Exe = @"\\nas\tools\a.exe", Value = "~ DPIUNAWARE" },
            new Item { AllUsers = false, Exe = @"C:\Tools\b.exe", Value = "~ GDIDPISCALING DPIUNAWARE" },
        };
        string text = Write(items) +
            "\r\n[HKEY_LOCAL_MACHINE\\SOFTWARE\\Evil]\r\n\"C:\\\\x.exe\"=\"~ HIGHDPIAWARE\"\r\n" +   // other keys are ignored
            "\r\n[" + LmKey + "]\r\n\"C:\\\\c.exe\"=\"~ WIN7RTM\"\r\n\"C:\\\\d.exe\"=dword:00000001\r\n";   // no DPI mode, not a string
        var back = Read(text);
        bool ok = back.Count == 3 && items.All(i => back.Any(b => b.AllUsers == i.AllUsers && b.Exe == i.Exe && b.Value == i.Value));
        Console.WriteLine((ok ? "OK   " : "FAIL ") + "backup round trip (" + back.Count + " items)");
        bool lf = Read(text.Replace("\r\n", "\n")).Count == 3;
        Console.WriteLine((lf ? "OK   " : "FAIL ") + "backup with LF line ends");
        return (ok ? 0 : 1) + (lf ? 0 : 1);
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

    // Folder names that say nothing about the program they hold
    static readonly HashSet<string> Generic = new HashSet<string>(new[] {
        "bin", "bin32", "bin64", "app", "apps", "application", "x64", "x86", "x86_64", "amd64", "arm64", "win32", "win64",
        "client", "platform", "current", "program", "programs", "release", "runtime", "ux", "scripts", "cache", "archive" },
        StringComparer.OrdinalIgnoreCase);

    // Generated ids rather than names: "20054CA46072C", "f0584be0fbaf", "A4RDcrnTH6hbii9a"
    static bool IsId(string s)
    {
        if (s.Length < 8 || !s.Any(char.IsDigit)) return false;
        if (s.All(Uri.IsHexDigit)) return true;
        return s.Length >= 12 && s.Any(char.IsUpper) && s.Any(char.IsLower) && s.All(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_');
    }

    // "132.0.0.0", "4.18.26090.9-0", "v2.1"
    static bool IsVersion(string s)
    {
        if (s.Length > 1 && (s[0] == 'v' || s[0] == 'V')) s = s.Substring(1);
        return s.Length > 0 && char.IsDigit(s[0]) && s.All(ch => char.IsDigit(ch) || ch == '.' || ch == '-' || ch == '_');
    }

    // A folder name without a version suffix ("app-1.0.9262" -> "app"); for a Store package folder
    // ("KDEe.V.KDEConnect_26.801.0_x64__7vt06qxq7ptv8", "eMClient.20054CA46072C_11.0_x64__veq4") just the app's
    // name ("KDEConnect", "eMClient")
    static string CleanName(string seg, bool package)
    {
        if (package)
        {
            int u = seg.IndexOf('_');
            if (u > 0) seg = seg.Substring(0, u);
            var parts = seg.Split('.').Where(x => x.Length > 0 && !IsId(x)).ToList();
            if (parts.Count > 0) seg = parts[parts.Count - 1];
        }
        int cut = seg.LastIndexOfAny(new[] { '-', ' ' });
        if (cut > 0 && IsVersion(seg.Substring(cut + 1))) seg = seg.Substring(0, cut);
        return seg.Trim();
    }

    // Name shown for a group: the nearest folder of its root that names the program, skipping version and
    // generic folders, e.g. "...\Discord\app-1.0.9262" -> "Discord", "...\Drive File Stream\132.0.0.0" -> "Drive File Stream"
    public static string GroupName(string root)
    {
        root = Norm(root);
        var segs = Segments(root);
        var names = segs.Select(s => root.Substring(s[0], s[1] - s[0])).ToArray();
        int c = ContainerDepth(names, names.Length);
        for (int k = names.Length - 1; k >= Math.Max(1, c); k--)
        {
            bool package = string.Equals(names[k - 1], "WindowsApps", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(names[k - 1], "SystemApps", StringComparison.OrdinalIgnoreCase);
            string n = CleanName(names[k], package);
            if (n.Length > 0 && !IsVersion(n) && !IsId(n) && !Generic.Contains(n)) return n;
        }
        return NameOf(root);
    }

    // Last folder name of a group root, e.g. "League of Legends"
    public static string NameOf(string root)
    {
        var segs = Segments(root);
        var last = segs[segs.Count - 1];
        string n = root.Substring(last[0], last[1] - last[0]);
        return n.Length > 0 ? n : root;
    }

    // Squirrel installs (Discord, Slack, GitHub Desktop, ...) put a launcher stub next to "app-<version>" folders that
    // hold the real program, and shortcuts point at the stub. Returns the newest real program, or the path unchanged.
    public static string RealProgram(string exe)
    {
        try
        {
            string dir = FolderOf(exe), name = FileName(exe);
            Version best = null; string found = null;
            foreach (var sub in Directory.GetDirectories(dir, "app-*"))
            {
                Version v;
                string candidate = Path.Combine(sub, name);
                if (!File.Exists(candidate)) continue;
                try { v = new Version(FileName(sub).Substring(4)); } catch { v = new Version(0, 0); }
                if (best == null || v > best) { best = v; found = candidate; }
            }
            return found ?? exe;
        }
        catch { return exe; }
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
        var names = new[] {
            new[] { @"C:\Program Files\Google\Drive File Stream\132.0.0.0", "Drive File Stream" },
            new[] { @"C:\ProgramData\Microsoft\Windows Defender\Platform\4.18.26090.9-0", "Windows Defender" },
            new[] { @"C:\Users\tobi\AppData\Local\Discord\app-1.0.9262", "Discord" },
            new[] { @"C:\Users\tobi\AppData\Local\hermes\bin", "hermes" },
            new[] { @"C:\Program Files\WindowsApps\KDEe.V.KDEConnect_26.801.6731.0_x64__7vt06qxq7ptv8\bin", "KDEConnect" },
            new[] { @"C:\Program Files\WindowsApps\Claude_2.42304.0.0_x64__pzs8sxrjxfjjc\app", "Claude" },
            new[] { @"C:\Program Files\WindowsApps\eMClient.20054CA46072C_11.0.1201.0_x64__veq4nz3ze5v72", "eMClient" },
            new[] { @"C:\Program Files\WindowsApps\5319275A.WhatsAppDesktop_2.2640.100.0_x64__cv1g1gvanyjgm", "WhatsAppDesktop" },
            new[] { @"C:\Users\tobi\AppData\Local\uv\cache\archive-v0\A4RDcrnTH6hbii9a\Scripts", "uv" },
            new[] { @"C:\Riot Games\League of Legends", "League of Legends" },
            new[] { @"C:\Program Files (x86)\Steam\", "Steam" },
            new[] { @"D:\Mods\x\y", "y" },
            new[] { @"D:\bin", "bin" },
            new[] { @"\\nas\games", @"\\nas\games" },
        };
        foreach (var c in names)
        {
            string got = GroupName(c[0]);
            bool ok = got == c[1];
            if (!ok) fail++;
            Console.WriteLine((ok ? "OK   " : "FAIL ") + "name [" + c[0] + "] -> [" + got + "]");
        }
        return fail;
    }
}

class Entry
{
    public string Path, Name, Display, AllUsers, CurrentUser;
    public bool System;
    public List<int> Pids = new List<int>();
    public Awareness Rendering = Awareness.Unknown;
    public bool HasWindow;   // one of its processes shows a window (background helpers can't look blurry)
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
        if (args.Length > 0 && args[0] == "--selftest") return Compat.SelfTest() + Grouping.SelfTest() + Backup.SelfTest();
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
                if (Demo) args = "--demo " + args;
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
