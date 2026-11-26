using AutoPortal.Guardian.Core.Config;
using AutoPortal.Guardian.Windows.Diagnostics;
using AutoPortal.Guardian.Windows.Performance;
using Xunit;

namespace AutoPortal.Guardian.Tests;

public class BlackBoxTests
{
    static string Tmp() => Path.Combine(Path.GetTempPath(), "apbb-" + Guid.NewGuid().ToString("N")[..8], BlackBoxRecorder.FileName);

    static void WaitFor(Func<bool> cond, int ms = 5000) { var end = DateTime.Now.AddMilliseconds(ms); while (!cond() && DateTime.Now < end) Thread.Sleep(25); }

    [Fact]
    public void LineNamesTheBusiestProcessesAndKernelTimeSoADriverSpinCanBeToldFromAnApp()
    {
        var procs = new[]
        {
            new ProcessUsage(1, "quiet", 1, 10, 0, 10, false), new ProcessUsage(2, "spinner", 91.4, 10, 0, 10, false),
            new ProcessUsage(3, "second", 40, 10, 0, 10, false), new ProcessUsage(4, "third", 20, 10, 0, 10, false)
        };
        var line = BlackBoxRecorder.FormatLine(new DateTime(2026, 11, 26, 11, 30, 5), 100, new KernelBreakdown(35.2, 12.1, 97), 70, 123, "0 C: q=12 lat=3200ms", "SM2P41C8=83C(max83)", procs);
        Assert.StartsWith("2026-11-26 11:30:05 cpu=100 dpc=35 int=12 priv=97 ram=70% udp=123", line);
        Assert.Contains("top=[spinner:91% second:40% third:20%]", line);
        Assert.DoesNotContain("quiet", line);            // only the top three
        Assert.Contains("0 C: q=12 lat=3200ms", line);
    }

    [Fact]
    public void MissingValuesAreWrittenAsNotAvailableNeverZero()
    {
        var line = BlackBoxRecorder.FormatLine(DateTime.Now, null, new KernelBreakdown(null, null, null), null, 0, "", "n/a", Array.Empty<ProcessUsage>());
        Assert.Contains("cpu=n/a dpc=n/a int=n/a priv=n/a ram=n/a%", line);
    }

    [Fact]
    public void LineFormatIsTheSameInEveryLanguage()
    {
        var old = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");   // decimal comma
            var line = BlackBoxRecorder.FormatLine(DateTime.Now, 55.6, new KernelBreakdown(1.5, 0.5, 20), 50, 1, "", "", new[] { new ProcessUsage(1, "p", 12.7, 1, 0, 1, false) });
            Assert.DoesNotContain(",", line.Replace("; ", ""));
            Assert.Contains("p:13%", line);
        }
        finally { Thread.CurrentThread.CurrentCulture = old; }
    }

    [Fact]
    public void DriveTemperaturesAreFormattedWithMaximumAndMissingOnesShowNotAvailable()
    {
        Assert.Equal("SSD=47C(max83); HDD=n/a", BlackBoxRecorder.FormatTemps(new[] { new DiskTemp("SSD", 47, 83), new DiskTemp("HDD", null, null) }));
        Assert.Equal("n/a", BlackBoxRecorder.FormatTemps(Array.Empty<DiskTemp>()));
    }

    [Fact]
    public void LinesReachTheFileImmediatelyWhileTheRecorderIsStillRunning()
    {
        var path = Tmp();
        try
        {
            using var r = new BlackBoxRecorder(path);
            r.Write("hello black box");
            WaitFor(() => File.Exists(path) && File.ReadAllText(path).Contains("hello black box"));   // readable now, not only after Dispose
            Assert.Contains("hello black box", File.ReadAllText(path));
        }
        finally { try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch { } }
    }

    [Fact]
    public void LogIsCappedByRotationSoItCannotFillTheDrive()
    {
        var path = Tmp();
        try
        {
            using var r = new BlackBoxRecorder(path, maxBytes: 2000);
            for (int i = 0; i < 100; i++) r.Write(new string('x', 100) + i);
            WaitFor(() => File.Exists(path + ".old"));
            Assert.True(File.Exists(path + ".old"));
            WaitFor(() => new FileInfo(path).Exists);
            Assert.True(new FileInfo(path).Length < 4000);
        }
        finally { try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch { } }
    }

    [Fact]
    public void WritingNeverBlocksEvenIfTheDriveIsUnavailable()
    {
        // an unusable path stands in for a stalled drive: callers must still return instantly and never throw
        using var r = new BlackBoxRecorder(Path.Combine("Z:\\definitely-not-a-drive", "x", BlackBoxRecorder.FileName));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 5000; i++) r.Write("line " + i);   // more than the queue holds: oldest are dropped
        Assert.True(sw.ElapsedMilliseconds < 1000);
    }

    [Fact]
    public void PrefersADifferentDriveThanWindowsWhenOneExists()
    {
        var p = BlackBoxRecorder.PickPath();
        Assert.EndsWith(BlackBoxRecorder.FileName, p);
        var sys = Path.GetPathRoot(Environment.SystemDirectory)!;
        var hasOther = DriveInfo.GetDrives().Any(d => d.DriveType == DriveType.Fixed && d.IsReady && d.AvailableFreeSpace > 1L << 30 && !string.Equals(d.RootDirectory.FullName, sys, StringComparison.OrdinalIgnoreCase));
        if (hasOther) Assert.NotEqual(sys, Path.GetPathRoot(p));
    }

    [Fact]
    public void RecorderProducesRealLinesOnThisMachineAndStopsCleanly()
    {
        var path = Tmp();
        try
        {
            using (var r = new BlackBoxRecorder(path)) { r.Start(2); WaitFor(() => File.Exists(path) && File.ReadAllLines(path).Length >= 1, 15000); }
            var first = File.ReadAllLines(path)[0];
            Assert.Matches(@"^\d{4}-\d\d-\d\d \d\d:\d\d:\d\d cpu=", first);
            Assert.Contains("ram=", first); Assert.Contains("top=[", first);
        }
        finally { try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch { } }
    }

    [Fact]
    public void DriveTemperatureReadNeverThrowsEvenWithoutAdministratorRights() => Assert.NotNull(DiskHealth.ReadTemperatures());

    [Fact]
    public void FlightRecorderIsOnByDefaultAndTheLogStaysLocal()
    {
        var s = new GuardianSettings();
        Assert.True(s.BlackBoxEnabled); Assert.Equal("", s.BlackBoxPath); Assert.False(s.Telemetry);
    }
}
