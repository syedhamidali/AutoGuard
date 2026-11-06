using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AutoPortal.Guardian.Analysis.Cooling;
using AutoPortal.Guardian.App.Services;
using AutoPortal.Guardian.Core.Config;
using AutoPortal.Guardian.Core.Models;
using AutoPortal.Guardian.Hardware;
using AutoPortal.Guardian.Windows.Performance;
using AutoPortal.Guardian.Windows.Startup;
using Microsoft.Win32;

namespace AutoPortal.Guardian.App;

public partial class MainWindow : Window
{
    readonly GuardianEngine _engine;
    readonly GuardianSettings _s;
    readonly ISensorProvider _sensors;
    readonly Dictionary<string, FrameworkElement> _pages = new();
    string _current = "Dashboard";
    EngineState? _state;

    public MainWindow(GuardianEngine engine, GuardianSettings settings, ISensorProvider sensors)
    {
        InitializeComponent();
        _engine = engine; _s = settings; _sensors = sensors;
        _pages["Dashboard"] = PageDashboard; _pages["Thermal Monitor"] = PageThermal; _pages["Cooling"] = PageCooling;
        _pages["Performance"] = PagePerf; _pages["System Health"] = PageHealth; _pages["Recommendations"] = PageRecs; _pages["Settings"] = PageSettings;
        foreach (var name in _pages.Keys)
        {
            var b = new Button { Content = name, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 2), Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
            var n = name; b.Click += (_, _) => Show(n);
            Nav.Children.Add(b);
        }
        SensorMode.Text = sensors.HasFullAccess ? $"Sensors: {sensors.Name}\nFull sensor access." : $"Sensors: {sensors.Name}\nLimited access. Run as administrator to read CPU/motherboard sensors and fans.";
        if (sensors.GetType() == typeof(DemoSensorProvider)) ShowBanner("DEMO DATA: these readings are simulated for UI development and are not from your hardware.");
        else if (!sensors.HasFullAccess) ShowBanner("Running without administrator rights: many temperature and fan sensors will show \"Not available\". Use Settings → Restart as administrator for full access.");
        CurveGrid.ItemsSource = FanCurve.Example.Points.Select(p => new { Temperature_C = p.TempC, Fan_Percent = p.Percent }).ToList();
        LoadSettings();
        engine.Updated += st => Dispatcher.BeginInvoke(() => { _state = st; if (IsVisible && WindowState != WindowState.Minimized) Refresh(st); });
        Show(_current);
    }

    void ShowBanner(string t) { Banner.Visibility = Visibility.Visible; BannerText.Text = t; }

    new void Show(string page)
    {
        _current = page;
        foreach (var (k, v) in _pages) v.Visibility = k == page ? Visibility.Visible : Visibility.Collapsed;
        foreach (Button b in Nav.Children) b.Foreground = (string)b.Content == page ? (Brush)FindResource("Accent") : (Brush)FindResource("Text");
        if (_state != null) Refresh(_state);
        if (page == "Performance") GridStartup.ItemsSource = StartupApps.List().Select(a => new { a.Name, a.Enabled, a.Location, a.Command }).ToList();
    }

    protected override void OnStateChanged(EventArgs e) { base.OnStateChanged(e); if (WindowState == WindowState.Minimized) Hide(); else if (_state != null) Refresh(_state); }
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e) { e.Cancel = true; Hide(); } // closing hides to tray; use tray menu Exit

    static string T(double? v, string unit) => v is null ? "Not available" : $"{v:0.#}{unit}";
    static IReadOnlyList<double?> S(IEnumerable<AutoPortal.Guardian.Core.Storage.Sample> h, Func<AutoPortal.Guardian.Core.Storage.Sample, double?> f) => h.Select(f).ToList();

    void Refresh(EngineState st)
    {
        var s = st.Snapshot; var u = st.Usage; var h = st.History;
        switch (_current)
        {
            case "Dashboard":
                HealthText.Text = st.Health;
                HealthText.Foreground = st.Health switch { "Good" => Brushes.LightGreen, "Watch" => Brushes.Khaki, "Needs attention" => Brushes.Orange, _ => Brushes.OrangeRed };
                HealthDetail.Text = st.Findings.Count == 0 ? "No concerning readings right now." : string.Join("\n", st.Findings.Take(3).Select(f => "• " + f.Title));
                TCpuT.Text = T(s.CpuTemp, " °C"); TGpuT.Text = T(s.GpuTemp, " °C"); TMoboT.Text = T(s.MoboTemp, " °C");
                TCpuL.Text = T(s.CpuLoad ?? u.CpuPercent, " %"); TRam.Text = T(u.RamPercent, " %"); TGpuL.Text = T(s.GpuLoad, " %");
                TFans.Text = s.Fans.Count == 0 ? "Not available" : string.Join("\n", s.Fans.Take(4).Select(f => $"{f.Name}: {f.Value:0} RPM"));
                var unexpected = st.Events.Count(e => e.Category == "UnexpectedShutdown");
                TStab.Text = $"{unexpected} unexpected shutdown(s)\n{st.Events.Count(e => e.Category is "AppCrash" or "AppHang")} app crash/hang(s)";
                DashTemps.Series = S(h, x => x.CpuTemp ?? x.GpuTemp); DashLoad.Series = S(h, x => x.CpuLoad);
                break;
            case "Thermal Monitor":
                ChCpuT.Series = S(h, x => x.CpuTemp); ChGpuT.Series = S(h, x => x.GpuTemp); ChGpuH.Series = S(h, x => x.GpuHotspot); ChFan.Series = S(h, x => x.CpuFan);
                ThermalFindings.ItemsSource = st.Findings.Where(f => f.Component != ComponentKind.Fan).Select(f => $"[{f.Level}] {f.Title}: {f.Explanation}").DefaultIfEmpty("No temperature warnings.").ToList();
                break;
            case "Cooling":
                FanGrid.ItemsSource = s.Fans.Count == 0 ? new[] { new { Fan = "No fans reported by this hardware", RPM = "Not available" } }.Cast<object>().ToList()
                    : s.Fans.Select(f => (object)new { Fan = f.Name, Device = f.HardwareName, RPM = f.Value is double r ? r.ToString("0") : "Not available" }).ToList();
                FanControlInfo.Text = _sensors is LhmSensorProvider lp
                    ? (lp.FanChannels().Count == 0 ? "No controllable fan channels were found. Many motherboards don't expose fan control to software; monitoring still works. Consider setting a fan curve in your BIOS/UEFI (Q-Fan, Smart Fan or similar)." : $"{lp.FanChannels().Count} controllable fan channel(s) found. Control is never changed without your confirmation.")
                    : "Fan control is unavailable in this mode.";
                CoolingList.ItemsSource = st.Findings.Where(f => f.Component is ComponentKind.Fan || f.Code.Contains("FAN") || f.Code.Contains("COOLING")).Select(f => $"[{f.Level}] {f.Title}: {f.Explanation}").DefaultIfEmpty("No cooling issues observed.").ToList();
                break;
            case "Performance":
                PerfSummary.Text = $"Uptime {u.Uptime.Days}d {u.Uptime.Hours}h {u.Uptime.Minutes}m   |   Available RAM {u.RamAvailableMb:N0} MB   |   Disk response {T(u.DiskResponseMs, " ms")}   |   UDP endpoints {st.UdpEndpoints:N0}   |   TCP connections {st.TcpConnections:N0}";
                ChCpuL.Series = S(h, x => x.CpuLoad); ChRam.Series = S(h, x => x.RamLoad); ChDisk.Series = S(h, x => x.DiskLoad); ChGpuL.Series = S(h, x => x.GpuLoad);
                GridCpu.ItemsSource = st.TopCpu.Select(Row).ToList(); GridRam.ItemsSource = st.TopRam.Select(Row).ToList(); GridDisk.ItemsSource = st.TopDisk.Select(Row).ToList();
                break;
            case "System Health":
                HealthUnclean.Text = _engine.UncleanPreviousSession is { } un
                    ? $"The previous Guardian session (started {un.Start:g}) did not end cleanly. The last recorded sample was {(un.LastSample?.ToString("g") ?? "none")}. The PC most likely froze or lost power then. Data before that point is in the report."
                    : "The previous session ended normally.";
                EventGrid.ItemsSource = st.Events.Select(e => new { Time = e.Time.ToString("g"), e.Category, e.Source, e.EventId, e.Summary }).ToList();
                OwnLog.ItemsSource = _engine.Store.Events(100).Select(e => new { Time = e.Time.ToString("g"), e.Kind, e.Message }).ToList();
                break;
            case "Recommendations":
                RecList.ItemsSource = st.Recommendations.Select(r =>
                {
                    var sp = new StackPanel { Margin = new Thickness(0, 0, 10, 8) };
                    var b = new Border { Style = (Style)FindResource("Card"), Child = sp, BorderBrush = r.Priority switch { Priority.Critical => Brushes.OrangeRed, Priority.High => Brushes.Orange, Priority.Recommended => Brushes.Khaki, _ => Brushes.SteelBlue } };
                    sp.Children.Add(new TextBlock { Text = $"{r.Priority}: {r.Title}{(r.IsPossibility ? "  (possible cause)" : "")}", FontWeight = FontWeights.SemiBold });
                    sp.Children.Add(new TextBlock { Text = r.Detail, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) });
                    foreach (var ev in r.Evidence) sp.Children.Add(new TextBlock { Text = "Evidence: " + ev, Style = (Style)FindResource("Dim"), TextWrapping = TextWrapping.Wrap });
                    return b;
                }).ToList();
                break;
        }
    }

    static object Row(ProcessUsage p) => new { p.Name, p.Pid, CPU_pct = Math.Round(p.CpuPercent, 1), RAM_MB = Math.Round(p.RamMb), Disk_MBps = Math.Round(p.DiskMbPerSec, 2), p.Handles, Critical_Windows = p.IsProtected ? "Yes - do not end" : "" };

    void LoadSettings()
    {
        SetStartup.IsChecked = StartupTask.IsInstalled(); SetDelay.Text = _s.StartupDelaySeconds.ToString(); SetInterval.Text = _s.IntervalSeconds.ToString();
        SetNotify.IsChecked = _s.NotificationsEnabled; SetRecovery.IsChecked = _s.AutoRecovery; SetRetention.Text = _s.RetentionDays.ToString();
        (CpuE.Text, CpuH.Text, CpuC.Text) = (_s.CpuLimits.Elevated.ToString(), _s.CpuLimits.High.ToString(), _s.CpuLimits.Critical.ToString());
        (GpuE.Text, GpuH.Text, GpuC.Text) = (_s.GpuLimits.Elevated.ToString(), _s.GpuLimits.High.ToString(), _s.GpuLimits.Critical.ToString());
        (MbE.Text, MbH.Text, MbC.Text) = (_s.MoboLimits.Elevated.ToString(), _s.MoboLimits.High.ToString(), _s.MoboLimits.Critical.ToString());
    }

    static ThermalLimits Limits(string e, string h, string c) => new(double.Parse(e), double.Parse(h), double.Parse(c));

    void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var cpu = Limits(CpuE.Text, CpuH.Text, CpuC.Text); var gpu = Limits(GpuE.Text, GpuH.Text, GpuC.Text); var mb = Limits(MbE.Text, MbH.Text, MbC.Text);
            foreach (var l in new[] { cpu, gpu, mb }) if (!(l.Elevated < l.High && l.High < l.Critical)) throw new FormatException("Thresholds must increase: Elevated < High < Critical.");
            _s.CpuLimits = cpu; _s.GpuLimits = gpu; _s.MoboLimits = mb;
            _s.StartupDelaySeconds = Math.Clamp(int.Parse(SetDelay.Text), 0, 600); _s.IntervalSeconds = Math.Clamp(int.Parse(SetInterval.Text), 1, 60);
            _s.RetentionDays = Math.Clamp(int.Parse(SetRetention.Text), 1, 365);
            _s.NotificationsEnabled = SetNotify.IsChecked == true; _s.AutoRecovery = SetRecovery.IsChecked == true;
            _s.StartWithWindows = SetStartup.IsChecked == true;
            _s.Save();
            var exe = Environment.ProcessPath!;
            var r = _s.StartWithWindows ? StartupTask.Install(exe, _s.StartupDelaySeconds) : (StartupTask.IsInstalled() ? StartupTask.Uninstall() : (true, ""));
            SettingsMsg.Text = r.Item1 ? "Saved. Interval changes apply after restart." : "Saved, but the startup task failed: " + r.Item2;
        }
        catch (Exception ex) { SettingsMsg.Text = "Not saved: " + ex.Message; }
    }

    void Elevate_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--restarted") { Verb = "runas", UseShellExecute = true }); Application.Current.Shutdown(); }
        catch { SettingsMsg.Text = "Administrator restart was cancelled."; }
    }

    void DeleteHistory_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Delete ALL stored diagnostic history on this PC? This cannot be undone.", "Delete history", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        { _engine.Store.DeleteAll(); SettingsMsg.Text = "History deleted."; }
    }

    void RestoreFans_Click(object sender, RoutedEventArgs e)
    {
        if (_sensors is LhmSensorProvider lp) FanControlInfo.Text = $"Restored {lp.RestoreDefaults()} fan channel(s) to firmware default control.";
    }

    void Report_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { FileName = $"Guardian-report-{DateTime.Now:yyyyMMdd-HHmm}.md", Filter = "Markdown (*.md)|*.md|Text (*.txt)|*.txt" };
        if (dlg.ShowDialog() == true) { File.WriteAllText(dlg.FileName, _engine.BuildReport()); MessageBox.Show("Report saved. It stays on this PC unless you share it.", "Report"); }
    }
}

