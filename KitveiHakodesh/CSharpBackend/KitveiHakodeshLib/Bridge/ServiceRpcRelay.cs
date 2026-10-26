using KitveiHakodeshLib.Service;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
    ///                               ends), so the frames are instead PUSHED to the page as
    ///                               shared buffers (PostSharedBufferToScript) — the MessagePack
    ///                               frames as raw bytes, no base64, no JSON — tagged with the
    ///                               stream id X; the last one also says the stream ended.
    ///                               One buffer per UI-thread turn,
    ///                               not per frame: every frame that arrived while the previous
    ///                               message was still crossing rides in the next one (see
    ///                               <see cref="StreamFramesToPage"/>). The page chooses X before
    ///                               it POSTs, so its listener is in place before the first
    ///                               frame can arrive.
    ///   <c>POST /rpc-stream-cancel?id=X</c> closes X's pipe - the service's cancel signal.
    ///   <c>POST /rpc-stream-ack?id=X&amp;n=N</c> the page consumed N more frames of X. Pushing is
    ///                               fire-and-forget, so without these credits the pipe's
    ///                               backpressure stops at this relay: a very common word pumps
    ///                               every frame into the page faster than it can consume them
    ///                               and the renderer runs out of memory.
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
            "Access-Control-Allow-Headers: Content-Type, X-KHS-Token\r\n" +
            "Access-Control-Expose-Headers: X-KHS-Unavailable\r\n";

        private readonly CoreWebView2 _core;
        private readonly WebBridge _bridge;
        private readonly IKitveiHakodeshServiceChannel _channel;
        // The WebView's UI thread. Every CoreWebView2 call must run there, and the request handler
        // resumes after the pipe round-trip wherever its await captured. The standalone app's
        // message loop installs a context; inside Word there is none on the thread, so the
        // handler resumed on the thread pool and WebView2 threw - every request failed ("cannot
        // reach the service"), and the error path's own WebView2 call took Word down.
        private readonly SynchronizationContext _uiContext;
        private readonly ConcurrentDictionary<string, Stream> _openStreams = new ConcurrentDictionary<string, Stream>();
        // Ids cancelled while their pipe was still being opened: the open completes, sees the
        // id here, and closes at once instead of pumping into a page that stopped listening.
        private readonly ConcurrentDictionary<string, byte> _cancelledBeforeOpen = new ConcurrentDictionary<string, byte>();
        // Frames a stream may have pushed but the page not yet acknowledged (see /rpc-stream-ack).
        // Must stay above the page's ack interval (STREAM_ACK_EVERY in serviceClient.ts).
        private const int StreamFrameWindow = 32;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _streamCredits = new ConcurrentDictionary<string, SemaphoreSlim>();

        public ServiceRpcRelay(CoreWebView2 core, WebBridge bridge, IKitveiHakodeshServiceChannel channel)
        {
            _core = core;
            _bridge = bridge;
            _channel = channel;
            // Built on the UI thread (the calls below require it), so a fresh WinForms context
            // posts back to this thread.
            _uiContext = SynchronizationContext.Current as System.Windows.Forms.WindowsFormsSynchronizationContext
                ?? new System.Windows.Forms.WindowsFormsSynchronizationContext();
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

            // The event fires on the UI thread. Make sure the awaits below resume there too (see
            // _uiContext), so touching e.Response after the pipe round-trip is safe; the deferral
            // keeps the request open.
            // A bare base SynchronizationContext posts to the thread pool, same as none.
            if (!(SynchronizationContext.Current is System.Windows.Forms.WindowsFormsSynchronizationContext))
                SynchronizationContext.SetSynchronizationContext(_uiContext);
            CoreWebView2Deferral deferral = e.GetDeferral();
            try
            {
                e.Response = await BuildResponseAsync(e.Request, uri);
            }
            catch (KitveiHakodeshServiceUnavailableException ex)
            {
                // 503 is what serviceClient.ts retries, so a service restart (it restarts itself
                // after a database-path change) is ridden out exactly as in dev.
                // X-KHS-Unavailable names the reason (not installed, declined, will not start) so
                // the page can show a message that says what is wrong.
                TrySetResponse(e, () => TextResponse(503, "Service Unavailable", ex.Message,
                    "X-KHS-Unavailable: " + ex.Reason + "\r\n"));
            }
            catch (Exception ex)
            {
                TrySetResponse(e, () => TextResponse(502, "Bad Gateway", ex.ToString()));
            }
            finally
            {
                // This handler is async void: anything thrown here ends the host process - which,
                // in the Word add-in, is Word.
                try { deferral.Complete(); } catch { }
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
                    _streamCredits[streamId] = new SemaphoreSlim(StreamFrameWindow);
                    _ = PumpFramesToPageAsync(streamId, frames);
                    return TextResponse(202, "Accepted", "");
                }
                case "/rpc-stream-ack":
                {
                    string streamId = StreamIdOf(uri);
                    int.TryParse(QueryValue(uri, "n"), out int n);
                    if (streamId != null && n > 0 && _streamCredits.TryGetValue(streamId, out SemaphoreSlim credits))
                        try { credits.Release(n); } catch (SemaphoreFullException) { } catch (ObjectDisposedException) { }
                    return TextResponse(200, "OK", "");
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

        /// <summary>Reads frames off one streaming connection and hands them to the page.
        /// Runs on the thread pool; <see cref="StreamFramesToPage"/> batches them onto the UI
        /// thread in order.</summary>
        private async Task PumpFramesToPageAsync(string streamId, Stream frames)
        {
            string error = null;
            _streamCredits.TryGetValue(streamId, out SemaphoreSlim credits);
            var toPage = new StreamFramesToPage(this, streamId);
            try
            {
                while (true)
                {
                    // Wait for the page to catch up before reading on, so the pipe (and with it
                    // the service) is held back instead of the page's memory filling up. A credit
                    // per frame also bounds what one message can carry: StreamFrameWindow frames.
                    if (credits != null) await credits.WaitAsync().ConfigureAwait(false);
                    byte[] frame = await KitveiHakodeshServiceFrameProtocol
                        .ReadFrameAsync(frames, CancellationToken.None).ConfigureAwait(false);
                    if (frame == null) break; // the service closed: the stream is complete
                    toPage.Add(frame);
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
                toPage.End(error);
            }
        }

        /// <summary>
        /// The frames of one stream on their way to the page, coalesced: a frame is queued and
        /// ONE flush is scheduled on the UI thread; every frame the pump reads before that
        /// flush runs joins the same <c>serviceStreamFrames</c> message. So a stream that
        /// outruns the UI thread sends one web message (one JSON, one BeginInvoke, one
        /// PostWebMessageAsJson, one parse in the page) per UI-thread turn instead of per
        /// frame, and a stream the page keeps up with still delivers every frame at once.
        /// The end marker goes through the same flush, after whatever frames are queued, so
        /// <c>serviceStreamEnd</c> can never overtake a frame.
        /// </summary>
        private sealed class StreamFramesToPage
        {
            private readonly ServiceRpcRelay _relay;
            private readonly string _streamId;
            private readonly object _gate = new object();
            private List<byte[]> _frames = new List<byte[]>();
            private bool _flushScheduled;
            private bool _ended;
            private string _endError;

            public StreamFramesToPage(ServiceRpcRelay relay, string streamId)
            {
                _relay = relay;
                _streamId = streamId;
            }

            public void Add(byte[] frame)
            {
                lock (_gate)
                {
                    _frames.Add(frame);
                    ScheduleFlush();
                }
            }

            public void End(string error)
            {
                lock (_gate)
                {
                    _ended = true;
                    _endError = error;
                    ScheduleFlush();
                }
            }

            // Under _gate. One flush in flight at a time: a second one would only split what
            // the first is about to send.
            private void ScheduleFlush()
            {
                if (_flushScheduled) return;
                _flushScheduled = true;
                _relay._uiContext.Post(_ => Flush(), null);
            }

            // UI thread. Takes everything queued so far; a frame added after this snapshot
            // schedules the next flush itself.
            private void Flush()
            {
                List<byte[]> frames;
                bool ended;
                string endError;
                lock (_gate)
                {
                    frames = _frames;
                    _frames = new List<byte[]>();
                    ended = _ended;
                    endError = _endError;
                    _flushScheduled = false;
                }
                // The end rides in the same buffer as the last frames, so it can never overtake them.
                if (frames.Count > 0 || ended)
                    _relay.PostFramesToPage(_streamId, frames, ended, endError);
            }
        }

        /// <summary>
        /// Hands frames to the page as ONE shared buffer: the bytes exactly as the pipe gave
        /// them, laid out as [u32 count] then per frame [u32 length][bytes], little-endian.
        /// The stream id and end marker travel as the buffer's small JSON tag. UI thread only.
        ///
        /// Closing our handle right after posting is safe: the page holds its own mapping until
        /// it calls chrome.webview.releaseBuffer (serviceClient.ts does, after copying out).
        /// </summary>
        private void PostFramesToPage(string streamId, List<byte[]> frames, bool ended, string endError)
        {
            long size = 4;
            foreach (byte[] frame in frames) size += 4 + frame.Length;

            string tag = System.Text.Json.JsonSerializer.Serialize(new { @event = "serviceStreamFrames", streamId, ended, error = endError });
            try
            {
                using (CoreWebView2SharedBuffer buffer = _core.Environment.CreateSharedBuffer((ulong)size))
                {
                    using (Stream stream = buffer.OpenStream())
                    using (var writer = new BinaryWriter(stream))
                    {
                        writer.Write(frames.Count);              // BinaryWriter is little-endian
                        foreach (byte[] frame in frames)
                        {
                            writer.Write(frame.Length);
                            writer.Write(frame);
                        }
                    }
                    _core.PostSharedBufferToScript(buffer, CoreWebView2SharedBufferAccess.ReadOnly, tag);
                }
            }
            catch (Exception ex)
            {
                // Out of shared memory, or the WebView is going away. Close the pipe so the
                // service stops producing, and end the stream with a plain message so the page's
                // consumer throws instead of waiting forever.
                CloseStream(streamId);
                try { _bridge.PushEvent(new { @event = "serviceStreamEnd", streamId, error = "stream delivery failed: " + ex.Message }); }
                catch { /* the WebView is gone: nobody is waiting */ }
            }
        }

        /// <summary>True when the id named an open stream (now closed).</summary>
        private bool CloseStream(string streamId)
        {
            if (!_openStreams.TryRemove(streamId, out Stream stream)) return false;
            try { stream.Dispose(); } catch { }
            // Wake a pump parked on credits; its next read hits the disposed pipe and it ends.
            // Not disposed: the pump may still be waiting on it.
            if (_streamCredits.TryRemove(streamId, out SemaphoreSlim credits))
                try { credits.Release(StreamFrameWindow); } catch (SemaphoreFullException) { }
            return true;
        }

        private static string StreamIdOf(Uri uri) => QueryValue(uri, "id");

        private static string QueryValue(Uri uri, string name)
        {
            string key = name + "=";
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

        /// <summary>Answers with an error response; if even that fails (the WebView is gone), the
        /// request is left to fail on its own rather than the exception escaping async void.</summary>
        private static void TrySetResponse(CoreWebView2WebResourceRequestedEventArgs e, Func<CoreWebView2WebResourceResponse> build)
        {
            try { e.Response = build(); } catch { }
        }

        private CoreWebView2WebResourceResponse TextResponse(int status, string reason, string text, string extraHeaders = "")
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text ?? "");
            return _core.Environment.CreateWebResourceResponse(
                new MemoryStream(bytes), status, reason,
                CorsHeaders + extraHeaders +
                "Content-Type: text/plain; charset=utf-8\r\n" +
                "Content-Length: " + bytes.Length);
        }
    }
}
