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

        bool IMessageFilter.PreFilterMessage(ref Message m)
        {
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
