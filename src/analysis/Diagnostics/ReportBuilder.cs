using System.Text;
using AutoPortal.Guardian.Core.Models;
using AutoPortal.Guardian.Core.Storage;

namespace AutoPortal.Guardian.Analysis.Diagnostics;

/// <summary>Builds the exportable diagnostic / freeze-investigation report (Markdown, local only).</summary>
public static class ReportBuilder
{
    public static string Build(DateTime generated, IReadOnlyList<Sample> history, IReadOnlyList<StabilityEvent> events,
        IReadOnlyList<Finding> findings, IReadOnlyList<Recommendation> recs, (DateTime Start, DateTime? LastSample)? lastUncleanSession, string? hardwareSummary = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# AutoPortal System Guardian - Diagnostic Report");
        sb.AppendLine($"Generated: {generated:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("Data stays on this PC. Nothing is uploaded.");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(hardwareSummary)) { sb.AppendLine("## Hardware"); sb.AppendLine(hardwareSummary); sb.AppendLine(); }

        if (lastUncleanSession is { } u)
        {
            sb.AppendLine("## Unexpected end of previous session");
            sb.AppendLine($"Guardian was running from {u.Start:yyyy-MM-dd HH:mm:ss}; the last recorded sample was {(u.LastSample?.ToString("yyyy-MM-dd HH:mm:ss") ?? "none")}.");
            sb.AppendLine("The PC most likely froze, lost power, or was reset at about that time. Data BEFORE that moment was recorded; nothing is known about the moments after the last sample.");
            var window = history.Where(s => u.LastSample.HasValue && s.Time >= u.LastSample.Value.AddMinutes(-10) && s.Time <= u.LastSample.Value).ToList();
            if (window.Count > 0) { sb.AppendLine("### Last recorded minutes before the interruption"); Table(sb, window.TakeLast(20).ToList()); }
            sb.AppendLine();
        }

        sb.AppendLine("## Summary statistics");
        if (history.Count == 0) sb.AppendLine("No samples recorded yet.");
        else
        {
            sb.AppendLine($"{history.Count} samples from {history.First().Time:g} to {history.Last().Time:g}");
            Stat(sb, "CPU temperature (°C)", history.Select(h => h.CpuTemp));
            Stat(sb, "GPU temperature (°C)", history.Select(h => h.GpuTemp));
            Stat(sb, "Motherboard temperature (°C)", history.Select(h => h.MoboTemp));
            Stat(sb, "CPU usage (%)", history.Select(h => h.CpuLoad));
            Stat(sb, "RAM usage (%)", history.Select(h => h.RamLoad));
            Stat(sb, "Disk usage (%)", history.Select(h => h.DiskLoad));
            Stat(sb, "CPU fan (RPM)", history.Select(h => h.CpuFan));
        }
        sb.AppendLine();

        sb.AppendLine("## Findings");
        if (findings.Count == 0) sb.AppendLine("None.");
        foreach (var f in findings) { sb.AppendLine($"- **[{f.Level}] {f.Title}** - {f.Explanation}"); foreach (var e in f.Evidence) sb.AppendLine($"  - Evidence: {e}"); }
        sb.AppendLine();

        sb.AppendLine("## Windows events");
        if (events.Count == 0) sb.AppendLine("None found in the reviewed period.");
        foreach (var e in events.OrderByDescending(e => e.Time).Take(100)) sb.AppendLine($"- {e.Time:yyyy-MM-dd HH:mm:ss} [{e.Category}] {e.Source} #{e.EventId}: {e.Summary}");
        sb.AppendLine();

        sb.AppendLine("## Suspected causes and next steps (possibilities, not confirmed diagnoses)");
        foreach (var r in recs)
        {
            sb.AppendLine($"- **[{r.Priority}] {r.Title}**{(r.IsPossibility ? " (possible)" : "")} - {r.Detail}");
            foreach (var e in r.Evidence) sb.AppendLine($"  - Evidence: {e}");
        }
        return sb.ToString();
    }

    static string N(double? v, string f = "0") => v.HasValue ? v.Value.ToString(f) : "n/a";

    static void Table(StringBuilder sb, List<Sample> rows)
    {
        sb.AppendLine("| Time | CPU °C | GPU °C | CPU % | RAM % | Disk % | CPU fan |");
        sb.AppendLine("|---|--:|--:|--:|--:|--:|--:|");
        foreach (var s in rows) sb.AppendLine($"| {s.Time:HH:mm:ss} | {N(s.CpuTemp)} | {N(s.GpuTemp)} | {N(s.CpuLoad)} | {N(s.RamLoad)} | {N(s.DiskLoad)} | {N(s.CpuFan)} |");
    }

    static void Stat(StringBuilder sb, string label, IEnumerable<double?> values)
    {
        var v = values.Where(x => x.HasValue).Select(x => x!.Value).ToList();
        sb.AppendLine(v.Count == 0 ? $"- {label}: Not available" : $"- {label}: min {v.Min():0.#}, avg {v.Average():0.#}, max {v.Max():0.#}");
    }
}
