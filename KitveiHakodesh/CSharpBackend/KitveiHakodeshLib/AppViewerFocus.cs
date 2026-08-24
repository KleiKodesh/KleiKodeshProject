using System;
using System.Globalization;
using System.Windows.Forms;

namespace KitveiHakodeshLib
{
    // Makes horizontal trackpad swipes reach the web app regardless of WinForms focus.
    //
    // Windows delivers WM_MOUSEHWHEEL (the two-finger horizontal trackpad scroll) to the
    // FOCUSED window — not the window under the cursor. A WinForms-hosted WebView2 does
    // NOT hold focus until the user clicks inside it, so until then the message goes to
    // the host Form and the web content never sees a wheel event. That's why swipe-to-
    // switch-tab did nothing until you clicked the page (and why it happens with OR
    // without the chrome-tabs strip).
    //
    // Fix: intercept WM_MOUSEHWHEEL in this thread's message pump (IMessageFilter) — the
    // exact path the message takes WHEN the web content isn't focused — and forward it
    // into the page, reusing the existing JS swipe handler by synthesizing a wheel event.
    // When the web content IS focused, the message goes straight to Chromium's own loop
    // (never reaching this filter) and the JS handler fires directly, so the two paths are
    // complementary and don't double-fire.
    //
    // A best-effort FocusWebContent() is kept too (focus the content on load / activation)
    // — harmless, and nice when it does take — but the filter is what actually guarantees
    // the gesture works from a cold start.
    public partial class AppViewer : IMessageFilter
    {
        private const int WM_MOUSEHWHEEL = 0x020E;

        // Chrome presses. WM_NCCALCSIZE turns the caption into CLIENT space (see
        // FluentChromeTabsForm.NonClient), so a press on the tab strip / title bar arrives
        // as a plain WM_LBUTTONDOWN on a WinForms control. The NC variants still cover the
        // parts that stayed non-client (resize borders, and the caption when the strip is
        // hidden), and the right/middle buttons open native context menus that must dismiss
        // page overlays too.
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int WM_NCRBUTTONDOWN = 0x00A4;
        private const int WM_NCMBUTTONDOWN = 0x00A7;

        private bool _wheelFilterInstalled;

        /// <summary>
        /// Gives the WebView2 web content OS focus. Best-effort — no-ops if not ready or
        /// visible, marshals to the UI thread if needed, never throws.
        /// </summary>
        public void FocusWebContent()
        {
            try
            {
                if (IsDisposed || _webView.IsDisposed || !Visible) return;
                if (_webView.CoreWebView2 == null) return;
                if (InvokeRequired)
                {
                    if (IsHandleCreated) BeginInvoke(new Action(FocusWebContent));
                    return;
                }
                _webView.Focus();
            }
            catch { /* best-effort — focus is a nicety, never fatal */ }
        }

        private void OnHostFormActivated(object sender, EventArgs e) => FocusWebContent();

        /// <summary>Registers the horizontal-wheel message filter once. Idempotent.</summary>
        private void InstallHorizontalWheelFilter()
        {
            if (_wheelFilterInstalled) return;
            _wheelFilterInstalled = true;
            Application.AddMessageFilter(this);
        }

        private void UninstallHorizontalWheelFilter()
        {
            if (!_wheelFilterInstalled) return;
            _wheelFilterInstalled = false;
            Application.RemoveMessageFilter(this);
        }

        private static bool IsMouseDownMessage(int msg)
        {
            return msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN || msg == WM_MBUTTONDOWN
                || msg == WM_NCLBUTTONDOWN || msg == WM_NCRBUTTONDOWN || msg == WM_NCMBUTTONDOWN;
        }

        bool IMessageFilter.PreFilterMessage(ref Message m)
        {
            if (IsMouseDownMessage(m.Msg))
            {
                try { NotifyChromePressedIfOutsideWebView(m.HWnd); }
                catch { /* best-effort */ }
                return false; // never consume — the strip still handles its own gesture
            }

            if (m.Msg != WM_MOUSEHWHEEL) return false; // cheap fast-path for every other message
            try
            {
                // Only when OUR top-level window is the active one. If the web content had
                // focus the message would be consumed by Chromium's own loop and never reach
                // here; getting it here means WinForms holds focus → forward it to the page.
                Form host = FindForm();
                if (host == null || Form.ActiveForm != host) return false;

                // HIWORD(wParam) is the signed wheel delta (+ = right, matching DOM deltaX).
                short delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
                ForwardHorizontalWheel(delta);
            }
            catch { /* best-effort */ }
            return false; // never consume — let default processing continue
        }

        /// <summary>
        /// Tells the page that a native chrome surface (tab strip, title bar, caption
        /// buttons, resize border) was pressed, so it can dismiss click-outside overlays.
        ///
        /// Those surfaces are separate Win32 windows, so the press never reaches the DOM and
        /// no `onClickOutside` handler can observe it. The window `blur` fallback doesn't
        /// cover it either: the strip handles the mouse without durably taking focus, so the
        /// page often never blurs at all.
        ///
        /// Presses INSIDE the WebView2 are ignored — the DOM already sees those, and echoing
        /// them would close a dropdown the moment the user clicked its own contents. The
        /// WebView2 hosts Chromium's render surface in child HWNDs, so this walks the parent
        /// chain rather than comparing handles.
        /// </summary>
        private void NotifyChromePressedIfOutsideWebView(IntPtr pressedHwnd)
        {
            if (IsDisposed || _webView.IsDisposed || _webView.CoreWebView2 == null) return;
            if (_bridge == null) return;

            if (pressedHwnd == IntPtr.Zero) return;
            if (!IsHandleCreated || !_webView.IsHandleCreated) return;

            IntPtr webViewHandle = _webView.Handle;

            // Walk up to the top-level window the press landed on. GetParent also returns the
            // OWNER of an owned top-level window, which is what carries a press on the native
            // tab-list popup back to the form that owns it. (GetAncestor(GA_PARENT) does NOT
            // do that — don't "modernize" this to it.) Win32 parent/owner chains are acyclic,
            // so this always terminates.
            IntPtr top = IntPtr.Zero;
            for (IntPtr h = pressedHwnd; h != IntPtr.Zero; h = GetParent(h))
            {
                if (h == webViewHandle) return; // inside the page — the DOM already sees it
                top = h;
            }
            if (top == IntPtr.Zero) return;

            // Any of OUR windows counts as chrome, not just the viewer's current parent: while
            // the viewer is popped out into its own window the main window keeps its tab strip
            // on screen, and pressing that strip must still dismiss page overlays. Presses on
            // an unrelated app's window belong to no form here and are ignored.
            if (Control.FromHandle(top) == null) return;

            _bridge.PushEvent(new { @event = "chromePressed" });
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr hWnd);

        /// <summary>
        /// Feeds a horizontal wheel delta into the web app by synthesizing a wheel event on
        /// its document, so the one place that owns swipe logic (createWheelSwipeHandler in
        /// useTabSwipeNavigation.ts) handles accumulation, threshold, and direction.
        /// </summary>
        public void ForwardHorizontalWheel(int delta)
        {
            if (delta == 0) return;
            if (IsDisposed || _webView.IsDisposed || _webView.CoreWebView2 == null) return;
            string dx = delta.ToString(CultureInfo.InvariantCulture);
            string js = "document.dispatchEvent(new WheelEvent('wheel',{deltaX:" + dx +
                        ",deltaY:0,bubbles:true,cancelable:true}));";
            try { _ = _webView.CoreWebView2.ExecuteScriptAsync(js); }
            catch { /* best-effort */ }
        }
    }
}
