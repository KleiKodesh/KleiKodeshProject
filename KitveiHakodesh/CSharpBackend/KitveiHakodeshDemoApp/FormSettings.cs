namespace KitveiHakodeshDemoApp
{
    using System;
    using System.Drawing;
    using System.Windows.Forms;
    using Microsoft.VisualBasic;

    public static class FormSettingsHelper
    {
        /// <summary>
        /// Restores the form's size and location. Window state (maximized/normal) is
        /// managed separately via AppSettings.SaveMainWindowMaximized so it survives
        /// abnormal exits. This method only restores geometry (size + position).
        /// </summary>
        public static void LoadFormSettings(Form form, string appName, string formName)
        {
            try
            {
                string hasSettings = Interaction.GetSetting(appName, formName + "FormSettings", $"{form.Name}_Width", "");
                if (string.IsNullOrEmpty(hasSettings)) return;

                int x = int.Parse(Interaction.GetSetting(appName, formName + "FormSettings", $"{form.Name}_Left", form.Left.ToString()));
                int y = int.Parse(Interaction.GetSetting(appName, formName + "FormSettings", $"{form.Name}_Top", form.Top.ToString()));
                int w = int.Parse(Interaction.GetSetting(appName, formName + "FormSettings", $"{form.Name}_Width", form.Width.ToString()));
                int h = int.Parse(Interaction.GetSetting(appName, formName + "FormSettings", $"{form.Name}_Height", form.Height.ToString()));

                // The saved position may be on a monitor that is no longer connected
                // (or the resolution shrank) — restoring it blindly opens the window
                // entirely off-screen. Only restore the position when the title-bar
                // area is still visible on some screen; otherwise keep the saved size
                // and center on the primary screen (CenterScreen already ran for the
                // default size before Load, so re-center manually for the new size).
                form.StartPosition = FormStartPosition.Manual;
                form.Size = new Size(w, h);
                if (IsOnScreen(new Rectangle(x, y, w, h)))
                {
                    // Saved coords are raw screen coords (Form.Bounds) — assign Location
                    // directly. SetDesktopLocation would add the primary working-area
                    // origin, drifting the window when the taskbar is docked left/top.
                    form.Location = new Point(x, y);
                }
                else
                {
                    var area = Screen.PrimaryScreen.WorkingArea;
                    form.Location = new Point(
                        area.X + Math.Max(0, (area.Width - w) / 2),
                        area.Y + Math.Max(0, (area.Height - h) / 2));
                }
            }
            catch
            {
                // Fail silently — app opens with default designer settings.
            }
        }

        /// <summary>
        /// True when enough of the window's title-bar strip intersects a working
        /// area for the user to see and grab it.
        /// </summary>
        private static bool IsOnScreen(Rectangle bounds)
        {
            var titleStrip = new Rectangle(bounds.X, bounds.Y, bounds.Width, 32);
            foreach (var screen in Screen.AllScreens)
            {
                var visible = Rectangle.Intersect(screen.WorkingArea, titleStrip);
                if (visible.Width >= 60 && visible.Height >= 16)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Saves the form's size and location (not window state — that is handled separately).
        /// </summary>
        /// <param name="lastNormalBounds">
        /// Last bounds observed while <see cref="Form.WindowState"/> was <see cref="FormWindowState.Normal"/>,
        /// tracked live by the caller. Used instead of <see cref="Form.RestoreBounds"/> when the form is not
        /// currently Normal: RestoreBounds is WinForms-cached and can be stale (e.g. closing while minimized),
        /// which previously made the saved position drift downward on every minimize/restore cycle.
        /// </param>
        public static void SaveFormSettings(Form form, string appName, string formName, Rectangle? lastNormalBounds = null)
        {
            Rectangle bounds = (form.WindowState == FormWindowState.Normal)
                ? form.Bounds
                : (lastNormalBounds ?? form.RestoreBounds);

            Interaction.SaveSetting(appName, formName + "FormSettings", $"{form.Name}_Left", bounds.Left.ToString());
            Interaction.SaveSetting(appName, formName + "FormSettings", $"{form.Name}_Top", bounds.Top.ToString());
            Interaction.SaveSetting(appName, formName + "FormSettings", $"{form.Name}_Width", bounds.Width.ToString());
            Interaction.SaveSetting(appName, formName + "FormSettings", $"{form.Name}_Height", bounds.Height.ToString());
        }
    }
}
