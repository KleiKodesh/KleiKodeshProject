namespace KitveiHakodeshDemoApp
{
    using System;
    using System.Drawing;
    using System.Windows.Forms;
    using Microsoft.VisualBasic;

    public static class FormSettingsHelper
    {
        /// <summary>
        /// Restores the form's size, location, and maximized state using a specific App Name.
        /// </summary>
        public static void LoadFormSettings(Form form, string appName, string formName)
        {
            try
            {
                // Use current form values as defaults if registry is empty
                string stateString = Interaction.GetSetting(appName, formName + "FormSettings", $"{form.Name}_State", "Normal");
                int x = int.Parse(Interaction.GetSetting(appName, formName + "FormSettings", $"{form.Name}_Left", form.Left.ToString()));
                int y = int.Parse(Interaction.GetSetting(appName, formName + "FormSettings", $"{form.Name}_Top", form.Top.ToString()));
                int w = int.Parse(Interaction.GetSetting(appName, formName + "FormSettings", $"{form.Name}_Width", form.Width.ToString()));
                int h = int.Parse(Interaction.GetSetting(appName, formName + "FormSettings", $"{form.Name}_Height", form.Height.ToString()));

                // 1. Apply geometry while in Normal state
                form.SetDesktopLocation(x, y);
                form.Size = new Size(w, h);

                // 2. Apply WindowState (Maximized/Normal)
                if (Enum.TryParse(stateString, out FormWindowState savedState))
                {
                    // Safety: Don't start the app minimized
                    form.WindowState = (savedState == FormWindowState.Minimized) ? FormWindowState.Normal : savedState;
                }
            }
            catch
            {
                // Fail silently to allow the app to open with default designer settings
            }
        }

        /// <summary>
        /// Saves the form's size, location, and maximized state using a specific App Name.
        /// </summary>
        public static void SaveFormSettings(Form form, string appName, string formName)
        {
            // Use RestoreBounds if the window is currently Maximized/Minimized 
            // to ensure we save the actual 'floating' size.
            Rectangle bounds = (form.WindowState == FormWindowState.Normal) ? form.Bounds : form.RestoreBounds;

            Interaction.SaveSetting(appName, formName + "FormSettings", $"{form.Name}_State", form.WindowState.ToString());
            Interaction.SaveSetting(appName, formName + "FormSettings", $"{form.Name}_Left", bounds.Left.ToString());
            Interaction.SaveSetting(appName, formName + "FormSettings", $"{form.Name}_Top", bounds.Top.ToString());
            Interaction.SaveSetting(appName, formName + "FormSettings", $"{form.Name}_Width", bounds.Width.ToString());
            Interaction.SaveSetting(appName, formName + "FormSettings", $"{form.Name}_Height", bounds.Height.ToString());
        }
    }
}
