using Microsoft.VisualBasic;
using System;
using System.IO;

namespace ZayitVueHost
{
    /// <summary>
    /// Persists app settings to the Windows registry via VB Interaction helpers.
    /// </summary>
    internal static class Settings
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
    }
}
