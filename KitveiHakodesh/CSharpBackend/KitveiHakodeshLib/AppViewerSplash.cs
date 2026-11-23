using Microsoft.Web.WebView2.Core;
using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KitveiHakodeshLib
{
    // Splash screen show/hide logic for AppViewer.
    // Owns: _splash field, _InitSplash, _HideSplash, _SyncSplashBackColor, ApplySplashTheme,
    // and the snapshot splash (SaveSnapshotAsync / _TryInitSnapshotSplash).
    public partial class AppViewer
    {
        private SplashOverlay _splash;

        private static readonly Color _darkBg  = Color.FromArgb(0x1a, 0x1a, 0x1a);
        private static readonly Color _lightBg = SystemColors.Control;

        private readonly bool _snapshotSplash;

        // The page as it looked when the window last closed, shown in place of the logo
        // splash. In the app root's page-snapshots\ folder (the app never writes outside its
        // folder), not the exe's KitveiHakodesh\ folder: the dev build mirrors the Vue dist over
        // that folder and deletes anything extra. The hosted test rig's profile folder still wins.
        private string SnapshotPath => Path.Combine(
            HostTestMode.ProfileDir ?? KitveiHakodesh.Core.Common.AppRootFolder.Subfolder(
                KitveiHakodesh.Core.Common.AppRootFolder.PageSnapshots),
            _webCacheFolder + "-snapshot.png");

        private void _InitSplash()
        {
            if (_snapshotSplash && _TryInitSnapshotSplash()) return;

            Image logo = null;
            using (var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("KitveiHakodesh.png"))
            {
                if (stream != null)
                    logo = Image.FromStream(stream);
            }

            _splash = new SplashOverlay(logo, GetInstalledVersionText()) { Dock = DockStyle.Fill };
            Controls.Add(_splash);
            _SyncSplashBackColor();
            _splash.BringToFront();
        }

        private bool _TryInitSnapshotSplash()
        {
            Image snapshot;
            try
            {
                if (!File.Exists(SnapshotPath)) return false;
                // From bytes, not the path: Image.FromFile keeps the file locked, and the
                // next close overwrites it.
                snapshot = Image.FromStream(new MemoryStream(File.ReadAllBytes(SnapshotPath)));
            }
            catch { return false; }

            _splash = new SplashOverlay(snapshot, isSnapshot: true) { Dock = DockStyle.Fill };
            Controls.Add(_splash);
            _SyncSplashBackColor();
            _splash.BringToFront();
            return true;
        }

        // True for a window opened for a tab dragged out of another window: its splash is
        // that tab's picture, and it stays up until the page has drawn the tab.
        private bool _splashIsMovedTabPicture;

        /// <summary>
        /// A picture of what this page shows now, as it is drawn (the tab being dragged out
        /// of the window). Null when the page is not up or the capture took too long.
        /// </summary>
        public async Task<Image> CapturePagePictureAsync()
        {
            if (_webView.CoreWebView2 == null || _webView.Width <= 0) return null;
            try
            {
                var stream = new MemoryStream();
                Task capture = _webView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
                if (await Task.WhenAny(capture, Task.Delay(1500)) != capture) return null;
                await capture;
                stream.Position = 0;
                // The stream stays open: GDI+ reads from it for as long as the image lives.
                return Image.FromStream(stream);
            }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// For a window opened for a tab dragged out of another window: shows that tab's
        /// picture in place of the loading splash, from the first frame until this page has
        /// drawn the tab itself - so the reader keeps looking at the tab they are dragging.
        /// Call right after construction, before the window shows.
        /// </summary>
        public async void ShowMovedTabPictureUntilDrawn(Task<Image> picture)
        {
            if (picture == null || _splash == null) return;
            _splashIsMovedTabPicture = true;
            Controls.Remove(_splash);
            _splash.Dispose();
            var overlay = new SplashOverlay(null, isSnapshot: true) { Dock = DockStyle.Fill };
            _splash = overlay;
            Controls.Add(overlay);
            _SyncSplashBackColor();
            overlay.BringToFront();

            Image image = null;
            try { image = await picture; } catch (Exception) { }
            // Still the same splash, still up: the page may have drawn already.
            if (image != null && _splash == overlay && !overlay.IsDisposed) overlay.SetSnapshot(image);
        }

        /// <summary>
        /// Captures the current page for the next startup's snapshot splash. Written to a
        /// temp file first so a failed capture never leaves a truncated PNG behind.
        /// </summary>
        internal async Task SaveSnapshotAsync()
        {
            if (!_snapshotSplash || _webView.CoreWebView2 == null || _webView.Width <= 0) return;
            string temp = SnapshotPath + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SnapshotPath));
                using (var stream = File.Create(temp))
                {
                    // Bounded: this runs inside the held close (CloseAfterPersistenceFlush), and a
                    // hung renderer must not keep the window open. A late capture is just dropped.
                    Task capture = _webView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
                    if (await Task.WhenAny(capture, Task.Delay(1500)) != capture) throw new TimeoutException();
                    await capture;
                }
                if (File.Exists(SnapshotPath)) File.Delete(SnapshotPath);
                File.Move(temp, SnapshotPath);
            }
            catch (Exception)
            {
                try { File.Delete(temp); } catch { }
            }
        }

        /// <summary>
        /// Installed version from the registry (written by the installer's
        /// SaveVersion) for the splash's bottom version line. Null on dev/portable
        /// runs — the splash then simply shows no version.
        /// </summary>
        private static string GetInstalledVersionText()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\KleiKodesh"))
                    return key?.GetValue("Version")?.ToString();
            }
            catch { return null; }
        }

        /// <summary>
        /// Sets the splash background to match the current theme.
        /// Light: standard window background. Dark: near-black matching the Vue dark UI.
        /// </summary>
        internal void ApplySplashTheme(bool isDark)
        {
            // Set the color directly on the splash rather than going through BackColor,
            // which would fire BackColorChanged and potentially re-trigger handle logic.
            if (_splash != null)
                _splash.BackColor = isDark ? _darkBg : _lightBg;
        }

        private void _SyncSplashBackColor()
        {
            if (_splash == null) return;
            _splash.BackColor = BackColor;
        }

        /// <summary>
        /// The loading splash is gone and the page shows. A host that draws around the viewer (e.g. the Word side
        /// pane's title) redraws then: removing the splash moves focus into the page, and Office repaints its pane.
        /// </summary>
        public event EventHandler SplashHidden;
        /// <summary>The loading splash is still up (<see cref="SplashHidden"/> not raised yet).</summary>
        public bool IsSplashShowing => _splash != null;

        internal void _HideSplash()
        {
            if (_splash == null) return;
            if (InvokeRequired) { Invoke(new Action(_HideSplash)); return; }
            _splash.FadeOut();
            _splash = null;
            SplashHidden?.Invoke(this, EventArgs.Empty);
        }
    }
}
