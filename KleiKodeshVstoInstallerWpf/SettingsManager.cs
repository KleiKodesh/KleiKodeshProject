using Microsoft.VisualBasic;
using Microsoft.Win32;

namespace KleiKodesh.Helpers
{
    public static class SettingsManager
    {
        const string AppName = "KleiKodesh";

        public static string Get(
            string section,
            string key,
            string defaultValue)
        {
            return Interaction.GetSetting(
                AppName,
                section,
                key,
                defaultValue);
        }

        public static void Save(
            string section,
            string key,
            object value)
        {
            Interaction.SaveSetting(
                AppName,
                section,
                key,
                value.ToString());
        }

        public static bool GetBool(
           string section,
           string key,
           bool defaultValue)
        {
            var str = Get(section, key, defaultValue.ToString());
            bool.TryParse(str, out defaultValue);
            return defaultValue;
        }

        public static int GetInt(
            string section,
            string key,
            int defaultValue)
        {
            var str = Get(section, key, defaultValue.ToString());
            int.TryParse(str, out defaultValue);
            return defaultValue;
        }

        public static TEnum GetEnum<TEnum>(
            string section,
            string key,
            TEnum defaultValue)
            where TEnum : struct
        {
            var str = Get(section, key, defaultValue.ToString());
            return Enum.TryParse(str, out TEnum value)
                ? value
                : defaultValue;
        }

        /// <summary>
        /// Deletes ALL saved settings for this application.
        /// </summary>
        public static void ClearAll()
        {
            using (var baseKey = Registry.CurrentUser.OpenSubKey(
                @"Software\VB and VBA Program Settings",
                writable: true))
            {
                if (baseKey?.OpenSubKey(AppName) != null)
                {
                    baseKey.DeleteSubKeyTree(AppName);
                }
            }
        }
    }
}
