using System.Net;
using System.Text;
using AutoPortal.Connectivity.Core;
using Xunit;

namespace AutoPortal.Guardian.Tests;

/// <summary>A fake FortiGate-style portal served over loopback HTTP; no real network or credentials involved.</summary>
sealed class FakePortal : IDisposable
{
    readonly HttpListener _l = new();
    public int Port { get; }
    public string? PostedBody; public volatile bool LoggedIn;
    public string FormHtml = "<html><body><form action=\"/\" method=\"post\"><input type=\"hidden\" name=\"magic\" value=\"abc123\"><input type=\"hidden\" name=\"4Tredir\" value=\"/x?a=1&amp;b=2\"><input type=\"text\" name=\"username\"><input type=\"password\" name=\"password\"><input type=\"submit\"></form></body></html>";

    public FakePortal()
    {
        Port = new Random().Next(20000, 40000);
        _l.Prefixes.Add($"http://127.0.0.1:{Port}/"); _l.Start();
        _ = Task.Run(async () =>
        {
            while (_l.IsListening)
            {
                HttpListenerContext c; try { c = await _l.GetContextAsync(); } catch { break; }
                var path = c.Request.Url!.AbsolutePath;
                if (c.Request.HttpMethod == "POST") { using var r = new StreamReader(c.Request.InputStream); PostedBody = r.ReadToEnd(); LoggedIn = PostedBody.Contains("password=secret%21"); Write(c, "ok"); }
                else if (path == "/connecttest.txt") { if (LoggedIn) Write(c, "Microsoft Connect Test"); else { c.Response.StatusCode = 302; c.Response.RedirectLocation = $"http://127.0.0.1:{Port}/login"; c.Response.Close(); } }
                else Write(c, FormHtml);
            }
        });
    }
    static void Write(HttpListenerContext c, string s) { var b = Encoding.UTF8.GetBytes(s); c.Response.OutputStream.Write(b); c.Response.Close(); }
    public void Dispose() => _l.Close();
}

sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> f) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => Task.FromResult(f(r));
}

public class ConnectivityTests
{
    static ConnectivitySettings Cfg(FakePortal p) => new() { PortalHost = "127.0.0.1", AllowHttpPortal = true, ProbeUrl = $"http://127.0.0.1:{p.Port}/connecttest.txt", AutoLoginEnabled = true, MaxConsecutiveFailures = 2 };

    [Fact]
    public async Task DetectsPortalViaRedirect()
    {
        using var p = new FakePortal();
        var r = await new CaptivePortalDetector(Cfg(p).ProbeUrl, "Microsoft Connect Test").ProbeAsync();
        Assert.Equal(NetState.PortalDetected, r.State); Assert.Contains("/login", r.PortalUrl!.ToString());
    }

    [Theory]
    [InlineData("<meta http-equiv=\"refresh\" content=\"0; url=http://10.1.2.2:1000/fgtauth?23b173b9993a093b\">", "http://10.1.2.2:1000/fgtauth?23b173b9993a093b")]
    [InlineData("<script>window.location=\"http://10.1.2.2:1000/fgtauth?abc\";</script>", "http://10.1.2.2:1000/fgtauth?abc")]
    [InlineData("<a href='http://10.1.2.2:1000/fgtauth?x=1&amp;y=2'>Login</a>", "http://10.1.2.2:1000/fgtauth?x=1&y=2")]
    public void ExtractsPortalAddressFromInterceptPage(string body, string expected) =>
        Assert.Equal(expected, CaptivePortalDetector.ExtractRedirect(body)!.ToString());

    [Fact]
    public void NoAddressInPageMeansNull() => Assert.Null(CaptivePortalDetector.ExtractRedirect("<html>Welcome to the hotel network</html>"));

    [Fact]
    public async Task InterceptPageWithoutAddressSendsNothing()
    {
        using var p = new FakePortal(); var s = Cfg(p); var store = new InMemoryCredentialStore(); store.Save("alice", "secret!");
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>please sign in</html>") });
        using var m = new ConnectivityMonitor(s, store, new CaptivePortalDetector(s.ProbeUrl, s.ProbeExpected, handler));
        await m.CheckOnceAsync();
        Assert.Null(p.PostedBody); Assert.Contains("could not be determined", m.LastMessage);
    }

    [Fact]
    public async Task PortalOnNonStandardPortOverPlainHttpWorksWhenAllowed()
    {
        // mirrors a FortiGate at http://<ip>:1000/fgtauth?<magic>: same host, odd port, plain HTTP, opt-in
        using var p = new FakePortal(); var s = Cfg(p); s.AllowHttpPortal = true;
        Assert.True(new PortalLoginClient(s).IsAllowedTarget(new Uri($"http://127.0.0.1:{p.Port}/fgtauth?23b173b9993a093b")));
        s.AllowHttpPortal = false;
        Assert.False(new PortalLoginClient(s).IsAllowedTarget(new Uri($"http://127.0.0.1:{p.Port}/fgtauth?23b173b9993a093b")));
    }

    [Fact]
    public async Task UnreachableMeansOffline() =>
        Assert.Equal(NetState.Offline, (await new CaptivePortalDetector("http://127.0.0.1:1/x", "x").ProbeAsync()).State);

    [Fact]
    public void ParsesFortiGateStyleForm()
    {
        using var p = new FakePortal();
        var f = PortalLoginClient.ParseForm(p.FormHtml, new Uri("http://127.0.0.1/login"))!;
        Assert.Equal("http://127.0.0.1/", f.Action.ToString());
        Assert.Contains(f.Fields, x => x.Name == "magic" && x.Value == "abc123");
        Assert.Contains(f.Fields, x => x.Name == "4Tredir" && x.Value == "/x?a=1&b=2");
    }

    [Fact]
    public async Task FullFlowLogsInAndInternetBecomesReachable()
    {
        using var p = new FakePortal(); var s = Cfg(p); var store = new InMemoryCredentialStore(); store.Save("alice", "secret!");
        using var m = new ConnectivityMonitor(s, store);
        await m.CheckOnceAsync();
        Assert.Equal(NetState.Online, m.State);
        Assert.Contains("username=alice", p.PostedBody); Assert.Contains("magic=abc123", p.PostedBody);
    }

    [Fact]
    public async Task NeverSendsCredentialsToOtherHosts()
    {
        using var p = new FakePortal(); var s = Cfg(p); s.PortalHost = "portal.example.com";
        var r = await new PortalLoginClient(s).LoginAsync(new Uri($"http://127.0.0.1:{p.Port}/login"), "alice", "secret!");
        Assert.False(r.Submitted); Assert.Null(p.PostedBody);
    }

    [Fact]
    public async Task RefusesPlainHttpUnlessAllowed()
    {
        using var p = new FakePortal(); var s = Cfg(p); s.AllowHttpPortal = false;
        Assert.False((await new PortalLoginClient(s).LoginAsync(new Uri($"http://127.0.0.1:{p.Port}/login"), "a", "b")).Submitted);
    }

    [Fact]
    public async Task StopsAfterRepeatedFailuresToProtectAccount()
    {
        using var p = new FakePortal(); var s = Cfg(p); var store = new InMemoryCredentialStore(); store.Save("alice", "WRONG");
        using var m = new ConnectivityMonitor(s, store);
        await m.CheckOnceAsync(); await m.CheckOnceAsync();
        Assert.Contains("Paused", m.LastMessage);
        p.PostedBody = null; await m.CheckOnceAsync();
        Assert.Null(p.PostedBody); // no further attempts while paused
    }

    [Fact]
    public async Task FormWithoutPasswordFieldIsNotSubmitted()
    {
        using var p = new FakePortal { FormHtml = "<form action='/'><input type='checkbox' name='accept'></form>" };
        var r = await new PortalLoginClient(Cfg(p)).LoginAsync(new Uri($"http://127.0.0.1:{p.Port}/login"), "a", "b");
        Assert.False(r.Submitted);
    }

    [Fact]
    public void StartupTaskXmlIsValidLogonTaskForTheCurrentUserWithoutAdmin()
    {
        var xml = StartupTask.BuildXml(@"C:\Program Files\X\a.exe", "S-1-5-21-1", 30);
        var doc = System.Xml.Linq.XDocument.Parse(xml);
        Assert.NotNull(doc.Root);
        Assert.Contains("<LogonTrigger>", xml); Assert.Contains("PT30S", xml);
        Assert.Contains("LeastPrivilege", xml);          // never needs elevation to start
        Assert.Contains("S-1-5-21-1", xml);              // user-specific trigger (works for non-admin creation)
        Assert.Contains("--tray", xml); Assert.Contains("RestartOnFailure", xml);
    }

    [Fact]
    public void StartupTaskCanBeCreatedAndRemovedByANormalUser()
    {
        // Regression test: `schtasks /SC ONLOGON` failed with "Access is denied" for non-admins; the XML route must work.
        var name = "apc-test-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var (ok, msg) = StartupTask.Install(name, Environment.ProcessPath!, 5);
            Assert.True(ok, "Install failed: " + msg);
            Assert.True(StartupTask.IsInstalled(name));
        }
        finally { StartupTask.Uninstall(name); }          // removes only this test's own temporary task
        Assert.False(StartupTask.IsInstalled(name));
    }

    [Fact]
    public void AppNeverUsesTheAdminOnlyCommandLineLogonTask()
    {
        foreach (var f in new[] { "src/connectivity/App/Program.cs", "src/connectivity/Core/StartupTask.cs" })
        {
            // ignore comments: the explanation of why this is forbidden is allowed to mention it
            var code = string.Join("\n", Repo.Read(f).Split('\n').Where(l => !l.TrimStart().StartsWith("//")));
            Assert.DoesNotContain("/sc onlogon", code.ToLowerInvariant());
        }
    }

    [Fact]
    public void SettingsFileNeverContainsPassword()
    {
        var path = Path.Combine(Path.GetTempPath(), "apc-" + Guid.NewGuid() + ".json");
        try { new ConnectivitySettings { Username = "alice" }.Save(path); Assert.DoesNotContain("assword", File.ReadAllText(path)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void WindowsCredentialStoreRoundTrip()
    {
        var store = new WindowsCredentialStore(); var prior = store.Load();
        try { store.Save("tester", "pä$$ w0rd"); Assert.Equal(("tester", "pä$$ w0rd"), store.Load()); }
        finally { if (prior is { } p) store.Save(p.User, p.Password); else store.Clear(); }
    }
}
