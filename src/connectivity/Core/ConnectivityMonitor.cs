namespace AutoPortal.Connectivity.Core;

/// <summary>
/// Watches connectivity, logs in to the portal when one is detected, and backs off after repeated failures
/// so a wrong password can't lock the account.
/// </summary>
public sealed class ConnectivityMonitor : IDisposable
{
    readonly ConnectivitySettings _s;
    readonly ICredentialStore _creds;
    readonly CaptivePortalDetector _detector;
    readonly PortalLoginClient _login;
    readonly CancellationTokenSource _cts = new();
    int _failures;
    DateTime _pausedUntil = DateTime.MinValue, _lastDiag = DateTime.MinValue;

    public NetState State { get; private set; } = NetState.Offline;
    public string LastMessage { get; private set; } = "Starting…";
    public event Action<NetState, string>? Changed;

    public ConnectivityMonitor(ConnectivitySettings s, ICredentialStore creds, CaptivePortalDetector? detector = null, PortalLoginClient? login = null)
    {
        _s = s; _creds = creds;
        _detector = detector ?? new CaptivePortalDetector(s.ProbeUrl, s.ProbeExpected);
        _login = login ?? new PortalLoginClient(s);
    }

    public void Start() => _ = Task.Run(LoopAsync);

    async Task LoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try { await CheckOnceAsync(_cts.Token); } catch (OperationCanceledException) { break; } catch (Exception e) { Set(State, "Error: " + e.Message); }
            try { await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _s.CheckIntervalSeconds)), _cts.Token); } catch (OperationCanceledException) { break; }
        }
    }

    public async Task CheckOnceAsync(CancellationToken ct = default)
    {
        var r = await _detector.ProbeAsync(ct);
        if (r.State != NetState.PortalDetected) { if (r.State == NetState.Online) _failures = 0; Set(r.State, r.Detail); return; }
        // Record exactly what the network sent (no credentials) so a failed login can be diagnosed afterwards.
        if (DateTime.Now - _lastDiag > TimeSpan.FromMinutes(2))
        {
            _lastDiag = DateTime.Now;
            DiagnosticLog.Write($"PORTAL probe: HTTP {r.Status?.ToString() ?? "?"} type={r.ContentType ?? "-"} portalUrl={r.PortalUrl?.ToString() ?? "(none)"} detail={r.Detail} body={DiagnosticLog.Snippet(r.Body)}");
        }
        Set(NetState.PortalDetected, "Login portal detected.");
        if (!_s.AutoLoginEnabled) { Set(NetState.PortalDetected, "Login portal detected. Auto-login is off."); return; }
        if (DateTime.Now < _pausedUntil) { Set(NetState.PortalDetected, $"Paused until {_pausedUntil:t} after repeated failed logins."); return; }
        var c = _creds.Load();
        if (c is null || string.IsNullOrEmpty(c.Value.User)) { Set(NetState.PortalDetected, "No saved credentials."); return; }

        LoginResult res;
        if (r.PortalUrl is not null) res = await _login.LoginAsync(r.PortalUrl, c.Value.User, c.Value.Password, ct);
        else if (r.Body is not null && System.Text.RegularExpressions.Regex.IsMatch(r.Body, @"type\s*=\s*[""']?password", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            res = await _login.LoginWithPageAsync(r.Body, c.Value.User, c.Value.Password, ct);   // login form is in the intercept page itself
        else { Set(NetState.PortalDetected, "A login page was detected but its address could not be determined, so nothing was sent. See connectivity.log."); return; }
        DiagnosticLog.Write("LOGIN attempt: " + res.Message);
        var after = await _detector.ProbeAsync(ct);
        if (after.State == NetState.Online) { _failures = 0; Set(NetState.Online, "Logged in to the portal. " + res.Message); return; }
        _failures++;
        if (_failures >= _s.MaxConsecutiveFailures)
        {
            _pausedUntil = DateTime.Now.AddMinutes(_s.PauseMinutesAfterFailures); _failures = 0;
            Set(NetState.PortalDetected, $"Login failed {_s.MaxConsecutiveFailures} times. Paused for {_s.PauseMinutesAfterFailures} min to protect your account. Check your password. ({res.Message})");
        }
        else Set(NetState.PortalDetected, "Login did not succeed: " + res.Message);
    }

    void Set(NetState s, string msg) { var changed = s != State || msg != LastMessage; State = s; LastMessage = msg; if (changed) Changed?.Invoke(s, msg); }

    public void Dispose() => _cts.Cancel();
}
