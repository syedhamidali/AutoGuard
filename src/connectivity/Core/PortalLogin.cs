using System.Net;
using System.Text.RegularExpressions;

namespace AutoPortal.Connectivity.Core;

public sealed record LoginResult(bool Submitted, string Message);

/// <summary>
/// Generic HTML-form portal login, written for Fortinet FortiGate captive portals (fgtauth page with hidden "magic" field)
/// but works with any simple username/password form. It does NOT solve captchas, OTP or "accept terms" steps.
/// Credentials are only ever sent to the configured portal host.
/// </summary>
public sealed class PortalLoginClient
{
    readonly ConnectivitySettings _s;
    readonly HttpMessageHandler? _handlerOverride;

    public PortalLoginClient(ConnectivitySettings s, HttpMessageHandler? handlerOverride = null) { _s = s; _handlerOverride = handlerOverride; }

    HttpClient Make()
    {
        HttpMessageHandler h = _handlerOverride ?? new HttpClientHandler
        {
            AllowAutoRedirect = true, MaxAutomaticRedirections = 5, CookieContainer = new CookieContainer(),
            // Self-signed certificate accepted ONLY for the user-configured portal host.
            ServerCertificateCustomValidationCallback = (req, _, _, errors) =>
                errors == System.Net.Security.SslPolicyErrors.None || (_s.TrustPortalCertificate && string.Equals(req.RequestUri?.Host, _s.PortalHost, StringComparison.OrdinalIgnoreCase))
        };
        return new HttpClient(h) { Timeout = TimeSpan.FromSeconds(15) };
    }

    public bool IsAllowedTarget(Uri u) =>
        !string.IsNullOrWhiteSpace(_s.PortalHost) && string.Equals(u.Host, _s.PortalHost, StringComparison.OrdinalIgnoreCase)
        && (u.Scheme == Uri.UriSchemeHttps || (_s.AllowHttpPortal && u.Scheme == Uri.UriSchemeHttp));

    public async Task<LoginResult> LoginAsync(Uri portalUrl, string user, string password, CancellationToken ct = default)
    {
        if (!IsAllowedTarget(portalUrl))
            return new(false, $"Refused: '{portalUrl.Host}' is not the configured portal host (or is plain HTTP). Credentials were not sent.");
        using var http = Make();
        try
        {
            using var page = await http.GetAsync(portalUrl, ct);
            var finalUri = page.RequestMessage?.RequestUri ?? portalUrl;
            if (!IsAllowedTarget(finalUri)) return new(false, $"Refused: portal redirected to '{finalUri.Host}'. Credentials were not sent.");
            var html = await page.Content.ReadAsStringAsync(ct);
            var form = ParseForm(html, finalUri);
            if (form is null) return new(false, "No login form found on the portal page.");
            if (!IsAllowedTarget(form.Action)) return new(false, $"Refused: form posts to '{form.Action.Host}'. Credentials were not sent.");

            var fields = new List<KeyValuePair<string, string>>();
            bool gotUser = false, gotPass = false;
            foreach (var f in form.Fields)
            {
                if (f.Type == "password") { fields.Add(new(f.Name, password)); gotPass = true; }
                else if (!gotUser && f.Type is "text" or "email" && Regex.IsMatch(f.Name, "user|login|name|email|id", RegexOptions.IgnoreCase)) { fields.Add(new(f.Name, user)); gotUser = true; }
                else if (f.Type is "hidden") fields.Add(new(f.Name, f.Value));
                else if (f.Type is "checkbox" or "radio") { /* terms boxes are not ticked automatically */ }
            }
            if (!gotUser || !gotPass) return new(false, "The form doesn't look like a simple username/password login (it may need OTP, a captcha or a terms step).");

            using var resp = await http.PostAsync(form.Action, new FormUrlEncodedContent(fields), ct);
            return new(true, $"Submitted login form (HTTP {(int)resp.StatusCode}).");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return new(false, "Login request failed: " + e.Message);
        }
    }

    public sealed record FormField(string Name, string Type, string Value);
    public sealed record ParsedForm(Uri Action, IReadOnlyList<FormField> Fields);

    /// <summary>Finds the first form containing a password input.</summary>
    public static ParsedForm? ParseForm(string html, Uri baseUri)
    {
        foreach (Match fm in Regex.Matches(html, @"<form\b([^>]*)>(.*?)</form>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var inner = fm.Groups[2].Value;
            if (!Regex.IsMatch(inner, @"type\s*=\s*[""']?password", RegexOptions.IgnoreCase)) continue;
            var action = Attr(fm.Groups[1].Value, "action");
            var target = string.IsNullOrWhiteSpace(action) ? baseUri : new Uri(baseUri, WebUtility.HtmlDecode(action));
            var fields = new List<FormField>();
            foreach (Match im in Regex.Matches(inner, @"<input\b([^>]*)>", RegexOptions.IgnoreCase))
            {
                var a = im.Groups[1].Value; var name = Attr(a, "name");
                if (string.IsNullOrEmpty(name)) continue;
                fields.Add(new(name, (Attr(a, "type") ?? "text").ToLowerInvariant(), WebUtility.HtmlDecode(Attr(a, "value") ?? "")));
            }
            return new(target, fields);
        }
        return null;
    }

    static string? Attr(string tag, string name)
    {
        var m = Regex.Match(tag, name + @"\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))", RegexOptions.IgnoreCase);
        return m.Success ? (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value) : null;
    }
}
