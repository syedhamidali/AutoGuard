using System.Net;

namespace AutoPortal.Connectivity.Core;

public enum NetState { Online, PortalDetected, Offline }
public sealed record ProbeResult(NetState State, Uri? PortalUrl, string Detail);

/// <summary>Detects a captive portal by requesting a known URL without following redirects.</summary>
public sealed class CaptivePortalDetector
{
    readonly HttpClient _http;
    readonly string _probe, _expected;

    public CaptivePortalDetector(string probeUrl, string expected, HttpMessageHandler? handler = null)
    {
        _probe = probeUrl; _expected = expected;
        _http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(8) };
    }

    /// <summary>Finds an absolute http(s) URL in a meta refresh, JavaScript location assignment, or link of an intercept page.</summary>
    public static Uri? ExtractRedirect(string body)
    {
        var m = System.Text.RegularExpressions.Regex.Match(body,
            @"(?:url\s*=\s*|location(?:\.href)?\s*=\s*|window\.open\(|href\s*=\s*)['""]?(https?://[^'""\s<>)]+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success && Uri.TryCreate(System.Net.WebUtility.HtmlDecode(m.Groups[1].Value), UriKind.Absolute, out var u) ? u : null;
    }

    public async Task<ProbeResult> ProbeAsync(CancellationToken ct = default)
    {
        try
        {
            using var r = await _http.GetAsync(_probe, ct);
            if (r.StatusCode is HttpStatusCode.Moved or HttpStatusCode.Found or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                var loc = r.Headers.Location;
                if (loc != null && !loc.IsAbsoluteUri) loc = new Uri(new Uri(_probe), loc);
                return new(NetState.PortalDetected, loc, "Redirected to " + loc);
            }
            var body = (await r.Content.ReadAsStringAsync(ct)).Trim();
            if (r.IsSuccessStatusCode && body.Contains(_expected, StringComparison.OrdinalIgnoreCase)) return new(NetState.Online, null, "Internet reachable");
            // 200 with different content: some portals intercept without an HTTP redirect and redirect from the page body
            // (meta refresh or JavaScript). Extract that address; never fall back to the probe URL, which is not the portal.
            var url = ExtractRedirect(body);
            return new(NetState.PortalDetected, url, url is null ? "Unexpected response (possible portal), but its address could not be determined" : "Portal page redirects to " + url);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return new(NetState.Offline, null, "No network: " + e.Message);
        }
    }
}
