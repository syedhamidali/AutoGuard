using System.Diagnostics;
using System.Security;
using System.Text;

namespace AutoPortal.Connectivity.Core;

/// <summary>
/// Task Scheduler "start at logon" entry, created from an XML definition for the CURRENT user.
/// (The command-line form `schtasks /SC ONLOGON` needs administrator rights and fails with "Access is denied"
/// for a normal user, so it must not be used.)
/// </summary>
public static class StartupTask
{
    public static string BuildXml(string exePath, string userSid, int delaySeconds)
    {
        var sid = SecurityElement.Escape(userSid);
        return $"""
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo><Description>Starts AutoPortal Connectivity Manager minimized to the tray at logon.</Description></RegistrationInfo>
  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{sid}</UserId><Delay>PT{Math.Max(0, delaySeconds)}S</Delay></LogonTrigger></Triggers>
  <Principals><Principal id="Author"><UserId>{sid}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <StartWhenAvailable>true</StartWhenAvailable>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <RestartOnFailure><Interval>PT1M</Interval><Count>5</Count></RestartOnFailure>
  </Settings>
  <Actions Context="Author"><Exec><Command>{SecurityElement.Escape(exePath)}</Command><Arguments>--tray</Arguments></Exec></Actions>
</Task>
""";
    }

    public static bool IsInstalled(string taskName) => Run($"/Query /TN \"{taskName}\"").code == 0;

    public static (bool ok, string message) Install(string taskName, string exePath, int delaySeconds = 30)
    {
        var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
        var tmp = Path.Combine(Path.GetTempPath(), "apc-task-" + Guid.NewGuid().ToString("N") + ".xml");
        File.WriteAllText(tmp, BuildXml(exePath, sid, delaySeconds), Encoding.Unicode);
        try { var r = Run($"/Create /TN \"{taskName}\" /XML \"{tmp}\" /F"); return (r.code == 0, r.output); }
        finally { try { File.Delete(tmp); } catch { } }
    }

    /// <summary>Removes only this application's own task (when the user unticks "Start with Windows").</summary>
    public static (bool ok, string message) Uninstall(string taskName)
    {
        if (!IsInstalled(taskName)) return (true, "Not installed.");
        var r = Run($"/Delete /TN \"{taskName}\" /F"); return (r.code == 0, r.output);
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
