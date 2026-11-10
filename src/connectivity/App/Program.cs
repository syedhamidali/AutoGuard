using System.Diagnostics;
using AutoPortal.Connectivity.Core;

namespace AutoPortal.Connectivity.App;

static class Program
{
    const string TaskName = "AutoPortal Connectivity Manager";

    [STAThread]
    static void Main(string[] args)
    {
        using var mutex = new Mutex(true, @"Local\AutoPortal.ConnectivityManager", out bool first);
        if (!first) return;
        ApplicationConfiguration.Initialize();

        var settings = ConnectivitySettings.Load();
        var creds = new WindowsCredentialStore();
        var monitor = new ConnectivityMonitor(settings, creds);
        var tray = new NotifyIcon { Icon = SystemIcons.Information, Text = "AutoPortal Connectivity Manager", Visible = true };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Settingsâ€¦", null, (_, _) => new SettingsForm(settings, creds, monitor, TaskName).ShowDialog());
        menu.Items.Add("Check now", null, async (_, _) => { try { await monitor.CheckOnceAsync(); } catch { } });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Application.Exit());
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => new SettingsForm(settings, creds, monitor, TaskName).ShowDialog();
        NetState last = NetState.Offline;
        monitor.Changed += (s, msg) =>
        {
            tray.Text = ("AutoPortal: " + msg).Length > 60 ? ("AutoPortal: " + msg)[..59] : "AutoPortal: " + msg;
            if (s != last && (s == NetState.Online || s == NetState.PortalDetected)) { tray.ShowBalloonTip(5000, "AutoPortal Connectivity", msg, ToolTipIcon.Info); }
            last = s;
        };
        monitor.Start();
        if (!args.Contains("--tray") && string.IsNullOrEmpty(settings.PortalHost)) new SettingsForm(settings, creds, monitor, TaskName).ShowDialog();
        Application.Run();
        tray.Visible = false; monitor.Dispose();
    }
}

sealed class SettingsForm : Form
{
    readonly ConnectivitySettings _s; readonly ICredentialStore _c; readonly ConnectivityMonitor _m; readonly string _task;
    readonly TextBox host = new() { Width = 260 }, user = new() { Width = 260 }, pass = new() { Width = 260, UseSystemPasswordChar = true };
    readonly CheckBox enable = new() { Text = "Log in automatically when a login page appears", AutoSize = true };
    readonly CheckBox trust = new() { Text = "Trust this portal's certificate (needed for self-signed FortiGate certificates)", AutoSize = true };
    readonly CheckBox allowHttp = new() { Text = "Portal uses plain HTTP, e.g. http://10.1.2.2:1000/â€¦ (password is sent unencrypted on your local network)", AutoSize = true, MaximumSize = new Size(440, 0) };
    readonly CheckBox startup = new() { Text = "Start with Windows", AutoSize = true };
    readonly Label status = new() { AutoSize = true, MaximumSize = new Size(440, 0) };

    public SettingsForm(ConnectivitySettings s, ICredentialStore c, ConnectivityMonitor m, string taskName)
    {
        _s = s; _c = c; _m = m; _task = taskName;
        Text = "AutoPortal Connectivity Manager"; Width = 500; Height = 470; FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterScreen; MaximizeBox = false;
        var p = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(14) };
        p.Controls.Add(new Label { Text = "Login portal address (host only, e.g. 192.168.1.1 or fw.example.com)", AutoSize = true }); p.Controls.Add(host);
        p.Controls.Add(new Label { Text = "Username", AutoSize = true, Margin = new Padding(3, 10, 3, 0) }); p.Controls.Add(user);
        p.Controls.Add(new Label { Text = "Password (stored in Windows Credential Manager, never in a file)", AutoSize = true, Margin = new Padding(3, 10, 3, 0) }); p.Controls.Add(pass);
        p.Controls.Add(enable); p.Controls.Add(trust); p.Controls.Add(allowHttp); p.Controls.Add(startup);
        var save = new Button { Text = "Save", AutoSize = true }; var clear = new Button { Text = "Delete saved password", AutoSize = true };
        var row = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 10, 0, 0) }; row.Controls.AddRange(new Control[] { save, clear });
        p.Controls.Add(row); p.Controls.Add(status); Controls.Add(p);

        host.Text = s.PortalHost; user.Text = s.Username; enable.Checked = s.AutoLoginEnabled; trust.Checked = s.TrustPortalCertificate; allowHttp.Checked = s.AllowHttpPortal;
        startup.Checked = IsTaskInstalled(); pass.Text = ""; pass.PlaceholderText = c.Load() is null ? "" : "(saved - leave blank to keep)";
        status.Text = "Status: " + m.LastMessage;
        save.Click += (_, _) => Save();
        clear.Click += (_, _) => { if (MessageBox.Show("Delete the saved password?", "Confirm", MessageBoxButtons.YesNo) == DialogResult.Yes) { _c.Clear(); status.Text = "Saved password deleted."; } };
    }

    void Save()
    {
        var h = host.Text.Trim();
        if (Uri.CheckHostName(h) == UriHostNameType.Unknown) { status.Text = "Enter a valid host name or IP address (no https:// or path)."; return; }
        _s.PortalHost = h; _s.Username = user.Text.Trim(); _s.AutoLoginEnabled = enable.Checked; _s.TrustPortalCertificate = trust.Checked; _s.AllowHttpPortal = allowHttp.Checked;
        if (!string.IsNullOrEmpty(pass.Text)) _c.Save(_s.Username, pass.Text);
        else if (_c.Load() is { } old && old.User != _s.Username) _c.Save(_s.Username, old.Password);
        _s.Save();
        var (ok, msg) = SetTask(startup.Checked);
        status.Text = ok ? "Saved." : "Saved, but \"Start with Windows\" could not be changed: " + msg;
    }

    bool IsTaskInstalled() => StartupTask.IsInstalled(_task);
    (bool ok, string message) SetTask(bool on) =>
        on ? StartupTask.Install(_task, Environment.ProcessPath!, 30) : StartupTask.Uninstall(_task);
}

