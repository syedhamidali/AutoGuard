using AutoPortal.Guardian.Core.Config;
using AutoPortal.Guardian.Core.Models;
using AutoPortal.Guardian.Core.Storage;

namespace AutoPortal.Guardian.Analysis.Thermal;

/// <summary>
/// Rule-based thermal diagnosis. Limits are configurable per component (never one universal threshold).
/// Every finding carries the recorded evidence behind it.
/// </summary>
public sealed class ThermalAnalyzer
{
    readonly GuardianSettings _s;
    readonly Queue<(DateTime t, double clk)> _cpuClocks = new();

    public ThermalAnalyzer(GuardianSettings settings) => _s = settings;

    public static ThermalLevel Classify(double temp, ThermalLimits l) =>
        temp >= l.Critical ? ThermalLevel.Critical :
        temp >= l.High ? ThermalLevel.High :
        temp >= l.Elevated ? ThermalLevel.Elevated : ThermalLevel.Normal;

    public IReadOnlyList<Finding> Analyze(Snapshot now, IReadOnlyList<Sample> recent)
    {
        var f = new List<Finding>();
        Component(f, ComponentKind.Cpu, "CPU", now.CpuTemp, now.CpuLoad, _s.CpuLimits, recent, s => s.CpuTemp);
        Component(f, ComponentKind.Gpu, "GPU", now.GpuTemp, now.GpuLoad, _s.GpuLimits, recent, s => s.GpuTemp);
        Component(f, ComponentKind.Motherboard, "Motherboard", now.MoboTemp, null, _s.MoboLimits, recent, s => s.MoboTemp);
        if (now.GpuHotspot is double hs && hs - (now.GpuTemp ?? hs) >= 25)
            f.Add(new("GPU_HOTSPOT_DELTA", ComponentKind.Gpu, ThermalLevel.Elevated, "Large GPU hotspot gap",
                "The GPU hotspot is far above the core temperature. This can indicate uneven heatsink contact.",
                new[] { $"Core {now.GpuTemp:0}°C, hotspot {hs:0}°C" }));
        Throttle(f, now);
        return f;
    }

    void Component(List<Finding> f, ComponentKind kind, string label, double? temp, double? load, ThermalLimits lim,
        IReadOnlyList<Sample> recent, Func<Sample, double?> pick)
    {
        if (temp is not double t) return; // sensor not available: say nothing rather than guess
        var level = Classify(t, lim);
        if (level != ThermalLevel.Normal)
            f.Add(new($"{label.ToUpperInvariant()}_TEMP_{level.ToString().ToUpperInvariant()}", kind, level,
                $"{label} temperature is {level.ToString().ToLowerInvariant()}",
                level == ThermalLevel.Critical
                    ? $"{label} is at {t:0}°C, at or above the configured critical limit of {lim.Critical:0}°C. Sustained operation at this level can shorten hardware life, and the hardware may throttle or shut down to protect itself."
                    : $"{label} is at {t:0}°C (configured {level} threshold: {(level == ThermalLevel.High ? lim.High : lim.Elevated):0}°C).",
                new[] { $"Current {label} temperature: {t:0.#}°C", load is double l ? $"{label} load: {l:0}%" : $"{label} load: Not available" }));

        var vals = recent.Select(pick).Where(v => v.HasValue).Select(v => v!.Value).ToList();
        if (vals.Count >= 10)
        {
            var tail = vals.TakeLast(Math.Max(10, vals.Count / 2)).OrderBy(v => v).ToList();
            var median = tail[tail.Count / 2];
            if (median >= lim.High)
                f.Add(new($"{label.ToUpperInvariant()}_SUSTAINED", kind, ThermalLevel.High, $"Sustained high {label} temperature",
                    $"The median {label} temperature over the recent window is {median:0}°C, above the High threshold of {lim.High:0}°C.",
                    new[] { $"Median over last {tail.Count} samples: {median:0.#}°C" }));
            var window = vals.TakeLast(15).ToList();
            if (window.Count >= 5 && window.Max() - window.Min() >= 20 && window.Last() > window.First())
                f.Add(new($"{label.ToUpperInvariant()}_SPIKE", kind, ThermalLevel.Elevated, $"{label} temperature spike",
                    $"{label} rose by {window.Max() - window.Min():0}°C within a short period.",
                    new[] { $"Range in last {window.Count} samples: {window.Min():0}–{window.Max():0}°C" }));
        }
        if (load is double ld && ld < 15 && t >= lim.Elevated)
            f.Add(new($"{label.ToUpperInvariant()}_IDLE_HOT", kind, ThermalLevel.High, $"{label} is hot while mostly idle",
                $"{label} is at {t:0}°C with only {ld:0}% load. High temperature under light load points to a cooling issue rather than heavy work.",
                new[] { $"{label} temperature {t:0}°C", $"{label} load {ld:0}%" }));
    }

    void Throttle(List<Finding> f, Snapshot now)
    {
        if (now.CpuClock is not double clk) return;
        _cpuClocks.Enqueue((now.Timestamp, clk));
        while (_cpuClocks.Count > 0 && now.Timestamp - _cpuClocks.Peek().t > TimeSpan.FromMinutes(10)) _cpuClocks.Dequeue();
        var peak = _cpuClocks.Max(c => c.clk);
        // Indicator only: clock well below its recent peak while hot AND busy.
        if (now.CpuTemp >= _s.CpuLimits.High && now.CpuLoad >= 60 && clk < peak * 0.8)
            f.Add(new("CPU_THROTTLE_INDICATOR", ComponentKind.Cpu, ThermalLevel.High, "Possible CPU thermal throttling",
                "CPU clock speed has dropped well below its recent peak while the CPU is hot and busy. This is an indicator of throttling, not proof; power limits can cause the same pattern.",
                new[] { $"Clock {clk:0} MHz vs recent peak {peak:0} MHz", $"CPU {now.CpuTemp:0}°C at {now.CpuLoad:0}% load" }));
    }
}
