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
        // splash. Under LocalAppData, not the exe's KitveiHakodesh\ folder: the dev build
        // mirrors the Vue dist over that folder and deletes anything extra.
        private string SnapshotPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KleiKodesh", _webCacheFolder + "-snapshot.png");

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

        internal void _HideSplash()
        {
            if (_splash == null) return;
            if (InvokeRequired) { Invoke(new Action(_HideSplash)); return; }
            _splash.FadeOut();
            _splash = null;
        }
    }
}
