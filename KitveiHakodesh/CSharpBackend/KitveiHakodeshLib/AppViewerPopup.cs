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
            // Only sized popups are ours; plain _blank links keep WebView2's default.
            var features = e.WindowFeatures;
            if (!features.HasSize) return;

            var deferral = e.GetDeferral();
            Form form = null;
            try
            {
                // The host owns the bounds: the last ones the user left, else the page's
                // requested size, centred on the app's screen.
                string name = e.Name ?? "";
                var (x, y, width, height) = name.Length == 0
                    ? (-1, -1, (int)features.Width, (int)features.Height)
                    : AppSettingsRegistry.GetPopupBounds(name, (int)features.Width, (int)features.Height);

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
                    if (name.Length > 0 && form.WindowState == FormWindowState.Normal)
                        AppSettingsRegistry.SetPopupBounds(name, form.Left, form.Top,
                            form.ClientSize.Width, form.ClientSize.Height);
                };
                form.FormClosed += (s, _) => popupView.Dispose();

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

        // Same as TaskPanePopOut: in the Word process DarkNet ignores SetWindowThemeForms
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

        // VSTO task pane has no host Form to borrow an icon from — same file TaskPanePopOut uses.
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
