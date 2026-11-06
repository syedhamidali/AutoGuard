using System.Diagnostics;
using System.Security;
using System.Text;

namespace AutoPortal.Guardian.Windows.Startup;

/// <summary>
/// Windows Task Scheduler integration: start at user logon (with delay), run in the interactive session,
/// restart on failure, and run as the normal user by default (no UAC on each start).
/// </summary>
public static class StartupTask
{
    public const string TaskName = "AutoPortal System Guardian";

    public static string BuildXml(string exePath, string userSid, int delaySeconds, bool elevated)
    {
        var delay = $"PT{Math.Max(0, delaySeconds)}S";
        var run = elevated ? "HighestAvailable" : "LeastPrivilege";
        return $"""
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo><Description>Starts AutoPortal System Guardian minimized to the tray at logon.</Description></RegistrationInfo>
  <Triggers>
    <LogonTrigger><Enabled>true</Enabled><UserId>{SecurityElement.Escape(userSid)}</UserId><Delay>{delay}</Delay></LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id="Author"><UserId>{SecurityElement.Escape(userSid)}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>{run}</RunLevel></Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <StartWhenAvailable>true</StartWhenAvailable>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <RestartOnFailure><Interval>PT1M</Interval><Count>5</Count></RestartOnFailure>
    <Priority>7</Priority>
  </Settings>
  <Actions Context="Author"><Exec><Command>{SecurityElement.Escape(exePath)}</Command><Arguments>--tray</Arguments></Exec></Actions>
</Task>
""";
    }

    public static bool IsInstalled() => Run("/Query /TN \"" + TaskName + "\"").code == 0;

    public static (bool ok, string message) Install(string exePath, int delaySeconds, bool elevated = false)
    {
        var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
        var xml = BuildXml(exePath, sid, delaySeconds, elevated);
        var tmp = Path.Combine(Path.GetTempPath(), "apsg-task.xml");
        File.WriteAllText(tmp, xml, Encoding.Unicode);
        try
        {
            var r = Run($"/Create /TN \"{TaskName}\" /XML \"{tmp}\" /F");
            return (r.code == 0, r.output);
        }
        finally { try { File.Delete(tmp); } catch { } }
    }

    /// <summary>Removes only this application's own scheduled task (called when the user turns startup off or uninstalls).</summary>
    public static (bool ok, string message) Uninstall()
    {
        var r = Run($"/Delete /TN \"{TaskName}\" /F");
        return (r.code == 0, r.output);
    }

    static (int code, string output) Run(string args)
    {
        using var p = Process.Start(new ProcessStartInfo("schtasks.exe", args)
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true })!;
        var o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, o.Trim());
    }
}
