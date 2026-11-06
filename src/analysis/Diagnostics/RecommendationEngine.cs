using AutoPortal.Guardian.Core.Config;
using AutoPortal.Guardian.Core.Models;

namespace AutoPortal.Guardian.Analysis.Diagnostics;

/// <summary>
/// Rule-based recommendations. Each is backed by recorded evidence and labeled as a possibility, never a confirmed diagnosis.
/// </summary>
public sealed class RecommendationEngine
{
    readonly GuardianSettings _s;
    public RecommendationEngine(GuardianSettings s) => _s = s;

    public IReadOnlyList<Recommendation> Build(Snapshot now, IReadOnlyList<Finding> findings, IReadOnlyList<StabilityEvent> events, bool uncleanShutdownDetected = false)
    {
        var r = new List<Recommendation>();
        var cpu = now.CpuTemp; var gpu = now.GpuTemp;
        bool cpuHot = cpu >= _s.CpuLimits.Elevated, gpuHot = gpu >= _s.GpuLimits.Elevated;

        // CASE 1: CPU hot while CPU load low
        if (cpuHot && now.CpuLoad < 20)
            r.Add(new(Priority.High, "Inspect CPU cooling",
                "CPU temperature is high while CPU usage is low. Possible causes: loose or misaligned cooler mounting, dried thermal paste, a stopped CPU fan, a failing liquid-cooler pump, or dust buildup.",
                new[] { $"CPU {cpu:0}°C at {now.CpuLoad:0}% load", $"CPU fan: {now.CpuFanRpm?.ToString("0") ?? "Not available"} RPM" }));

        // CASE 2: GPU hot (under load = review workload; idle = cooling suspect)
        if (gpuHot)
            r.Add(new(Priority.High, "Check GPU cooling",
                now.GpuLoad >= 70
                    ? "GPU temperature is high under heavy load. Possible causes: GPU fan not ramping, restricted airflow, dusty heatsink, or a very demanding workload (consider a frame cap)."
                    : "GPU temperature is high without heavy load. Possible causes: stopped GPU fan, dust on the heatsink, or poor case airflow.",
                new[] { $"GPU {gpu:0}°C at {now.GpuLoad?.ToString("0") ?? "n/a"}% load", $"GPU fan: {now.GpuFanRpm?.ToString("0") ?? "Not available"} RPM" }));

        // CASE 3: both hot -> case airflow
        if (cpuHot && gpuHot)
            r.Add(new(Priority.High, "Improve case airflow",
                "Both CPU and GPU are warm, which points to the case or room rather than a single cooler. Possible causes: wrong fan orientation, blocked or dusty filters, insufficient exhaust, high room temperature.",
                new[] { $"CPU {cpu:0}°C", $"GPU {gpu:0}°C", now.MoboTemp is double m ? $"Motherboard {m:0}°C" : "Motherboard temperature: Not available" }));
        else if (cpuHot && gpu is not null && gpu < _s.GpuLimits.Elevated - 10)
            r.Add(new(Priority.Recommended, "CPU is hot but GPU is normal",
                "CPU temperature is unusually high while the GPU remains normal. Possible causes include CPU cooler mounting, thermal paste degradation, or insufficient CPU cooling rather than case airflow.",
                new[] { $"CPU {cpu:0}°C", $"GPU {gpu:0}°C" }));

        foreach (var f in findings.Where(x => x.Level == ThermalLevel.Critical))
            r.Add(new(Priority.Critical, $"Critical: {f.Title}",
                f.Explanation + " Close demanding programs, ensure fans are running and vents are clear. If it persists, shut down and inspect cooling. Your hardware's own thermal protection remains active.",
                f.Evidence, IsPossibility: false));

        foreach (var f in findings.Where(x => x.Code is "FAN_ZERO_RPM"))
            r.Add(new(Priority.High, f.Title, f.Explanation + " Check the fan connector and that it spins freely.", f.Evidence));

        // CASE 4: freezes / unexpected shutdowns while temperatures normal
        var shutdowns = events.Where(e => e.Category is "UnexpectedShutdown" or "PowerLoss").ToList();
        bool temperaturesNormal = !cpuHot && !gpuHot && !findings.Any(x => x.Level >= ThermalLevel.High);
        if ((shutdowns.Count > 0 || uncleanShutdownDetected) && temperaturesNormal)
        {
            var ev = new List<string> { $"{shutdowns.Count} unexpected shutdown event(s) in the reviewed period", "Recorded temperatures were not high" };
            r.Add(new(Priority.High, "Freezes with normal temperatures: investigate non-thermal causes",
                "Heat does not appear to explain the freezes. Possible causes: faulty RAM (run Windows Memory Diagnostic or MemTest86), failing storage, power supply or power-delivery issues, graphics/chipset driver problems, or a security-software/driver conflict.",
                ev));
        }
        var whea = events.Count(e => e.Category == "WHEA");
        if (whea > 0)
            r.Add(new(Priority.High, "Hardware errors reported by Windows (WHEA)", "WHEA events mean the CPU, memory or PCIe bus reported a hardware error. Possible causes include unstable overclock/XMP, faulty RAM or a failing component.",
                new[] { $"{whea} WHEA event(s)" }));
        var disk = events.Count(e => e.Category == "Disk");
        if (disk > 0)
            r.Add(new(Priority.High, "Storage errors detected", "Windows logged disk errors. Back up important data and check drive health (SMART) and cables.", new[] { $"{disk} disk event(s)" }));
        var gpuReset = events.Count(e => e.Category == "DisplayDriver");
        if (gpuReset > 0)
            r.Add(new(Priority.Recommended, "Display driver resets", "The display driver stopped responding and recovered. Possible causes: driver bugs, unstable GPU clocks, overheating, or insufficient power.", new[] { $"{gpuReset} display driver event(s)" }));
        var hangs = events.Count(e => e.Category is "AppHang" or "AppCrash");
        if (hangs > 0)
            r.Add(new(Priority.Recommended, "Application crashes or hangs", "Specific programs have crashed or stopped responding; check the System Health page to see which ones.", new[] { $"{hangs} application event(s)" }));

        if (!r.Any())
            r.Add(new(Priority.Informational, "No problems detected", "No concerning readings or events were found in the data collected so far. Leave Guardian running to capture an issue when it happens.", Array.Empty<string>(), false));
        return r.OrderBy(x => x.Priority).ToList();
    }
}
