using System;
using System.IO;

namespace FtsLibDemo.Services
{
    /// <summary>
    /// Persists settings to a simple text file next to the executable.
    /// One key=value per line.
    /// </summary>
    public sealed class SettingsService : ISettingsService
    {
        private static readonly string FilePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "fts-demo.settings");

        private const string KeyDbPath = "IndexedDbPath";

        public string IndexedDbPath { get; set; }

        public SettingsService()
        {
            Load();
        }

        public void Save()
        {
            try
            {
                File.WriteAllLines(FilePath, new[]
                {
                    $"{KeyDbPath}={IndexedDbPath ?? string.Empty}"
                });
            }
            catch { /* non-critical */ }
        }

        private void Load()
        {
            if (!File.Exists(FilePath)) return;
            try
            {
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();
                    if (key == KeyDbPath) IndexedDbPath = val;
                }
            }
            catch { /* non-critical */ }
        }
    }
}
