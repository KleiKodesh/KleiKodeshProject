using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace KitveiHakodeshLib
{
    // Splash screen show/hide logic for AppViewer.
    // Owns: _splash field, _InitSplash, _HideSplash, _SyncSplashBackColor, ApplySplashTheme.
    public partial class AppViewer
    {
        private SplashOverlay _splash;

        private static readonly Color _darkBg  = System.Drawing.Color.FromArgb(0x1a, 0x1a, 0x1a);
        private static readonly Color _lightBg = SystemColors.Control;

        private void _InitSplash()
        {
            System.Drawing.Image logo = null;
            using (var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("KitveiHakodesh.png"))
            {
                if (stream != null)
                    logo = System.Drawing.Image.FromStream(stream);
            }

            _splash = new SplashOverlay(logo) { Dock = DockStyle.Fill };
            Controls.Add(_splash);
            _SyncSplashBackColor();
            _splash.BringToFront();
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
            if (InvokeRequired) { Invoke(new System.Action(_HideSplash)); return; }
            _splash.FadeOut();
            _splash = null;
        }
    }
}
