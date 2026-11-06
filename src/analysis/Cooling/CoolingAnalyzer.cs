using AutoPortal.Guardian.Core.Config;
using AutoPortal.Guardian.Core.Models;
using AutoPortal.Guardian.Core.Storage;

namespace AutoPortal.Guardian.Analysis.Cooling;

public sealed class CoolingAnalyzer
{
    readonly GuardianSettings _s;
    public CoolingAnalyzer(GuardianSettings s) => _s = s;

    public IReadOnlyList<Finding> Analyze(Snapshot now, IReadOnlyList<Sample> recent)
    {
        var f = new List<Finding>();

        // Zero-RPM fans are only suspicious when the related component is warm (many GPUs legitimately stop fans when cool).
        foreach (var fan in now.Fans)
        {
            if (fan.Value is not double rpm || rpm > 0) continue;
            double? related = fan.Component == ComponentKind.Gpu ? now.GpuTemp : now.CpuTemp;
            var lim = fan.Component == ComponentKind.Gpu ? _s.GpuLimits : _s.CpuLimits;
            if (related >= lim.Elevated - 15)
                f.Add(new("FAN_ZERO_RPM", fan.Component, ThermalLevel.High, $"Fan reports 0 RPM: {fan.Name}",
                    "A fan reports 0 RPM while the related component is warm. It may be stopped, unplugged, or the header may not report speed.",
                    new[] { $"{fan.HardwareName} / {fan.Name}: 0 RPM", $"Related temperature: {related:0}°C" }));
        }

        Fluctuation(f, recent, s => s.CpuFan, "CPU fan");
        Fluctuation(f, recent, s => s.GpuFan, "GPU fan");

        // Poor cooling response: temperature keeps rising while the fan is already near its observed maximum.
        var cpu = recent.Where(s => s.CpuTemp.HasValue && s.CpuFan.HasValue).TakeLast(30).ToList();
        if (cpu.Count >= 20 && cpu.Last().CpuTemp - cpu.First().CpuTemp >= 10 && cpu.Last().CpuLoad < cpu.First().CpuLoad + 10)
            f.Add(new("CPU_POOR_COOLING_RESPONSE", ComponentKind.Cpu, ThermalLevel.High, "CPU temperature rising without a matching load increase",
                "CPU temperature climbed while load stayed flat, which can mean the cooler is not removing heat effectively.",
                new[] { $"CPU temp {cpu.First().CpuTemp:0}→{cpu.Last().CpuTemp:0}°C", $"CPU load {cpu.First().CpuLoad:0}→{cpu.Last().CpuLoad:0}%", $"CPU fan {cpu.Last().CpuFan:0} RPM" }));
        return f;
    }

    static void Fluctuation(List<Finding> f, IReadOnlyList<Sample> recent, Func<Sample, double?> pick, string label)
    {
        var v = recent.Select(pick).Where(x => x.HasValue && x > 0).Select(x => x!.Value).TakeLast(60).ToList();
        if (v.Count < 30) return;
        var mean = v.Average();
        var sd = Math.Sqrt(v.Average(x => (x - mean) * (x - mean)));
        if (mean > 0 && sd / mean > 0.35)
            f.Add(new("FAN_FLUCTUATION", ComponentKind.Fan, ThermalLevel.Elevated, $"{label} speed is fluctuating",
                "Fan speed varies a lot. This can be a normal reaction to a bursty load, a bad curve, or a failing fan.",
                new[] { $"{label} mean {mean:0} RPM, deviation {sd:0} RPM ({sd / mean:P0})" }));
    }
}
