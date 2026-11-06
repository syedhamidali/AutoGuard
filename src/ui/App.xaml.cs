using System.IO;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using AutoPortal.Guardian.App.Services;
using AutoPortal.Guardian.Core.Config;
using AutoPortal.Guardian.Core.Models;
using AutoPortal.Guardian.Core.Storage;
using AutoPortal.Guardian.Hardware;
using AutoPortal.Guardian.Windows.Recovery;

namespace AutoPortal.Guardian.App;

public partial class App : System.Windows.Application
{
    Mutex? _single;
    NotifyIcon? _tray;
    GuardianEngine? _engine;
    RecoveryService? _recovery;
    ISensorProvider? _sensors;
    MainWindow? _window;
    HistoryStore? _store;

    sealed class EmptyProvider : ISensorProvider
    {
        public string Name => "No sensor provider available";
        public bool HasFullAccess => false;
        public Snapshot Read() => new();
        public void Dispose() { }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _single = new Mutex(true, @"Local\AutoPortal.SystemGuardian", out bool first);
        if (!first && !e.Args.Contains("--restarted")) { Shutdown(); return; }

        // A crash exits non-zero so Task Scheduler's restart-on-failure relaunches us.
        DispatcherUnhandledException += (_, ex) => { LogCrash(ex.Exception); ex.Handled = true; Environment.Exit(1); };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => { LogCrash(ex.ExceptionObject as Exception); Environment.Exit(1); };

        var settings = GuardianSettings.Load();
        bool demo = e.Args.Contains("--demo");
        try { _sensors = demo ? new DemoSensorProvider() : new LhmSensorProvider(); }
        catch (Exception ex) { LogCrash(ex); _sensors = new EmptyProvider(); }

        _store = new HistoryStore(Path.Combine(GuardianSettings.Dir, demo ? "demo-history.db" : "history.db"));
        _engine = new GuardianEngine(settings, _sensors, _store);
        _window = new MainWindow(_engine, settings, _sensors);

        _recovery = new RecoveryService();
        _recovery.Resumed += () => { if (settings.AutoRecovery) { _store.LogEvent("Resume", "Resumed from sleep/hibernate; monitoring restarted."); _engine.Resume(); } };
        _recovery.SessionEnding += () => ShutdownCleanly();

        BuildTray();
        _engine.Alert += f => Dispatcher.BeginInvoke(() =>
        {
            _tray!.BalloonTipIcon = f.Level == ThermalLevel.Critical ? ToolTipIcon.Error : ToolTipIcon.Warning;
            _tray.BalloonTipTitle = f.Title;
            _tray.BalloonTipText = $"{f.Explanation} Open System Guardian for recommended actions and a diagnostic report.";
            _tray.ShowBalloonTip(10000);
        });
        _engine.Start();

        if (!e.Args.Contains("--tray")) _window.Show();
    }

    void BuildTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open System Guardian", null, (_, _) => ShowWindow());
        menu.Items.Add("Pause monitoring", null, (s, _) =>
        {
            var item = (ToolStripMenuItem)s!;
            if (item.Text!.StartsWith("Pause")) { _engine!.Pause(); item.Text = "Resume monitoring"; } else { _engine!.Resume(); item.Text = "Pause monitoring"; }
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => { ShutdownCleanly(); Shutdown(); });
        _tray = new NotifyIcon { Icon = SystemIcons.Shield, Text = "AutoPortal System Guardian", Visible = true, ContextMenuStrip = menu };
        _tray.DoubleClick += (_, _) => ShowWindow();
    }

    void ShowWindow() { _window!.Show(); _window.WindowState = WindowState.Normal; _window.Activate(); }

    bool _down;
    void ShutdownCleanly()
    {
        if (_down) return; _down = true;
        try { _tray!.Visible = false; _tray.Dispose(); } catch { }
        try { _engine?.Dispose(); _recovery?.Dispose(); _sensors?.Dispose(); _store?.Dispose(); } catch { }
    }

    protected override void OnExit(ExitEventArgs e) { ShutdownCleanly(); _single?.Dispose(); base.OnExit(e); }

    static void LogCrash(Exception? ex)
    {
        try { Directory.CreateDirectory(GuardianSettings.Dir); File.AppendAllText(Path.Combine(GuardianSettings.Dir, "crash.log"), $"{DateTime.Now:s} {ex}\n\n"); } catch { }
    }
}

