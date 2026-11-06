using System.Management;

namespace AutoPortal.Guardian.Windows.Diagnostics;

public sealed record DiskInfo(string Name, string MediaType, string Health, string Operational, bool? SmartPredictsFailure, double? SizeGb);

/// <summary>Reads drive health from Windows Storage (and SMART failure prediction where the driver exposes it). Read-only.</summary>
public static class DiskHealth
{
    public static IReadOnlyList<DiskInfo> Read()
    {
        var list = new List<DiskInfo>();
        try
        {
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
            using var s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT FriendlyName,MediaType,HealthStatus,OperationalStatus,Size FROM MSFT_PhysicalDisk"));
            foreach (ManagementObject d in s.Get())
            {
                using (d)
                {
                    var op = d["OperationalStatus"] is ushort[] ops && ops.Length > 0 ? Op(ops[0]) : "Unknown";
                    list.Add(new DiskInfo(d["FriendlyName"]?.ToString() ?? "Disk", Media(d["MediaType"]), Health(d["HealthStatus"]), op, null,
                        d["Size"] is ulong sz ? Math.Round(sz / 1073741824.0, 0) : null));
                }
            }
        }
        catch { /* storage WMI unavailable: return what we have, never guess */ }

        bool? predict = null;
        try
        {
            using var s = new ManagementObjectSearcher(@"root\wmi", "SELECT PredictFailure FROM MSStorageDriver_FailurePredictStatus");
            var any = false; var fail = false;
            foreach (ManagementBaseObject o in s.Get()) { any = true; if (o["PredictFailure"] is true) fail = true; }
            if (any) predict = fail;
        }
        catch { /* needs administrator or driver support: leave as not available */ }
        // The WMI class is not keyed to the friendly name, so apply it to all disks only as a system-wide indicator.
        return predict is null ? list : list.Select(d => d with { SmartPredictsFailure = predict }).ToList();
    }

    static int? N(object? v) => v is null ? null : Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture);
    static string Health(object? v) => N(v) switch { 0 => "Healthy", 1 => "Warning", 2 => "Unhealthy", _ => "Not available" };
    static string Media(object? v) => N(v) switch { 3 => "HDD", 4 => "SSD", 5 => "SCM", _ => "Unknown" };
    static string Op(ushort v) => v switch { 2 => "OK", 3 => "Degraded", 5 => "Predictive failure", 6 => "Error", 10 => "Stopped", _ => "Code " + v };
}
