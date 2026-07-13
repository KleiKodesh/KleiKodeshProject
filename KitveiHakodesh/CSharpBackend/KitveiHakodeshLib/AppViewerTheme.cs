using Dark.Net;
using KitveiHakodeshLib.Settings;
using System;
using System.Windows.Forms;

namespace KitveiHakodeshLib
{
    /// <summary>
    /// Theme details sent by Vue via the 'setTheme' bridge action.
    /// ChromeColor is the Vue theme's title-bar background (hex), used to derive
    /// the native chrome-tab-strip palette; null when Vue sent no color.
    /// </summary>
    public class ChromeThemeChangedEventArgs : EventArgs
    {
        public ChromeThemeChangedEventArgs(bool isDark, string chromeColor, string accentColor)
        {
            IsDark = isDark;
            ChromeColor = chromeColor;
            AccentColor = accentColor;
        }

        public bool IsDark { get; }
        public string ChromeColor { get; }

        /// <summary>The Vue theme's accent color (hex), for the tab-list active indicator; null when not sent.</summary>
        public string AccentColor { get; }
    }

    // Title-bar theme (DarkNet) wiring for AppViewer.
    // Owns: OnParentChanged, OnHostFormHandleCreated, ApplyTitleBarTheme,
    //       ApplyTitleBarThemeToForm, HandleSetTheme, _extraThemeForm.
    public partial class AppViewer
    {
        /// <summary>Raised when Vue reports a theme change, carrying the chrome-strip color.</summary>
        public event EventHandler<ChromeThemeChangedEventArgs> ChromeThemeChanged;

        // Extra form to keep in sync with theme toggles (used by VSTO popout).
        private Form _extraThemeForm;

        private void OnParentChanged(object sender, EventArgs e)
        {
            Form hostForm = FindForm();
            if (hostForm == null) return;
            bool isDark = AppSettings.LoadDarkMode();
            ApplySplashTheme(isDark);
            // Subscribe to the host Form's HandleCreated to apply the per-window theme
            // before the form is shown — the required moment for SetWindowThemeForms.
            // OnHandleCreated on the UserControl fires too late (form already visible).
            hostForm.HandleCreated -= OnHostFormHandleCreated;
            hostForm.HandleCreated += OnHostFormHandleCreated;
            // If the form already has a handle (re-parenting into an already-shown window),
            // HandleCreated won't fire again — apply directly.
            if (hostForm.IsHandleCreated)
                OnHostFormHandleCreated(hostForm, EventArgs.Empty);
        }

        private void OnHostFormHandleCreated(object sender, EventArgs e)
        {
            var hostForm = (Form)sender;
            hostForm.HandleCreated -= OnHostFormHandleCreated;
            bool isDark = AppSettings.LoadDarkMode();
            try
            {
                // Form has an HWND but WS_VISIBLE is not yet set — the correct moment
                // for DarkNet's initial registration of the window handle.
                DarkNet.Instance.SetWindowThemeForms(hostForm, isDark ? Theme.Dark : Theme.Light);
            }
            catch { /* best-effort — VSTO or other hosts may not support this */ }
        }

        private void ApplyTitleBarTheme(bool isDark)
        {
            try
            {
                Form hostForm = FindForm();
                if (hostForm == null) return;
                // SetWindowThemeForms is safe to call multiple times after the window
                // is shown — this is the documented live-toggle API per the DarkNet README.
                DarkNet.Instance.SetWindowThemeForms(hostForm, isDark ? Theme.Dark : Theme.Light);
            }
            catch { /* best-effort — no-op if DarkNet can't apply the theme */ }
        }

        /// <summary>
        /// Applies the current persisted dark mode preference to an explicitly provided Form.
        /// Used by VSTO's TaskPanePopOut, which moves the WebView2 child control directly into
        /// a new floating Form — bypassing the normal ParentChanged path that only fires when
        /// AppViewer itself is re-parented.
        /// Call this after the popout Form is created but before or after Show().
        /// </summary>
        public void ApplyTitleBarThemeToForm(Form form)
        {
            if (form == null) return;
            bool isDark = AppSettings.LoadDarkMode();
            try { DarkNet.Instance.SetWindowThemeForms(form, isDark ? Theme.Dark : Theme.Light); }
            catch { /* best-effort */ }
            // Track this form so live toggles from HandleSetTheme also update it.
            _extraThemeForm = form;
            form.FormClosed += (_, __) => { if (_extraThemeForm == form) _extraThemeForm = null; };
        }

        /// <summary>
        /// Handles the "setTheme" bridge action sent by Vue whenever the user toggles
        /// dark/light mode. Persists the preference and updates the host Form's title bar.
        /// </summary>
        private void HandleSetTheme(System.Text.Json.JsonElement root, string id)
        {
            _bridge.Reply(id, new { });

            bool isDark = root.TryGetProperty("isDark", out var v) && v.GetBoolean();
            string chromeColor = root.TryGetProperty("chromeColor", out var c) &&
                                 c.ValueKind == System.Text.Json.JsonValueKind.String
                ? c.GetString()
                : null;
            string accentColor = root.TryGetProperty("accentColor", out var ac) &&
                                 ac.ValueKind == System.Text.Json.JsonValueKind.String
                ? ac.GetString()
                : null;

            AppSettings.SaveDarkMode(isDark);
            if (chromeColor != null) AppSettings.SaveChromeColor(chromeColor);
            if (accentColor != null) AppSettings.SaveAccentColor(accentColor);

            if (InvokeRequired)
                Invoke(new Action(() => ApplyTitleBarTheme(isDark)));
            else
                ApplyTitleBarTheme(isDark);

            // Also update the VSTO popout window if one is active.
            // That form hosts the WebView2 directly and is not reachable via FindForm().
            if (_extraThemeForm != null && !_extraThemeForm.IsDisposed)
            {
                try { DarkNet.Instance.SetWindowThemeForms(_extraThemeForm, isDark ? Theme.Dark : Theme.Light); }
                catch { }
            }

            ApplySplashTheme(isDark);

            // Let a native chrome-tabs host (ChromeTabsMirror) re-derive its strip palette.
            ChromeThemeChanged?.Invoke(this, new ChromeThemeChangedEventArgs(isDark, chromeColor, accentColor));
        }
    }
}
