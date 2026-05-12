using Microsoft.VisualBasic;
using System;
using System.IO;

namespace KitveiHakodeshLib.Settings
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
            return Interaction.GetSetting("KitveiHakodesh", "Database", "Path", DefaultDbPath);
        }

        public static void SaveDbPath(string path)
        {
            Interaction.SaveSetting("KitveiHakodesh", "Database", "Path", path);
        }

        public static System.Drawing.Rectangle LoadPopoutBounds()
        {
            int x = int.Parse(Interaction.GetSetting("KitveiHakodesh", "Popout", "X", "-1"));
            int y = int.Parse(Interaction.GetSetting("KitveiHakodesh", "Popout", "Y", "-1"));
            int w = int.Parse(Interaction.GetSetting("KitveiHakodesh", "Popout", "W", "900"));
            int h = int.Parse(Interaction.GetSetting("KitveiHakodesh", "Popout", "H", "750"));
            return new System.Drawing.Rectangle(x, y, w, h);
        }

        public static void SavePopoutBounds(System.Drawing.Rectangle bounds)
        {
            Interaction.SaveSetting("KitveiHakodesh", "Popout", "X", bounds.X.ToString());
            Interaction.SaveSetting("KitveiHakodesh", "Popout", "Y", bounds.Y.ToString());
            Interaction.SaveSetting("KitveiHakodesh", "Popout", "W", bounds.Width.ToString());
            Interaction.SaveSetting("KitveiHakodesh", "Popout", "H", bounds.Height.ToString());
        }

        public static DateTime LoadHbCsvLastUpdated()
        {
            string raw = Interaction.GetSetting("KitveiHakodesh", "HebrewBooks", "CsvLastUpdated", "");
            if (DateTime.TryParse(raw, out DateTime dt)) return dt;
            return DateTime.MinValue;
        }

        public static void SaveHbCsvLastUpdated(DateTime utcDate)
        {
            Interaction.SaveSetting("KitveiHakodesh", "HebrewBooks", "CsvLastUpdated", utcDate.ToString("o"));
        }
    }
}
