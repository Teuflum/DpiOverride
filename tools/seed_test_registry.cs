// Test helper for running the UI under Mono on Linux: writes sample overrides
// into Mono's emulated registry. Never run this on Windows.
using Microsoft.Win32;

class Seed
{
    const string Layers = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";

    static void Main()
    {
        var cu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64).CreateSubKey(Layers);
        cu.SetValue(@"C:\Riot Games\League of Legends\LeagueClient.exe", "~ HIGHDPIAWARE");
        cu.Close();
        var lm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).CreateSubKey(Layers);
        lm.SetValue(@"C:\Games\Old.exe", "~ GDIDPISCALING DPIUNAWARE");
        lm.SetValue(@"C:\Program Files\Steam\steam.exe", "~ HIGHDPIAWARE");
        lm.SetValue(@"C:\Riot Games\Riot Client\RiotClientServices.exe", "~ HIGHDPIAWARE RUNASADMIN");
        lm.Close();
    }
}
