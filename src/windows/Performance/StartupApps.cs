using Microsoft.Win32;

namespace AutoPortal.Guardian.Windows.Performance;

public sealed record StartupApp(string Name, string Command, string Location, bool Enabled);

/// <summary>Lists startup applications (read-only). Respects the Task Manager "StartupApproved" enabled/disabled flag.</summary>
public static class StartupApps
{
    public static IReadOnlyList<StartupApp> List()
    {
        var l = new List<StartupApp>();
        Scan(l, Registry.CurrentUser, "HKCU Run");
        Scan(l, Registry.LocalMachine, "HKLM Run");
        return l;
    }

    static void Scan(List<StartupApp> l, RegistryKey hive, string label)
    {
        try
        {
            using var run = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            using var appr = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run");
            if (run is null) return;
            foreach (var n in run.GetValueNames())
            {
                var flag = appr?.GetValue(n) as byte[];
                bool enabled = flag is null || flag.Length == 0 || (flag[0] & 1) == 0 && flag[0] != 3;
                l.Add(new StartupApp(n, run.GetValue(n)?.ToString() ?? "", label, enabled));
            }
        }
        catch { }
    }
}
