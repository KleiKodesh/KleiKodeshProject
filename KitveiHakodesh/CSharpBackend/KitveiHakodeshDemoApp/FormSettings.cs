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

                // Switch to Manual so CenterScreen does not override the saved position.
                form.StartPosition = FormStartPosition.Manual;
                form.SetDesktopLocation(x, y);
                form.Size = new Size(w, h);
            }
            catch
            {
                // Fail silently — app opens with default designer settings.
            }
        }

        /// <summary>
        /// Saves the form's size and location (not window state — that is handled separately).
        /// </summary>
        public static void SaveFormSettings(Form form, string appName, string formName)
        {
            // Always save the restore bounds so we have the normal size even when maximized.
            Rectangle bounds = (form.WindowState == FormWindowState.Normal) ? form.Bounds : form.RestoreBounds;

            Interaction.SaveSetting(appName, formName + "FormSettings", $"{form.Name}_Left", bounds.Left.ToString());
            Interaction.SaveSetting(appName, formName + "FormSettings", $"{form.Name}_Top", bounds.Top.ToString());
            Interaction.SaveSetting(appName, formName + "FormSettings", $"{form.Name}_Width", bounds.Width.ToString());
            Interaction.SaveSetting(appName, formName + "FormSettings", $"{form.Name}_Height", bounds.Height.ToString());
        }
    }
}
