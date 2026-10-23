using Microsoft.VisualBasic;
using System.Globalization;

namespace DocDesign.Helpers
{
    /// <summary>
    /// Pane settings persisted between sessions under
    /// HKCU\Software\VB and VBA Program Settings\KleiKodesh\DocDesign,
    /// the same subtree the rest of the add-in uses - the NSIS uninstaller and
    /// the installer's full-clean option both delete it whole.
    /// </summary>
    internal static class DocDesignSettings
    {
        const string AppName = "KleiKodesh";
        const string Section = "DocDesign";

        public static int GetInt(string key, int defaultValue)
        {
            try
            {
                return int.TryParse(Interaction.GetSetting(AppName, Section, key, ""), out int value) ? value : defaultValue;
            }
            catch { return defaultValue; }
        }

        public static float GetFloat(string key, float defaultValue)
        {
            try
            {
                return float.TryParse(Interaction.GetSetting(AppName, Section, key, ""),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : defaultValue;
            }
            catch { return defaultValue; }
        }

        public static void Save(string key, int value) => Save(key, value.ToString(CultureInfo.InvariantCulture));
        public static void Save(string key, float value) => Save(key, value.ToString(CultureInfo.InvariantCulture));

        static void Save(string key, string value)
        {
            try { Interaction.SaveSetting(AppName, Section, key, value); }
            catch { } // a settings write must never break the pane
        }
    }
}
