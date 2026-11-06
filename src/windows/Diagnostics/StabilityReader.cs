using System.Diagnostics.Eventing.Reader;
using AutoPortal.Guardian.Core.Models;

namespace AutoPortal.Guardian.Windows.Diagnostics;

/// <summary>Reads supported Windows diagnostic sources (Event Log). Read-only.</summary>
public static class StabilityReader
{
    // (log, provider, id, category)
    static readonly (string Log, string Provider, int Id, string Category)[] Map =
    {
        ("System", "Microsoft-Windows-Kernel-Power", 41, "UnexpectedShutdown"),
        ("System", "EventLog", 6008, "UnexpectedShutdown"),
        ("System", "Microsoft-Windows-WHEA-Logger", -1, "WHEA"),
        ("System", "BugCheck", 1001, "BugCheck"),
        ("System", "Display", 4101, "DisplayDriver"),
        ("System", "disk", 7, "Disk"), ("System", "disk", 11, "Disk"), ("System", "disk", 51, "Disk"), ("System", "disk", 153, "Disk"),
        ("System", "Ntfs", 55, "Disk"), ("System", "Ntfs", 98, "Disk"),
        ("System", "Tcpip", 4266, "ResourceExhaustion"),
        ("System", "Service Control Manager", 7009, "ServiceTimeout"),
        ("Application", "Application Error", 1000, "AppCrash"),
        ("Application", "Application Hang", 1002, "AppHang"),
    };

    public static IReadOnlyList<StabilityEvent> Read(TimeSpan lookback, int maxPerSource = 100)
    {
        var result = new List<StabilityEvent>();
        var since = DateTime.UtcNow - lookback;
        foreach (var g in Map.GroupBy(m => (m.Log, m.Provider)))
        {
            var ids = g.Select(x => x.Id).Where(i => i > 0).ToList();
            var idFilter = ids.Count == 0 ? "" : " and (" + string.Join(" or ", ids.Select(i => $"EventID={i}")) + ")";
            var q = $"*[System[Provider[@Name='{g.Key.Provider}']{idFilter} and TimeCreated[@SystemTime>='{since:yyyy-MM-ddTHH:mm:ss.fffZ}']]]";
            try
            {
                using var reader = new EventLogReader(new EventLogQuery(g.Key.Log, PathType.LogName, q) { ReverseDirection = true });
                int n = 0;
                for (var e = reader.ReadEvent(); e != null && n < maxPerSource; e = reader.ReadEvent(), n++)
                {
                    using (e)
                    {
                        var cat = g.FirstOrDefault(x => x.Id == e.Id).Category ?? g.First().Category;
                        string msg;
                        try { msg = (e.FormatDescription() ?? "").Split('\n')[0].Trim(); } catch { msg = "(description unavailable)"; }
                        // Kernel-Power 41 with bugcheck code 0 = power loss / hard reset / hang, not a blue screen.
                        if (e.Id == 41 && e.ProviderName == "Microsoft-Windows-Kernel-Power") cat = "UnexpectedShutdown";
                        result.Add(new StabilityEvent(e.TimeCreated ?? DateTime.MinValue, e.ProviderName ?? g.Key.Provider, e.Id, cat, msg));
                    }
                }
            }
            catch (EventLogException) { /* log not accessible (needs admin) or provider absent: skip, never invent */ }
            catch (UnauthorizedAccessException) { }
        }
        return result.OrderByDescending(r => r.Time).ToList();
    }
}
