using System.Diagnostics;
using System.Net.NetworkInformation;
using Microsoft.Win32;

namespace AutoPortal.Guardian.Windows.Recovery;

/// <summary>
/// Safe recovery actions. Nothing here reboots Windows, disables security features, or acts on hardware.
/// Network adapter resets only happen with explicit approval (the caller passes <c>approved: true</c>).
/// </summary>
public sealed class RecoveryService : IDisposable
{
    public event Action? Resumed;          // after sleep/hibernate
    public event Action? SessionEnding;    // logoff/shutdown: flush and mark a clean end

    public RecoveryService()
    {
        SystemEvents.PowerModeChanged += OnPower;
        SystemEvents.SessionEnding += OnEnding;
    }

    void OnPower(object? s, PowerModeChangedEventArgs e) { if (e.Mode == PowerModes.Resume) Resumed?.Invoke(); }
    void OnEnding(object? s, SessionEndingEventArgs e) => SessionEnding?.Invoke();

    public static IReadOnlyList<(string Name, OperationalStatus Status)> NetworkAdapters() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)
            .Select(n => (n.Name, n.OperationalStatus)).ToList();

    /// <summary>Reports adapters that are present but down. Detection only.</summary>
    public static IReadOnlyList<string> DetectFailedAdapters() =>
        NetworkAdapters().Where(a => a.Status is OperationalStatus.Down or OperationalStatus.Unknown).Select(a => a.Name).ToList();

    public static (bool ok, string message) ResetAdapter(string adapterName, bool approved)
    {
        if (!approved) return (false, "Network adapter reset requires explicit approval.");
        if (adapterName.Any(c => c is '"' or '\'' or ';' or '`' or '$')) return (false, "Invalid adapter name.");
        var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -Command \"Restart-NetAdapter -Name '{adapterName}' -Confirm:$false\"")
        { RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEnd(); p.WaitForExit();
        return (p.ExitCode == 0, p.ExitCode == 0 ? "Adapter restarted." : "Failed (administrator rights are required): " + err.Trim());
    }

    public void Dispose() { SystemEvents.PowerModeChanged -= OnPower; SystemEvents.SessionEnding -= OnEnding; }
}
