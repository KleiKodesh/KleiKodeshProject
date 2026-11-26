using Microsoft.Web.WebView2.Core;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KitveiHakodeshLib.Pdf
{
    /// <summary>A PDF the hidden viewer could not open; <see cref="Code"/> says why.</summary>
    internal sealed class PdfLoadException : IOException
    {
        public const string PasswordRequired = "password required";
        public const string WrongPassword = "wrong password";
        public string Code { get; }
        public PdfLoadException(string code, string message) : base(message) { Code = code; }
    }

    /// <summary>
    /// Knows when a PDF loaded into a WebView2 is really ready to print. Event-driven, no clocks.
    ///
    /// The WebView2 PDF viewer is Edge's own extension page (a "webview" DevTools target whose
    /// url is chrome-extension://.../edge_pdf/index.html). NavigationCompleted, load and the
    /// network events all come long before the document is in the viewer; the viewer's own
    /// window.viewer.loaded promise resolves when it is. We attach to that target and await the
    /// promise there. Each way the wait can end:
    ///   loaded     viewer.loaded resolved and loadState_ is not 'failed'      -> Verified
    ///   failed     loadState_ became 'failed' / loaded rejected                -> error
    ///   password   the viewer asked for a password (it waits in a hidden window) -> error
    ///   internals  the extension frame reached network idle and still has no viewer
    ///              object with a loaded promise (Edge changed): the document is
    ///              its last page is printed to memory until it comes out              -> Unverified
    ///   navigation failed / process failed / viewer target gone / cancelled    -> error
    /// </summary>
    internal static class PdfViewerReady
    {
        public enum Result { Verified, Unverified }

        // Runs inside the extension frame; resolves to one word. The real viewer object is
        // assigned to window.viewer a little after the page's load event (before that, window.viewer
        // is just the element with id "viewer"), so a setter on window.viewer is the event for it.
        private static string WaitScript = @"(async()=>{
 const isViewer=x=>!!(x&&x.loaded&&typeof x.loaded.then==='function');
 const v=await new Promise(res=>{
  if(isViewer(window.viewer)) return res(window.viewer);
  let cur=window.viewer;
  try{ Object.defineProperty(window,'viewer',{configurable:true,get:()=>cur,set:x=>{ cur=x; if(isViewer(x)) res(x); }}); }catch(e){}
 });
 const failedNow=()=>v.loadState_==='failed';
 const PW=/*PW*/null; let tried=false;
 const pw=new Promise(res=>{
  // The viewer asks for a password (hidden window). With one in hand it is submitted the way the
  // viewer's own dialog does; a second request after that means it was wrong.
  const ask=()=>{
   if(PW===null||typeof v.onPasswordSubmitted_!=='function') return res('password');
   if(tried) return res('wrongpassword');
   tried=true;
   Promise.resolve().then(()=>v.onPasswordSubmitted_({detail:{eventData:{password:PW}}}));
  };
  const has=()=>!!document.getElementById('password-screen');
  if(has()) ask();
  if(PW===null){
   const mo=new MutationObserver(()=>{ if(has()){ mo.disconnect(); res('password'); } });
   mo.observe(document.documentElement,{childList:true,subtree:true});
  }
  if(typeof v.handlePasswordRequest==='function'){ const o=v.handlePasswordRequest; v.handlePasswordRequest=function(){ const r=o.apply(this,arguments); ask(); return r; }; }
 });
 const fl=new Promise(res=>{
  if(failedNow()) return res('failed');
  let cur=v.loadState_;
  try{ Object.defineProperty(v,'loadState_',{configurable:true,get:()=>cur,set:x=>{ cur=x; if(x==='failed') res('failed'); }}); }catch(e){}
 });
 const ld=v.loaded.then(()=>failedNow()?'failed':'loaded',()=>'failed');
 return await Promise.race([ld,pw,fl]);
})()";

        // Asked when the extension frame reports its network idle: is the viewer there?
        private static string HasViewerScript = "(()=>{const v=window.viewer;return !!(v&&v.loaded&&typeof v.loaded.then==='function');})()";

        /// <summary>
        /// Start the navigation (<paramref name="navigate"/> is called once everything is hooked)
        /// and return when the loaded document can be printed; throws when it never can.
        /// <paramref name="log"/> gets which signal ended the wait.
        /// </summary>
        public static async Task<Result> WaitAsync(CoreWebView2 core, Action navigate, CancellationToken ct, Action<string> log,
            Func<CancellationToken, Task> proveByOutput, string password = null)
        {
            var end = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);
            var committed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ulong navId = 0; bool navSeen = false;
            string viewerTarget = null; Task probeTask = null;
            var sw = Stopwatch.StartNew();
            void Fail(string signal, Exception ex) { log(signal + " @" + sw.ElapsedMilliseconds + "ms"); end.TrySetException(ex); }

            EventHandler<CoreWebView2NavigationStartingEventArgs> onStart = (_, e) =>
            {
                if (navSeen || e.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return;
                navSeen = true; navId = e.NavigationId;
            };
            EventHandler<CoreWebView2ContentLoadingEventArgs> onCommit = (_, e) =>
            {
                if (navSeen && e.NavigationId == navId) committed.TrySetResult(true);
            };
            EventHandler<CoreWebView2NavigationCompletedEventArgs> onDone = (_, e) =>
            {
                if (navSeen && e.NavigationId == navId && !e.IsSuccess)
                    Fail("navigation-failed", new PdfLoadException("load", "the file could not be loaded: " + e.WebErrorStatus));
            };
            EventHandler<CoreWebView2ProcessFailedEventArgs> onProcess = (_, e) =>
                Fail("process-failed", new PdfLoadException("process", "the browser process failed: " + e.ProcessFailedKind));

            void OnTarget(object _, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
            {
                using (var json = JsonDocument.Parse(e.ParameterObjectAsJson))
                {
                    if (!json.RootElement.TryGetProperty("targetInfo", out var info)) return;
                    string url = info.TryGetProperty("url", out var u) ? u.GetString() : null;
                    if (viewerTarget != null || url == null || !url.StartsWith("chrome-extension://") || !url.Contains("edge_pdf")) return;
                    viewerTarget = info.GetProperty("targetId").GetString();
                }
                _ = EvaluateAsync();
            }
            void OnGone(object _, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
            {
                using (var json = JsonDocument.Parse(e.ParameterObjectAsJson))
                {
                    string id = json.RootElement.TryGetProperty("targetId", out var t) ? t.GetString() : null;
                    if (viewerTarget != null && id == viewerTarget)
                        Fail("viewer-target-gone", new PdfLoadException("closed", "the PDF viewer closed before the document loaded"));
                }
            }
            string session = null; bool viewerKnown = false;
            void OnLifecycle(object _, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
            {
                if (session == null || e.SessionId != session || viewerKnown) return;
                using (var json = JsonDocument.Parse(e.ParameterObjectAsJson))
                {
                    string name = json.RootElement.TryGetProperty("name", out var n) ? n.GetString() : null;
                    if (name != "networkIdle") return;
                }
                _ = CheckViewerExistsAsync();
            }
            // The extension page has loaded everything it ever fetches: if the viewer object is
            // still not there, Edge changed its internals - printing is then proven by output.
            async Task CheckViewerExistsAsync()
            {
                try
                {
                    string reply = await core.CallDevToolsProtocolMethodForSessionAsync(session, "Runtime.evaluate",
                        JsonSerializer.Serialize(new { expression = HasViewerScript, returnByValue = true }));
                    using (var j = JsonDocument.Parse(reply))
                    {
                        bool has = j.RootElement.TryGetProperty("result", out var r) && r.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.True;
                        if (has) { viewerKnown = true; return; }
                    }
                    // Not proof yet: on a big file the viewer object can appear after the idle event.
                    // So the output probe runs alongside; whichever shows readiness first wins
                    // (viewer.loaded is the stronger one and still ends the wait when it comes).
                    log("viewer object not there at network idle @" + sw.ElapsedMilliseconds + "ms: probing by output");
                    probeTask = ProbeAsync();
                }
                catch (Exception ex) { end.TrySetException(ex); }
            }
            var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            async Task ProbeAsync()
            {
                try
                {
                    await proveByOutput(probeCts.Token);
                    log("output probe printed the last page @" + sw.ElapsedMilliseconds + "ms");
                    end.TrySetResult(Result.Unverified);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { end.TrySetException(ex); }
            }
            async Task EvaluateAsync()
            {
                try
                {
                    string attached = await core.CallDevToolsProtocolMethodAsync("Target.attachToTarget",
                        "{\"targetId\":\"" + viewerTarget + "\",\"flatten\":true}");
                    string sid;
                    using (var j = JsonDocument.Parse(attached)) sid = j.RootElement.GetProperty("sessionId").GetString();
                    session = sid;
                    await core.CallDevToolsProtocolMethodForSessionAsync(sid, "Page.enable", "{}");
                    await core.CallDevToolsProtocolMethodForSessionAsync(sid, "Page.setLifecycleEventsEnabled", "{\"enabled\":true}");
                    string reply = await core.CallDevToolsProtocolMethodForSessionAsync(sid, "Runtime.evaluate",
                        // The password only goes into this one script text (never logged or stored).
                        JsonSerializer.Serialize(new { expression = WaitScript.Replace("/*PW*/null", password == null ? "null" : JsonSerializer.Serialize(password)), awaitPromise = true, returnByValue = true }));
                    string word;
                    using (var j = JsonDocument.Parse(reply))
                        word = j.RootElement.TryGetProperty("result", out var r) && r.TryGetProperty("value", out var v) ? v.GetString() : "error:" + reply;
                    log("viewer says '" + word + "' @" + sw.ElapsedMilliseconds + "ms");
                    switch (word)
                    {
                        case "loaded": end.TrySetResult(Result.Verified); break;
                        case "password": end.TrySetException(new PdfLoadException(PdfLoadException.PasswordRequired, "the PDF is password protected")); break;
                        case "wrongpassword": end.TrySetException(new PdfLoadException(PdfLoadException.WrongPassword, "the PDF password is wrong")); break;
                        case "failed": end.TrySetException(new PdfLoadException("failed", "the PDF could not be opened (damaged or not a PDF)")); break;
                        default: end.TrySetException(new PdfLoadException("viewer", "the PDF viewer answered unexpectedly: " + word)); break;
                    }
                }
                catch (Exception ex) { end.TrySetException(ex); }
            }

            var onCreated = core.GetDevToolsProtocolEventReceiver("Target.targetCreated");
            var onChanged = core.GetDevToolsProtocolEventReceiver("Target.targetInfoChanged");
            var onDestroyed = core.GetDevToolsProtocolEventReceiver("Target.targetDestroyed");
            var onDetached = core.GetDevToolsProtocolEventReceiver("Target.detachedFromTarget");
            var onLifecycle = core.GetDevToolsProtocolEventReceiver("Page.lifecycleEvent");
            core.NavigationStarting += onStart; core.ContentLoading += onCommit; core.NavigationCompleted += onDone; core.ProcessFailed += onProcess;
            onCreated.DevToolsProtocolEventReceived += OnTarget; onChanged.DevToolsProtocolEventReceived += OnTarget;
            onDestroyed.DevToolsProtocolEventReceived += OnGone; onDetached.DevToolsProtocolEventReceived += OnGone;
            onLifecycle.DevToolsProtocolEventReceived += OnLifecycle;
            using (ct.Register(() => end.TrySetCanceled()))
            {
                try
                {
                    await core.CallDevToolsProtocolMethodAsync("Target.setDiscoverTargets", "{\"discover\":true}");
                    navigate();
                    Result result = await end.Task;
                    // Never print before the navigation has committed: a print then returns the
                    // previous (blank) page as if it were the document.
                    await committed.Task;
                    log("ready: " + result + " @" + sw.ElapsedMilliseconds + "ms");
                    return result;
                }
                finally
                {
                    probeCts.Cancel();
                    // A probe print still running must not overlap the caller's print.
                    if (probeTask != null) await probeTask;
                    core.NavigationStarting -= onStart; core.ContentLoading -= onCommit; core.NavigationCompleted -= onDone; core.ProcessFailed -= onProcess;
                    onCreated.DevToolsProtocolEventReceived -= OnTarget; onChanged.DevToolsProtocolEventReceived -= OnTarget;
                    onDestroyed.DevToolsProtocolEventReceived -= OnGone; onDetached.DevToolsProtocolEventReceived -= OnGone;
                    onLifecycle.DevToolsProtocolEventReceived -= OnLifecycle;
                }
            }
        }
    }
}
