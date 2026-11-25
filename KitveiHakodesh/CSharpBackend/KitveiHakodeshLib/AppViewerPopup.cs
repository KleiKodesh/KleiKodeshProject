using KitveiHakodeshLib.Bridge;
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
    /// <summary>
    /// The page opened an extra app window of itself (the VS Code model: one page draws many
    /// windows). The host supplies the window and the control its page fills.
    /// </summary>
    public sealed class AppWindowRequestedEventArgs : EventArgs
    {
        public AppWindowRequestedEventArgs(string windowKey) { WindowKey = windowKey; }

        /// <summary>The page's key for the window (the tabs it shows carry it).</summary>
        public string WindowKey { get; }

        /// <summary>Set by the host: the window. Left null, the page gets a plain pop-out.</summary>
        public Form Window { get; set; }

        /// <summary>Set by the host: the control the window's page fills.</summary>
        public Control ContentHost { get; set; }

        /// <summary>Set by the host: runs just before the page closes the window itself.</summary>
        public Action ClosingByPage { get; set; }
    }

    /// <summary>A tool window (widget or commentary pop-out) of the page opened.</summary>
    public sealed class ToolWindowOpenedEventArgs : EventArgs
    {
        public ToolWindowOpenedEventArgs(Form window) { Window = window; }

        public Form Window { get; }
    }

    public partial class AppViewer
    {
        /// <summary>
        /// Raised when the page opens an extra app window of itself (window name
        /// "appwindow-&lt;key&gt;", AppWindow.vue). A host with tab strips makes it a full app
        /// window; without a handler it opens as a plain pop-out.
        /// </summary>
        public event EventHandler<AppWindowRequestedEventArgs> AppWindowRequested;

        private const string AppWindowPrefix = "appwindow-";

        private async void OnNewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            // A library-app link opened as a new window (target=_blank): open the book here.
            HostLink bookLink = HostLink.TryParse(e.Uri);
            if (bookLink != null)
            {
                e.Handled = true;
                OpenBookFromHost(bookLink);
                return;
            }
            var features = e.WindowFeatures;
            // A plain new-window link inside a Torah website: a whitelisted address opens as an app
            // tab, anything else in the system browser - never a bare browser window over the app.
            if (!features.HasSize && _webSites != null && _webSites.IsWebsiteAddress(e.OriginalSourceFrameInfo?.Source))
            {
                e.Handled = true;
                if (_webSites.IsWebsiteAddress(e.Uri))
                    _bridge?.PushEvent(new { @event = "siteOpenInNewTab", url = e.Uri, frame = e.OriginalSourceFrameInfo?.Name ?? "" });
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

                // An extra app window of this page: the host makes the window (with its strip).
                Control contentHost = null;
                bool isAppWindow = false;
                Action closingByPage = null;
                if (name.StartsWith(AppWindowPrefix, StringComparison.Ordinal) && AppWindowRequested != null)
                {
                    var request = new AppWindowRequestedEventArgs(name.Substring(AppWindowPrefix.Length));
                    AppWindowRequested(this, request);
                    if (request.Window != null && request.ContentHost != null)
                    {
                        form = request.Window;
                        contentHost = request.ContentHost;
                        isAppWindow = true;
                        closingByPage = request.ClosingByPage;
                        form.StartPosition = FormStartPosition.Manual;
                        if (pageKeepsBounds && x != -1) form.ClientSize = new Size(width, height);
                        else form.Size = OwnNormalBounds()?.Size ?? form.Size;
                    }
                }
                if (form == null)
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
                    : isAppWindow ? CascadeFrom(form.Size) : CenterOnScreen(form.Size);
                if (!isAppWindow) ApplyPopupTheme(form);

                var popupView = new WebView2 { Dock = DockStyle.Fill };
                (contentHost ?? form).Controls.Add(popupView);

                var owner = GetAncestor(Handle, GA_ROOT);
                if (isAppWindow) form.Show(); // a window of its own, like the main one
                else if (SetPopupOwner != null)
                {
                    SetPopupOwner(form.Handle); // VSTO: owned by the Word window
                    form.Show();
                }
                else if (HostWindowHidden)
                {
                    // Its owner would be hidden: a window of its own, on the taskbar.
                    form.ShowInTaskbar = true;
                    form.Show();
                }
                else if (owner != IntPtr.Zero) form.Show(new OwnerWindow(owner));
                else form.Show();

                await popupView.EnsureCoreWebView2Async(_webView.CoreWebView2.Environment);
                if (isAppWindow) ConfigureAppWindowView(popupView.CoreWebView2, form);
                popupView.CoreWebView2.DocumentTitleChanged += (s, _) =>
                    form.Text = popupView.CoreWebView2.DocumentTitle;
                popupView.CoreWebView2.WindowCloseRequested += (s, _) =>
                {
                    closingByPage?.Invoke();
                    form.Close();
                };
                popupView.CoreWebView2.LaunchingExternalUriScheme += OnLaunchingExternalUriScheme;
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
                // A widget's or a commentary panel's window docks like the app's other windows
                // (the host's screen docking); let go on a tab strip it becomes a tab there.
                bool isWidget = name.StartsWith(WidgetWindowPrefix, StringComparison.Ordinal);
                bool isCommentary = name.StartsWith(CommentaryWindowPrefix, StringComparison.Ordinal);
                if (isWidget || isCommentary)
                {
                    Form toolWindow = form;
                    _toolWindowWidgets[toolWindow] = isWidget ? name.Substring(WidgetWindowPrefix.Length) : null;
                    toolWindow.FormClosed += (s, _) => _toolWindowWidgets.Remove(toolWindow);
                    ToolWindowOpened?.Invoke(this, new ToolWindowOpenedEventArgs(toolWindow));
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

        /// <summary>
        /// An extra app window's view gets what the main view has for the frames inside it:
        /// the app's own folder, files opened in tabs, the Torah websites (frame headers,
        /// navigation, activity), the frame scripts, and no browser context menu.
        /// </summary>
        private void ConfigureAppWindowView(CoreWebView2 core, Form window)
        {
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.SetVirtualHostNameToFolderMapping("kitveihakodesh-app", AppDir, CoreWebView2HostResourceAccessKind.Allow);
            MapKiwixIfInstalled(core);
            _ = core.AddScriptToExecuteOnDocumentCreatedAsync(JsBridge.IframeScrollScript + "\n" + JsBridge.OtzariaAddinBridgeStubScript);
            _ = core.AddScriptToExecuteOnDocumentCreatedAsync(SiteFrameActivityScript);
            KitveiHakodeshLib.WebSites.WebSitesFrameHeaderStripper.Attach(core, AppDir);
            core.FrameCreated += OnAppChildFrameCreated;
            // New-window links inside a site there: a tab, or the system browser (as in the main view).
            core.NewWindowRequested += OnNewWindowRequested;
            _localFile?.AddCore(core);
            window.FormClosed += (s, e) => _localFile?.RemoveCore(core);
        }

        // The page names a widget's window "widget-<id>" (WidgetWindow.vue) and a commentary
        // panel's "commentary-<n>-<slot>" (BookViewPage.vue).
        private const string WidgetWindowPrefix = "widget-";
        private const string CommentaryWindowPrefix = "commentary-";

        /// <summary>
        /// Raised when a tool window (a widget's or a commentary panel's pop-out) opens: a host
        /// with screen docking lets it dock and tile like its other windows.
        /// </summary>
        public event EventHandler<ToolWindowOpenedEventArgs> ToolWindowOpened;

        // The widget each open widget window shows (null: a commentary pop-out).
        private readonly System.Collections.Generic.Dictionary<Form, string> _toolWindowWidgets = new System.Collections.Generic.Dictionary<Form, string>();

        /// <summary>
        /// A tool window of this viewer's page let go on a tab strip (or with no place left on its
        /// screen): a widget opens as a tab of <paramref name="target"/>'s page and its window closes;
        /// a commentary pop-out closes, which docks the panel back into its book - only into this
        /// viewer's own window, where its book is. False when the window stays.
        /// </summary>
        public bool MergeToolWindowIntoTabs(Form toolWindow, AppViewer target)
        {
            if (toolWindow == null || toolWindow.IsDisposed || target == null) return false;
            if (!_toolWindowWidgets.TryGetValue(toolWindow, out string widgetId)) return false;
            if (widgetId != null) target.OpenWidgetAsTab(widgetId);
            else if (target != this) return false;
            toolWindow.Close();
            return true;
        }

        /// <summary>Opens a widget (by its id) as a tab of this viewer's page: a widget window was dropped on its strip.</summary>
        public void OpenWidgetAsTab(string widgetId)
            => _bridge?.PushEvent(new { @event = "openWidgetAsTab", widgetId });

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

        // A new app window with nothing saved: a little down and across from this one.
        private Point CascadeFrom(Size size)
        {
            Rectangle? mine = OwnNormalBounds();
            if (mine == null) return CenterOnScreen(size);
            var area = Screen.FromRectangle(mine.Value).WorkingArea;
            var place = new Point(mine.Value.Left + 32, mine.Value.Top + 32);
            if (place.X + size.Width > area.Right || place.Y + size.Height > area.Bottom) return CenterOnScreen(size);
            return place;
        }

        // This viewer's window as it is when neither minimized nor maximized: a minimized
        // window sits off screen at a caption's size, which a new window must not copy.
        private Rectangle? OwnNormalBounds()
        {
            Form mine = FindForm();
            if (mine == null) return null;
            return mine.WindowState == FormWindowState.Normal ? mine.Bounds : mine.RestoreBounds;
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
