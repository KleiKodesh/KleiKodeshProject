using Dark.Net;
using KitveiHakodesh.Core.Settings;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KitveiHakodeshLib
{
    /// <summary>
    /// window.open popups (e.g. Hameshiv): WebView2's default popup ignores the requested
    /// size, so we host it in our own small Form sized from the features string and owned
    /// by the app's top-level window — it stays above it, minimizes and closes with it.
    /// </summary>
    public partial class AppViewer
    {
        private async void OnNewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            var features = e.WindowFeatures;
            // A plain new-window link inside a Torah website: a whitelisted address opens as an app
            // tab, anything else in the system browser - never a bare browser window over the app.
            if (!features.HasSize && _webSites != null && _webSites.IsWebsiteAddress(e.OriginalSourceFrameInfo?.Source))
            {
                e.Handled = true;
                if (_webSites.IsWebsiteAddress(e.Uri))
                    _bridge?.PushEvent(new { @event = "siteOpenInNewTab", url = e.Uri });
                else if (Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri external) &&
                         (external.Scheme == Uri.UriSchemeHttp || external.Scheme == Uri.UriSchemeHttps))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = external.AbsoluteUri, UseShellExecute = true });
                return;
            }
            // Only sized popups are ours; plain _blank links keep WebView2's default.
            if (!features.HasSize) return;

            var deferral = e.GetDeferral();
            Form form = null;
            try
            {
                // Where the window opens: the last bounds the user left, else the page's
                // requested size, centred on the app's screen. A page that keeps those bounds
                // itself (the app's detached windows - its saved settings, so a settings reset
                // clears them) names the window "name@x,y,w,h", or "name@" for none saved yet;
                // the host then reports the new bounds back (popupBounds) instead of storing
                // them. Any other named popup keeps its bounds in the registry.
                string name = e.Name ?? "";
                bool pageKeepsBounds = name.IndexOf('@') >= 0;
                var (x, y, width, height) = (-1, -1, (int)features.Width, (int)features.Height);
                if (pageKeepsBounds)
                {
                    string pageBounds = name.Substring(name.IndexOf('@') + 1);
                    name = name.Substring(0, name.IndexOf('@'));
                    var parts = pageBounds.Split(',');
                    if (parts.Length == 4 && int.TryParse(parts[0], out int sx) && int.TryParse(parts[1], out int sy)
                        && int.TryParse(parts[2], out int sw) && int.TryParse(parts[3], out int sh) && sw > 0 && sh > 0)
                        (x, y, width, height) = (sx, sy, sw, sh);
                }
                else if (name.Length > 0)
                    (x, y, width, height) = AppSettingsRegistry.GetPopupBounds(name, (int)features.Width, (int)features.Height);

                form = new Form
                {
                    Text = "",
                    ShowInTaskbar = false,
                    StartPosition = FormStartPosition.Manual,
                    ClientSize = new Size(width, height),
                    RightToLeft = RightToLeft.Yes,
                    RightToLeftLayout = true,
                    Icon = FindForm()?.Icon ?? AddinIcon(),
                };
                // The page's left/top are CSS pixels, not ours — only the saved spot counts.
                var saved = new Rectangle(new Point(x, y), form.Size);
                form.Location = x != -1 && y != -1 && IsOnScreen(saved)
                    ? saved.Location
                    : CenterOnScreen(form.Size);
                ApplyPopupTheme(form);

                var popupView = new WebView2 { Dock = DockStyle.Fill };
                form.Controls.Add(popupView);

                var owner = GetAncestor(Handle, GA_ROOT);
                if (SetPopupOwner != null)
                {
                    SetPopupOwner(form.Handle); // VSTO: owned by the Word window
                    form.Show();
                }
                else if (owner != IntPtr.Zero) form.Show(new OwnerWindow(owner));
                else form.Show();

                await popupView.EnsureCoreWebView2Async(_webView.CoreWebView2.Environment);
                popupView.CoreWebView2.DocumentTitleChanged += (s, _) =>
                    form.Text = popupView.CoreWebView2.DocumentTitle;
                popupView.CoreWebView2.WindowCloseRequested += (s, _) => form.Close();
                form.FormClosing += (s, _) =>
                {
                    if (name.Length == 0 || form.WindowState != FormWindowState.Normal) return;
                    if (pageKeepsBounds)
                        _bridge?.PushEvent(new
                        {
                            @event = "popupBounds", name,
                            x = form.Left, y = form.Top, width = form.ClientSize.Width, height = form.ClientSize.Height,
                        });
                    else
                        AppSettingsRegistry.SetPopupBounds(name, form.Left, form.Top,
                            form.ClientSize.Width, form.ClientSize.Height);
                };
                form.FormClosed += (s, _) => popupView.Dispose();
                if (name.Length > 0)
                {
                    _popupFormsByName[name] = form;
                    form.FormClosed += (s, _) => { if (_popupFormsByName.TryGetValue(name, out var f) && f == form) _popupFormsByName.Remove(name); };
                }

                e.NewWindow = popupView.CoreWebView2;
                e.Handled = true;
            }
            catch
            {
                // Fall back to WebView2's default popup — don't leave an empty window behind.
                form?.Dispose();
            }
            finally
            {
                deferral.Complete();
            }
        }

        // Named popups, so the page can size one to its content (resizePopup).
        private readonly System.Collections.Generic.Dictionary<string, Form> _popupFormsByName =
            new System.Collections.Generic.Dictionary<string, Form>();

        /// <summary>
        /// The page asks a named popup to take this client size (CSS pixels): a window
        /// sized to its content. Kept on the screen; the top-left corner stays put.
        /// </summary>
        private void HandleResizePopup(System.Text.Json.JsonElement root, string id)
        {
            _bridge.Reply(id, new { });
            string name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (name == null || !_popupFormsByName.TryGetValue(name, out var form) || form.IsDisposed) return;
            if (form.WindowState != FormWindowState.Normal) return;
            double scale = form.DeviceDpi / 96.0;
            int width = (int)Math.Ceiling(root.GetProperty("width").GetDouble() * scale);
            int height = (int)Math.Ceiling(root.GetProperty("height").GetDouble() * scale);
            var area = Screen.FromControl(form).WorkingArea;
            var chrome = form.Size - form.ClientSize;
            width = Math.Min(width, area.Width - chrome.Width);
            height = Math.Min(height, area.Height - chrome.Height);
            form.ClientSize = new Size(width, height);
            // Grown past the screen's edge: pull it back in.
            int left = Math.Min(form.Left, area.Right - form.Width);
            int top = Math.Min(form.Top, area.Bottom - form.Height);
            form.Location = new Point(Math.Max(area.Left, left), Math.Max(area.Top, top));
        }

        // Reachable if a reasonable part of its title bar is on some screen.
        private static bool IsOnScreen(Rectangle bounds)
        {
            var titleBar = new Rectangle(bounds.X, bounds.Y, bounds.Width, 32);
            foreach (var screen in Screen.AllScreens)
                if (screen.WorkingArea.IntersectsWith(titleBar))
                    return true;
            return false;
        }

        // Open popups, so a live dark/light toggle (HandleSetTheme) repaints their title bars.
        private readonly System.Collections.Generic.List<Form> _popupForms = new System.Collections.Generic.List<Form>();

        private void ApplyPopupTheme(Form form)
        {
            SetPopupTheme(form, AppSettingsRegistry.GetDarkMode());
            _popupForms.Add(form);
            form.FormClosed += (_, __) => _popupForms.Remove(form);
        }

        private void ApplyThemeToPopups(bool isDark)
        {
            foreach (var form in _popupForms) SetPopupTheme(form, isDark);
        }

        // In the Word process DarkNet ignores SetWindowThemeForms
        // (Light) unless SetCurrentProcessTheme(Auto) runs right before it.
        private void SetPopupTheme(Form form, bool isDark)
        {
            try
            {
                if (SetPopupOwner != null) DarkNet.Instance.SetCurrentProcessTheme(Theme.Auto); // VSTO only
                DarkNet.Instance.SetWindowThemeForms(form, isDark ? Theme.Dark : Theme.Light);
            }
            catch { /* best-effort */ }
        }

        // VSTO task pane has no host Form to borrow an icon from — the add-in's own icon file.
        private static Icon _addinIcon;

        private static Icon AddinIcon()
        {
            if (_addinIcon != null) return _addinIcon;
            var path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "KleiKodesh_Main.ico");
            return _addinIcon = System.IO.File.Exists(path) ? new Icon(path) : null;
        }

        private Point CenterOnScreen(Size size)
        {
            var area = Screen.FromControl(this).WorkingArea;
            return new Point(area.Left + (area.Width - size.Width) / 2,
                             area.Top + (area.Height - size.Height) / 2);
        }

        private sealed class OwnerWindow : IWin32Window
        {
            public OwnerWindow(IntPtr handle) { Handle = handle; }
            public IntPtr Handle { get; }
        }

        private const uint GA_ROOT = 2;

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    }
}
