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

class Entry
{
    public string Path, Name, Display, AllUsers, CurrentUser;
    public bool System;
    public List<int> Pids = new List<int>();
    public Awareness Rendering = Awareness.Unknown;
    public bool Running { get { return Pids.Count > 0; } }
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
        if (args.Length > 0 && args[0] == "--selftest") return Compat.SelfTest();
        Demo = args.Contains("--demo");
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
                Process.Start(new ProcessStartInfo(Application.ExecutablePath) { Verb = "runas", UseShellExecute = true });
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
