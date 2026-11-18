using KiwixLib;
using System.Windows.Forms;

namespace KiwixDemoApp
{
    public class MainForm : Form
    {
        // "--theme <name>": the Office themes as OfficeThemeWatcher applies them in Word
        // (background, foreground) - same names as DemoShared\PaneSnapshot.cs.
        static readonly (string Name, string Back, string Fore)[] Themes =
        {
            ("office-white",      "#FFFFFF", "#262626"),
            ("office-light-gray", "#F3F3F3", "#262626"),
            ("office-dark-gray",  "#666666", "#E6E6E6"),
            ("office-black",      "#262626", "#D4D4D4"),
        };

        public MainForm(string theme = null)
        {
            Text = "Kiwix";
            // Same size as the other pane demos: a Word task pane (400 x 452 at 96 DPI, measured
            // live in maximized Word at 150% = 600 x 678 px). Declared at 96 DPI; WinForms scales
            // it to the monitor (the demo is per-monitor aware via PaneDemo.manifest).
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            float scale;
            using (var g = CreateGraphics()) scale = g.DpiX / 96f;
            ClientSize = new System.Drawing.Size((int)(400 * scale), (int)(452 * scale));
            var viewer = new KiwixWebview { Dock = DockStyle.Fill };
            foreach (var t in Themes)
            {
                if (!string.Equals(t.Name, theme, System.StringComparison.OrdinalIgnoreCase)) continue;
                viewer.BackColor = System.Drawing.ColorTranslator.FromHtml(t.Back);
                viewer.ForeColor = System.Drawing.ColorTranslator.FromHtml(t.Fore);
            }
            Controls.Add(viewer);
        }
    }
}