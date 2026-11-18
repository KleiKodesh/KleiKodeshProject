using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace KitveiHakodeshLib.WebSites
{
    /// <summary>
    /// Lets the whitelisted Torah websites (websites.json, beside index.html) load inside the
    /// app's web-view iframe.
    ///
    /// Some of them (hameshiv, responsa, tashma) send X-Frame-Options / CSP frame-ancestors,
    /// which makes the browser refuse to show them in a frame. WebView2 cannot edit a response
    /// header in place, so for a whitelisted site's PAGE (document) GET request this fetches the
    /// page itself and serves it back without those headers. The page's images, scripts and
    /// styles are not touched - they load normally.
    ///
    /// Also covers websites.json "loginDomains": login pages a site sends you to (Otzar's
    /// library login goes through OpenAthens), which are not sites but must load in the frame.
    ///
    /// GET and POST (a login form submit) both go through here; the POST body is passed on.
    /// Cookies travel both ways: the request carries the WebView2 cookies for the URL, and each
    /// Set-Cookie of the response is stored in WebView2's cookie manager as SameSite=None, so a
    /// login made inside the frame is still sent from the frame (a third-party context).
    /// </summary>
    internal sealed class WebSitesFrameHeaderStripper
    {
        private static readonly HttpClient Http = new HttpClient(new HttpClientHandler
        {
            UseCookies = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        }) { Timeout = TimeSpan.FromSeconds(30) };

        private readonly CoreWebView2 _core;
        private readonly string[] _domains;

        private WebSitesFrameHeaderStripper(CoreWebView2 core, string[] domains)
        {
            _core = core;
            _domains = domains;
        }

        /// <summary>Starts stripping for every domain listed in appDir\websites.json. Null when the file is missing or empty.</summary>
        public static WebSitesFrameHeaderStripper Attach(CoreWebView2 core, string appDir)
        {
            string[] domains = ReadDomains(Path.Combine(appDir, "websites.json"));
            if (domains.Length == 0) return null;
            var stripper = new WebSitesFrameHeaderStripper(core, domains);
            foreach (string domain in domains)
            {
                core.AddWebResourceRequestedFilter("https://" + domain + "/*", CoreWebView2WebResourceContext.Document);
                core.AddWebResourceRequestedFilter("https://*." + domain + "/*", CoreWebView2WebResourceContext.Document);
            }
            core.WebResourceRequested += stripper.OnWebResourceRequested;
            return stripper;
        }

        public void Detach()
        {
            try { _core.WebResourceRequested -= OnWebResourceRequested; } catch { }
        }

        private static string[] ReadDomains(string path)
        {
            try
            {
                if (!File.Exists(path)) return new string[0];
                using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path)))
                {
                    var domains = new List<string>();
                    foreach (JsonElement site in doc.RootElement.GetProperty("sites").EnumerateArray())
                        if (site.TryGetProperty("domains", out JsonElement list))
                            foreach (JsonElement d in list.EnumerateArray())
                                domains.Add(d.GetString().ToLowerInvariant());
                    if (doc.RootElement.TryGetProperty("loginDomains", out JsonElement logins))
                        foreach (JsonElement d in logins.EnumerateArray())
                            domains.Add(d.GetString().ToLowerInvariant());
                    return domains.Distinct().ToArray();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[WebSites] websites.json unreadable: " + ex.Message);
                return new string[0];
            }
        }

        /// <summary>True for an https address on a websites.json domain (or a login domain).</summary>
        public bool IsWebsiteAddress(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out Uri uri) && uri.Scheme == Uri.UriSchemeHttps && IsWhitelisted(uri);

        private bool IsWhitelisted(Uri uri)
        {
            string host = uri.Host.ToLowerInvariant();
            return _domains.Any(d => host == d || host.EndsWith("." + d, StringComparison.Ordinal));
        }

        private async void OnWebResourceRequested(object sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            if (e.ResourceContext != CoreWebView2WebResourceContext.Document) return;
            string method = e.Request.Method?.ToUpperInvariant();
            if (method != "GET" && method != "POST") return;
            if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out Uri uri) || uri.Scheme != Uri.UriSchemeHttps) return;
            if (!IsWhitelisted(uri)) return;

            // The awaits below must resume on the UI thread (WebView2 objects, the deferral): never add
            // ConfigureAwait(false) in this file.
            CoreWebView2Deferral deferral = e.GetDeferral();
            try
            {
                e.Response = await FetchWithoutFrameBlockAsync(e.Request, uri);
            }
            catch (Exception ex)
            {
                // A GET left unanswered is fetched by the browser the normal way. A POST is not: the
                // browser would send the form (a login) a second time.
                System.Diagnostics.Debug.WriteLine("[WebSites] fetch failed " + uri + ": " + ex.Message);
                if (method == "POST")
                    e.Response = _core.Environment.CreateWebResourceResponse(null, 502, "Bad Gateway", "");
            }
            finally
            {
                deferral.Complete();
            }
        }

        private async Task<CoreWebView2WebResourceResponse> FetchWithoutFrameBlockAsync(CoreWebView2WebResourceRequest browserRequest, Uri uri)
        {
            bool isPost = string.Equals(browserRequest.Method, "POST", StringComparison.OrdinalIgnoreCase);
            var request = new HttpRequestMessage(isPost ? HttpMethod.Post : HttpMethod.Get, uri);
            if (isPost)
            {
                var postBody = new MemoryStream();
                browserRequest.Content?.CopyTo(postBody);
                request.Content = new ByteArrayContent(postBody.ToArray());
            }
            foreach (KeyValuePair<string, string> header in browserRequest.Headers)
            {
                string name = header.Key.ToLowerInvariant();
                // No conditional headers: a 304 cannot be served as a host-built response (empty page).
                if (name == "cookie" || name == "host" || name == "accept-encoding" || name == "content-length"
                    || name == "if-none-match" || name == "if-modified-since") continue;
                if (name.StartsWith("content-", StringComparison.Ordinal))
                {
                    request.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    continue;
                }
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            if (!request.Headers.Contains("User-Agent"))
                request.Headers.TryAddWithoutValidation("User-Agent", _core.Settings.UserAgent);

            List<CoreWebView2Cookie> cookies = await _core.CookieManager.GetCookiesAsync(uri.AbsoluteUri);
            if (cookies.Count > 0)
                request.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", cookies.Select(c => c.Name + "=" + c.Value)));

            // The body is read off the UI thread (async) into memory before WebView2 gets it: WebView2
            // reads a managed stream synchronously, so a network stream would freeze the app on a slow
            // site. ResponseHeadersRead: the 30 s timeout covers getting an answer, not the download.
            var body = new MemoryStream();
            using (HttpResponseMessage response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
            {
                await response.Content.CopyToAsync(body);
                body.Position = 0;
                return BuildResponse(uri, response, body);
            }
        }

        private CoreWebView2WebResourceResponse BuildResponse(Uri uri, HttpResponseMessage response, Stream body)
        {

            var headers = new StringBuilder();
            foreach (KeyValuePair<string, IEnumerable<string>> header in response.Headers.Concat(response.Content.Headers))
            {
                string name = header.Key.ToLowerInvariant();
                if (name == "x-frame-options" || name == "content-length" || name == "transfer-encoding" || name == "content-encoding")
                    continue;
                foreach (string value in header.Value)
                {
                    string v = value;
                    if (name == "content-security-policy" || name == "content-security-policy-report-only")
                        v = string.Join(";", value.Split(';').Where(p => !p.Trim().StartsWith("frame-ancestors", StringComparison.OrdinalIgnoreCase)));
                    else if (name == "set-cookie")
                    {
                        // Stored directly: WebView2 is not relied on to apply Set-Cookie from a
                        // response the host built itself.
                        StoreCookie(uri, value);
                        continue;
                    }
                    headers.Append(header.Key).Append(": ").Append(v).Append("\r\n");
                }
            }

            return _core.Environment.CreateWebResourceResponse(
                body, (int)response.StatusCode, response.ReasonPhrase ?? "OK", headers.ToString());
        }

        /// <summary>One Set-Cookie header into WebView2's cookie manager, as SameSite=None; Secure.</summary>
        private void StoreCookie(Uri uri, string setCookie)
        {
            try
            {
                string[] parts = setCookie.Split(';');
                int eq = parts[0].IndexOf('=');
                if (eq <= 0) return;
                string name = parts[0].Substring(0, eq).Trim();
                string value = parts[0].Substring(eq + 1).Trim();
                string domain = uri.Host, path = "/";
                DateTime? expires = null;
                bool httpOnly = false;
                foreach (string raw in parts.Skip(1))
                {
                    string attr = raw.Trim();
                    int i = attr.IndexOf('=');
                    string key = (i < 0 ? attr : attr.Substring(0, i)).Trim().ToLowerInvariant();
                    string val = i < 0 ? "" : attr.Substring(i + 1).Trim();
                    if (key == "domain" && val.Length > 0) domain = val;
                    else if (key == "path" && val.Length > 0) path = val;
                    else if (key == "httponly") httpOnly = true;
                    else if (key == "max-age" && long.TryParse(val, out long seconds)) expires = seconds > 315360000 ? DateTime.UtcNow.AddYears(10) : DateTime.UtcNow.AddSeconds(seconds);
                    else if (key == "expires" && expires == null && DateTime.TryParse(val, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out DateTime at)) expires = at;
                }
                CoreWebView2Cookie cookie = _core.CookieManager.CreateCookie(name, value, domain, path);
                cookie.IsSecure = true;
                cookie.IsHttpOnly = httpOnly;
                cookie.SameSite = CoreWebView2CookieSameSiteKind.None;
                if (expires.HasValue)
                {
                    if (expires.Value <= DateTime.UtcNow) { _core.CookieManager.DeleteCookie(cookie); return; }
                    cookie.Expires = expires.Value.ToLocalTime();
                }
                _core.CookieManager.AddOrUpdateCookie(cookie);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[WebSites] cookie not stored: " + ex.Message);
            }
        }
    }
}
