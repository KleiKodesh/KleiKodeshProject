using System;
using System.Configuration;


namespace WpfLib.Helpers
{  
    public static class ConfigurationManagerWrapper
    {
        /// <summary>
        /// Gets a value from AppSettings. Returns defaultValue if the key is not found or cannot be parsed.
        /// </summary>
        public static T GetAppSetting<T>(string key, T defaultValue = default)
        {
            try
            {
                var value = ConfigurationManager.AppSettings[key];
                if (value == null)
                    return defaultValue;

                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// Updates or adds a key-value pair in AppSettings and persists the change to the configuration file.
        /// </summary>
        public static void SetAppSetting(string key, string value)
        {
            var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            if (config.AppSettings.Settings[key] != null)
            {
                config.AppSettings.Settings[key].Value = value;
            }
            else
            {
                config.AppSettings.Settings.Add(key, value);
            }
            config.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection("appSettings");
        }
    }

}
