using System.Xml.Linq;
using AutoPortal.Guardian.Analysis.Cooling;
using AutoPortal.Guardian.Analysis.Diagnostics;
using AutoPortal.Guardian.Analysis.Thermal;
using AutoPortal.Guardian.Core.Config;
using AutoPortal.Guardian.Core.Models;
using AutoPortal.Guardian.Core.Storage;
using AutoPortal.Guardian.Hardware;
using AutoPortal.Guardian.Windows.Performance;
using AutoPortal.Guardian.Windows.Recovery;
using AutoPortal.Guardian.Windows.Startup;
using Xunit;

namespace AutoPortal.Guardian.Tests;

static class T
{
    public static SensorReading R(ComponentKind c, string n, SensorKind k, double? v) => new("t/" + n, "test", c, n, k, v);
    public static Snapshot Snap(params SensorReading[] r) => new() { Readings = r };
}

public class SnapshotTests
{
    [Fact]
    public void MissingSensorsAreNullNotInvented()
    {
        var s = T.Snap();
        Assert.Null(s.CpuTemp); Assert.Null(s.GpuTemp); Assert.Null(s.MoboTemp); Assert.Null(s.CpuFanRpm); Assert.Empty(s.Fans);
    }

    [Fact]
    public void ReadingWithNullValueShowsNotAvailable() =>
        Assert.Equal("Not available", T.R(ComponentKind.Cpu, "CPU Package", SensorKind.Temperature, null).Display("°C"));

    [Fact]
    public void SensorWithoutValueIsIgnoredByAccessors()
    {
        var s = T.Snap(T.R(ComponentKind.Cpu, "CPU Package", SensorKind.Temperature, null));
        Assert.Null(s.CpuTemp);
    }

    [Fact]
    public void PicksPackageTemperature()
    {
        var s = T.Snap(T.R(ComponentKind.Cpu, "Core #1", SensorKind.Temperature, 50), T.R(ComponentKind.Cpu, "CPU Package", SensorKind.Temperature, 61));
        Assert.Equal(61, s.CpuTemp);
    }

    [Fact]
    public void DemoDataIsFlagged() => Assert.True(new DemoSensorProvider().Read().IsDemo);
}

public class ThermalTests
{
    static readonly GuardianSettings S = new();

    [Theory]
    [InlineData(50, ThermalLevel.Normal)]
    [InlineData(72, ThermalLevel.Elevated)]
    [InlineData(82, ThermalLevel.High)]
    [InlineData(95, ThermalLevel.Critical)]
    public void ClassifiesWithConfiguredLimits(double t, ThermalLevel expected) =>
        Assert.Equal(expected, ThermalAnalyzer.Classify(t, S.CpuLimits));

    [Fact]
    public void LimitsArePerComponentNotUniversal()
    {
        Assert.Equal(ThermalLevel.High, ThermalAnalyzer.Classify(85, S.CpuLimits));
        Assert.Equal(ThermalLevel.Critical, ThermalAnalyzer.Classify(85, S.MoboLimits));
    }

    [Fact]
    public void HighCpuUsageAloneIsNotOverheating()
    {
        var snap = T.Snap(T.R(ComponentKind.Cpu, "CPU Package", SensorKind.Temperature, 55), T.R(ComponentKind.Cpu, "CPU Total", SensorKind.Load, 100));
        Assert.Empty(new ThermalAnalyzer(S).Analyze(snap, Array.Empty<Sample>()));
    }

    [Fact]
    public void NoSensorMeansNoFinding() => Assert.Empty(new ThermalAnalyzer(S).Analyze(T.Snap(), Array.Empty<Sample>()));

    [Fact]
    public void CriticalReadingProducesCriticalFindingWithEvidence()
    {
        var f = new ThermalAnalyzer(S).Analyze(T.Snap(T.R(ComponentKind.Cpu, "CPU Package", SensorKind.Temperature, 93)), Array.Empty<Sample>());
        var crit = Assert.Single(f, x => x.Level == ThermalLevel.Critical);
        Assert.Equal(ComponentKind.Cpu, crit.Component); Assert.NotEmpty(crit.Evidence);
    }

    [Fact]
    public void HotWhileIdleIsFlagged()
    {
        var snap = T.Snap(T.R(ComponentKind.Cpu, "CPU Package", SensorKind.Temperature, 74), T.R(ComponentKind.Cpu, "CPU Total", SensorKind.Load, 3));
        Assert.Contains(new ThermalAnalyzer(S).Analyze(snap, Array.Empty<Sample>()), x => x.Code == "CPU_IDLE_HOT");
    }

    [Fact]
    public void DetectsSuddenSpike()
    {
        var hist = Enumerable.Range(0, 15).Select(i => new Sample(DateTime.Now.AddSeconds(i), 40 + i * 2.5, null, null, null, 30, null, null, null, null, null)).ToList();
        var snap = T.Snap(T.R(ComponentKind.Cpu, "CPU Package", SensorKind.Temperature, 75));
        Assert.Contains(new ThermalAnalyzer(S).Analyze(snap, hist), x => x.Code == "CPU_SPIKE");
    }
}

public class FanCurveTests
{
    [Theory]
    [InlineData(20, 30)] [InlineData(40, 30)] [InlineData(50, 45)] [InlineData(55, 52.5)] [InlineData(85, 100)] [InlineData(120, 100)]
    public void ExampleCurveInterpolates(double temp, double pct) => Assert.Equal(pct, FanCurve.Example.Evaluate(temp), 3);

    [Fact] public void RejectsFanOff() => Assert.Throws<ArgumentException>(() => new FanCurve(new[] { new FanPoint(40, 0), new FanPoint(80, 100) }));
    [Fact] public void RejectsCurveThatNeverReaches100() => Assert.Throws<ArgumentException>(() => new FanCurve(new[] { new FanPoint(40, 30), new FanPoint(80, 80) }));
    [Fact] public void RejectsDecreasingCurve() => Assert.Throws<ArgumentException>(() => new FanCurve(new[] { new FanPoint(40, 50), new FanPoint(60, 30), new FanPoint(80, 100) }));
}

public class CoolingTests
{
    [Fact]
    public void ZeroRpmGpuFanWhenCoolIsNotAFailure()
    {
        var snap = T.Snap(T.R(ComponentKind.Gpu, "GPU Fan", SensorKind.FanRpm, 0), T.R(ComponentKind.Gpu, "GPU Core", SensorKind.Temperature, 35));
        Assert.Empty(new CoolingAnalyzer(new()).Analyze(snap, Array.Empty<Sample>()));
    }

    [Fact]
    public void ZeroRpmCpuFanWhenWarmIsFlagged()
    {
        var snap = T.Snap(T.R(ComponentKind.Fan, "CPU Fan", SensorKind.FanRpm, 0), T.R(ComponentKind.Cpu, "CPU Package", SensorKind.Temperature, 68));
        Assert.Contains(new CoolingAnalyzer(new()).Analyze(snap, Array.Empty<Sample>()), x => x.Code == "FAN_ZERO_RPM");
    }
}

public class RecommendationTests
{
    static readonly GuardianSettings S = new();

    [Fact]
    public void Case1_CpuHotWhileIdleSuggestsCoolerInspectionAsPossibility()
    {
        var snap = T.Snap(T.R(ComponentKind.Cpu, "CPU Package", SensorKind.Temperature, 78), T.R(ComponentKind.Cpu, "CPU Total", SensorKind.Load, 5));
        var r = new RecommendationEngine(S).Build(snap, Array.Empty<Finding>(), Array.Empty<StabilityEvent>());
        var rec = Assert.Single(r, x => x.Title.Contains("CPU cooling"));
        Assert.True(rec.IsPossibility); Assert.NotEmpty(rec.Evidence);
    }

    [Fact]
    public void Case3_BothHotSuggestsCaseAirflow()
    {
        var snap = T.Snap(T.R(ComponentKind.Cpu, "CPU Package", SensorKind.Temperature, 78), T.R(ComponentKind.Gpu, "GPU Core", SensorKind.Temperature, 80));
        Assert.Contains(new RecommendationEngine(S).Build(snap, Array.Empty<Finding>(), Array.Empty<StabilityEvent>()), x => x.Title.Contains("airflow"));
    }

    [Fact]
    public void Case4_FreezesWithNormalTempsPointAwayFromHeat()
    {
        var snap = T.Snap(T.R(ComponentKind.Cpu, "CPU Package", SensorKind.Temperature, 45));
        var ev = new[] { new StabilityEvent(DateTime.Now, "Kernel-Power", 41, "UnexpectedShutdown", "x") };
        var r = new RecommendationEngine(S).Build(snap, Array.Empty<Finding>(), ev);
        Assert.Contains(r, x => x.Title.Contains("non-thermal") && x.Detail.Contains("RAM"));
    }

    [Fact]
    public void NothingWrongSaysSoWithoutInventingProblems()
    {
        var r = new RecommendationEngine(S).Build(T.Snap(), Array.Empty<Finding>(), Array.Empty<StabilityEvent>());
        Assert.Equal(Priority.Informational, Assert.Single(r).Priority);
    }

    [Fact]
    public void CriticalComesFirst()
    {
        var f = new[] { new Finding("X", ComponentKind.Cpu, ThermalLevel.Critical, "Hot", "e", new[] { "ev" }) };
        var r = new RecommendationEngine(S).Build(T.Snap(), f, Array.Empty<StabilityEvent>());
        Assert.Equal(Priority.Critical, r[0].Priority);
    }
}

public class StoreTests
{
    [Fact]
    public void RoundTripsSamplesIncludingNulls()
    {
        using var db = new HistoryStore(":memory:");
        db.Add(new Sample(DateTime.Now, 55.5, null, null, null, 10, null, 40, null, null, null));
        var s = Assert.Single(db.Range(DateTime.Now.AddMinutes(-1), DateTime.Now.AddMinutes(1)));
        Assert.Equal(55.5, s.CpuTemp); Assert.Null(s.GpuTemp);
    }

    [Fact]
    public void DetectsUncleanPreviousSession()
    {
        var path = Path.Combine(Path.GetTempPath(), "apsg-test-" + Guid.NewGuid() + ".db");
        try
        {
            using (var a = new HistoryStore(path)) { Assert.Null(a.BeginSession(DateTime.Now.AddMinutes(-10))); a.Add(new Sample(DateTime.Now.AddMinutes(-5), 50, null, null, null, 1, null, 1, 1, null, null)); /* power cut: no EndSession */ }
            using var b = new HistoryStore(path);
            var u = b.BeginSession(DateTime.Now);
            Assert.NotNull(u); Assert.NotNull(u!.Value.LastSample);
        }
        finally { foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + "*")) try { File.Delete(f); } catch { } }
    }

    [Fact]
    public void CleanSessionIsNotReportedAsCrash()
    {
        using var db = new HistoryStore(":memory:");
        var start = DateTime.Now.AddMinutes(-5); db.BeginSession(start); db.EndSession(start, DateTime.Now);
        Assert.Null(db.BeginSession(DateTime.Now));
    }

    [Fact]
    public void UserCanDeleteAllHistory()
    {
        using var db = new HistoryStore(":memory:");
        db.Add(new Sample(DateTime.Now, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1)); db.LogEvent("k", "m");
        db.DeleteAll();
        Assert.Empty(db.Range(DateTime.Now.AddDays(-1), DateTime.Now.AddDays(1))); Assert.Empty(db.Events());
    }

    [Fact]
    public void ReportDoesNotClaimDataFromAfterTheFreeze()
    {
        var rep = ReportBuilder.Build(DateTime.Now, Array.Empty<Sample>(), Array.Empty<StabilityEvent>(), Array.Empty<Finding>(), Array.Empty<Recommendation>(), (DateTime.Now.AddHours(-1), DateTime.Now.AddMinutes(-30)));
        Assert.Contains("nothing is known about the moments after the last sample", rep);
    }
}

public class SettingsTests
{
    [Fact]
    public void DefaultsRespectPrivacy()
    {
        var s = new GuardianSettings();
        Assert.False(s.Telemetry); Assert.False(s.AllowNetworkAdapterReset); Assert.False(s.StartWithWindows);
    }

    [Fact]
    public void RoundTripsAndSurvivesCorruption()
    {
        var p = Path.Combine(Path.GetTempPath(), "apsg-set-" + Guid.NewGuid() + ".json");
        try
        {
            new GuardianSettings { IntervalSeconds = 7 }.Save(p);
            Assert.Equal(7, GuardianSettings.Load(p).IntervalSeconds);
            File.WriteAllText(p, "{ not json");
            Assert.Equal(2, GuardianSettings.Load(p).IntervalSeconds);
        }
        finally { File.Delete(p); }
    }
}

public class WindowsTests
{
    [Fact]
    public void StartupTaskXmlIsValidAndSafe()
    {
        var xml = StartupTask.BuildXml(@"C:\Program Files\Guardian\g.exe", "S-1-5-21-1", 45, elevated: false);
        var doc = XDocument.Parse(xml);
        Assert.Contains("PT45S", xml); Assert.Contains("LeastPrivilege", xml); Assert.Contains("RestartOnFailure", xml);
        Assert.Contains("InteractiveToken", xml); Assert.Contains("--tray", xml);
        Assert.NotNull(doc.Root);
    }

    [Fact]
    public void ElevatedOptionIsOptIn() => Assert.Contains("HighestAvailable", StartupTask.BuildXml("a.exe", "S-1", 0, elevated: true));

    [Fact]
    public void CriticalWindowsProcessesAreProtected()
    {
        foreach (var n in new[] { "lsass", "csrss", "winlogon", "System", "svchost", "explorer" }) Assert.Contains(n, PerformanceMonitor.Protected);
    }

    [Fact]
    public void AdapterResetRequiresApproval()
    {
        var (ok, msg) = RecoveryService.ResetAdapter("Ethernet", approved: false);
        Assert.False(ok); Assert.Contains("approval", msg);
    }

    [Fact]
    public void AdapterResetRejectsInjection() => Assert.False(RecoveryService.ResetAdapter("Eth'; calc; '", approved: true).ok);

    [Fact]
    public void SystemUsageReadsOnThisMachine()
    {
        using var p = new PerformanceMonitor();
        var u = p.ReadSystem();
        Assert.NotNull(u.RamPercent); Assert.InRange(u.RamPercent!.Value, 1, 100);
    }
}
