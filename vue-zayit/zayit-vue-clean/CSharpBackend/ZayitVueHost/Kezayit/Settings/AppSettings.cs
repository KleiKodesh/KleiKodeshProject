using Microsoft.VisualBasic;
using System;
using System.IO;

namespace Kezayit.Settings
{
    /// <summary>
    /// Persists app settings to the Windows registry via VB Interaction helpers.
    /// </summary>
    public static class AppSettings
    {
        private static readonly string DefaultDbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "io.github.kdroidfilter.seforimapp", "databases", "seforim.db");

        public static string LoadDbPath()
        {
            return Interaction.GetSetting("ZayitApp", "Database", "Path", DefaultDbPath);
        }

        public static void SaveDbPath(string path)
        {
            Interaction.SaveSetting("ZayitApp", "Database", "Path", path);
        }

        public static DateTime LoadHbCsvLastUpdated()
        {
            string raw = Interaction.GetSetting("ZayitApp", "HebrewBooks", "CsvLastUpdated", "");
            if (DateTime.TryParse(raw, out DateTime dt)) return dt;
            return DateTime.MinValue;
        }

        public static void SaveHbCsvLastUpdated(DateTime utcDate)
        {
            Interaction.SaveSetting("ZayitApp", "HebrewBooks", "CsvLastUpdated", utcDate.ToString("o"));
        }
    }
}
