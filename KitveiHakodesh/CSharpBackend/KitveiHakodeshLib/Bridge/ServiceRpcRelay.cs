using KitveiHakodesh.ServiceClient;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KitveiHakodeshLib.Bridge
{
    /// <summary>
    /// Serves <c>http://kitvei-service/*</c> inside the WebView2 by relaying each request to the
    /// KitveiHakodesh service's named pipe.
    ///
    /// The hosted page talks to the service with the SAME client the dev page uses
    /// (serviceClient.ts). In dev its requests reach the service's loopback HTTP host directly;
    /// in the hosted app they land here, and the body - the MessagePack <c>{Op, Args}</c>
    /// envelope - is written to the pipe untouched. The reply bytes go back untouched too.
    /// Nothing in this process decodes, re-encodes or even understands the payload, which is
    /// why KitveiHakodeshLib needs no DTO, no MessagePack and no database library of its own.
    ///
    /// Two legs:
    ///   <c>POST /rpc</c>            one pipe round-trip; the reply is the HTTP response body.
    ///   <c>POST /rpc-stream?id=X</c> the streaming ops. WebView2 cannot stream a response body
    ///                               progressively (the body must be complete when the deferral
    ///                               ends), so the frames are instead PUSHED to the page one by
    ///                               one as <c>serviceStreamFrame</c> messages carrying the
    ///                               stream id X, followed by <c>serviceStreamEnd</c>. The page
    ///                               chooses X before it POSTs, so its listener is in place
    ///                               before the first frame can arrive.
    ///   <c>POST /rpc-stream-cancel?id=X</c> closes X's pipe - the service's cancel signal.
    ///
    /// Every open stream is closed when the page navigates away (reload, app reset): the new
    /// page knows none of the old ids, so pumping on would only leak pipes.
    ///
    /// The relay host is a SEPARATE virtual host from the folder-mapped app host on purpose:
    /// WebResourceRequested never fires for a folder-mapped host, so the RPC paths could not be
    /// intercepted under it. Being cross-origin from the page costs one CORS preflight per
    /// request type, answered below.
    /// </summary>
    public sealed class ServiceRpcRelay
    {
        public const string HostName = "kitvei-service";
        public const string BaseUrl = "http://" + HostName;

        private const string CorsHeaders =
            "Access-Control-Allow-Origin: *\r\n" +
            "Access-Control-Allow-Methods: POST, OPTIONS\r\n" +
            "Access-Control-Allow-Headers: Content-Type, X-KHS-Token\r\n";

        private readonly CoreWebView2 _core;
        private readonly WebBridge _bridge;
        private readonly IKitveiHakodeshServiceChannel _channel;
        private readonly ConcurrentDictionary<string, Stream> _openStreams = new ConcurrentDictionary<string, Stream>();
        // Ids cancelled while their pipe was still being opened: the open completes, sees the
        // id here, and closes at once instead of pumping into a page that stopped listening.
        private readonly ConcurrentDictionary<string, byte> _cancelledBeforeOpen = new ConcurrentDictionary<string, byte>();

        public ServiceRpcRelay(CoreWebView2 core, WebBridge bridge, IKitveiHakodeshServiceChannel channel)
        {
            _core = core;
            _bridge = bridge;
            _channel = channel;
            // SourceKinds.All: the two-argument filter raises the event for document navigations
            // only, and these are fetch() calls from the page.
            _core.AddWebResourceRequestedFilter(
                BaseUrl + "/*", CoreWebView2WebResourceContext.All,
                CoreWebView2WebResourceRequestSourceKinds.All);
            _core.WebResourceRequested += OnWebResourceRequested;
            _core.NavigationStarting += OnNavigationStarting;
        }

        public void Detach()
        {
            try { _core.WebResourceRequested -= OnWebResourceRequested; } catch { }
            try { _core.NavigationStarting -= OnNavigationStarting; } catch { }
            CloseAllStreams();
        }

        /// <summary>Closes every streaming connection. Called on page navigation and teardown.</summary>
        public void CloseAllStreams()
        {
            foreach (var id in _openStreams.Keys) CloseStream(id);
        }

        private void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            // Only a top-level navigation replaces the page; iframes (local files) keep it.
            if (!e.IsRedirected) CloseAllStreams();
        }

        private async void OnWebResourceRequested(object sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out Uri uri)) return;
            if (!uri.Host.Equals(HostName, StringComparison.OrdinalIgnoreCase)) return;

            // The event fires on the UI thread and the awaits below resume there, so touching
            // e.Response after the pipe round-trip is safe; the deferral keeps the request open.
            CoreWebView2Deferral deferral = e.GetDeferral();
            try
            {
                e.Response = await BuildResponseAsync(e.Request, uri);
            }
            catch (KitveiHakodeshServiceUnavailableException ex)
            {
                // 503 is what serviceClient.ts retries, so a service restart (it restarts itself
                // after a database-path change) is ridden out exactly as in dev.
                e.Response = TextResponse(503, "Service Unavailable", ex.Message);
            }
            catch (Exception ex)
            {
                e.Response = TextResponse(502, "Bad Gateway", ex.ToString());
            }
            finally
            {
                deferral.Complete();
            }
        }

        private async Task<CoreWebView2WebResourceResponse> BuildResponseAsync(
            CoreWebView2WebResourceRequest request, Uri uri)
        {
            if (request.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
                return _core.Environment.CreateWebResourceResponse(new MemoryStream(), 204, "No Content", CorsHeaders);

            if (!request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase))
                return TextResponse(405, "Method Not Allowed", "Only POST is served here.");

            switch (uri.AbsolutePath)
            {
                case "/rpc":
                {
                    byte[] reply = await _channel.CallAsync(ReadAll(request.Content), CancellationToken.None);
                    return _core.Environment.CreateWebResourceResponse(
                        new MemoryStream(reply), 200, "OK",
                        CorsHeaders +
                        "Content-Type: application/octet-stream\r\n" +
                        "Content-Length: " + reply.Length);
                }
                case "/rpc-stream":
                {
                    string streamId = StreamIdOf(uri);
                    if (streamId == null) return TextResponse(400, "Bad Request", "Missing ?id=<stream id>.");
                    Stream frames = await _channel.OpenStreamAsync(ReadAll(request.Content), CancellationToken.None);
                    if (_cancelledBeforeOpen.TryRemove(streamId, out _))
                    {
                        frames.Dispose();
                        return TextResponse(202, "Accepted", "");
                    }
                    _openStreams[streamId] = frames;
                    _ = PumpFramesToPageAsync(streamId, frames);
                    return TextResponse(202, "Accepted", "");
                }
                case "/rpc-stream-cancel":
                {
                    string streamId = StreamIdOf(uri);
                    if (streamId != null && !CloseStream(streamId))
                        _cancelledBeforeOpen[streamId] = 0;
                    return TextResponse(200, "OK", "");
                }
                default:
                    return TextResponse(404, "Not Found", "No such route: " + uri.AbsolutePath);
            }
        }

        /// <summary>Reads frames off one streaming connection and pushes each to the page.
        /// Runs on the thread pool; WebBridge marshals every push onto the UI thread in order.</summary>
        private async Task PumpFramesToPageAsync(string streamId, Stream frames)
        {
            string error = null;
            try
            {
                while (true)
                {
                    byte[] frame = await KitveiHakodeshServiceFrameProtocol
                        .ReadFrameAsync(frames, CancellationToken.None).ConfigureAwait(false);
                    if (frame == null) break; // the service closed: the stream is complete
                    _bridge.PushEvent(new { @event = "serviceStreamFrame", streamId, frame = Convert.ToBase64String(frame) });
                }
            }
            catch (Exception ex)
            {
                // A cancelled stream (CloseStream disposed the pipe under us) ends here too; the
                // page has already stopped listening, so the error it carries is never shown.
                error = ex.Message;
            }
            finally
            {
                CloseStream(streamId);
                _bridge.PushEvent(new { @event = "serviceStreamEnd", streamId, error });
            }
        }

        /// <summary>True when the id named an open stream (now closed).</summary>
        private bool CloseStream(string streamId)
        {
            if (!_openStreams.TryRemove(streamId, out Stream stream)) return false;
            try { stream.Dispose(); } catch { }
            return true;
        }

        private static string StreamIdOf(Uri uri)
        {
            const string key = "id=";
            string query = uri.Query.TrimStart('?');
            foreach (string part in query.Split('&'))
                if (part.StartsWith(key, StringComparison.Ordinal) && part.Length > key.Length)
                    return Uri.UnescapeDataString(part.Substring(key.Length));
            return null;
        }

        private static byte[] ReadAll(Stream content)
        {
            if (content == null) return Array.Empty<byte>();
            using (var buffer = new MemoryStream())
            {
                content.CopyTo(buffer);
                return buffer.ToArray();
            }
        }

        private CoreWebView2WebResourceResponse TextResponse(int status, string reason, string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text ?? "");
            return _core.Environment.CreateWebResourceResponse(
                new MemoryStream(bytes), status, reason,
                CorsHeaders +
                "Content-Type: text/plain; charset=utf-8\r\n" +
                "Content-Length: " + bytes.Length);
        }
    }
}
