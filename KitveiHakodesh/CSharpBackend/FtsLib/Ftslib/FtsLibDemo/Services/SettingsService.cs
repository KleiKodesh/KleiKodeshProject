using System;
using System.Globalization;
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

        private const string KeyDbPath        = "IndexedDbPath";
        private const string KeyWindowLeft    = "WindowLeft";
        private const string KeyWindowTop     = "WindowTop";
        private const string KeyWindowWidth   = "WindowWidth";
        private const string KeyWindowHeight  = "WindowHeight";
        private const string KeyWindowMaximized = "WindowMaximized";

        public string  IndexedDbPath    { get; set; }
        public double? WindowLeft       { get; set; }
        public double? WindowTop        { get; set; }
        public double? WindowWidth      { get; set; }
        public double? WindowHeight     { get; set; }
        public bool    WindowMaximized  { get; set; }

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
                    $"{KeyDbPath}={IndexedDbPath ?? string.Empty}",
                    $"{KeyWindowLeft}={FormatDouble(WindowLeft)}",
                    $"{KeyWindowTop}={FormatDouble(WindowTop)}",
                    $"{KeyWindowWidth}={FormatDouble(WindowWidth)}",
                    $"{KeyWindowHeight}={FormatDouble(WindowHeight)}",
                    $"{KeyWindowMaximized}={WindowMaximized}"
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

                    switch (key)
                    {
                        case KeyDbPath:
                            IndexedDbPath = val;
                            break;
                        case KeyWindowLeft:
                            WindowLeft = ParseDouble(val);
                            break;
                        case KeyWindowTop:
                            WindowTop = ParseDouble(val);
                            break;
                        case KeyWindowWidth:
                            WindowWidth = ParseDouble(val);
                            break;
                        case KeyWindowHeight:
                            WindowHeight = ParseDouble(val);
                            break;
                        case KeyWindowMaximized:
                            WindowMaximized = string.Equals(val, "True", StringComparison.OrdinalIgnoreCase);
                            break;
                    }
                }
            }
            catch { /* non-critical */ }
        }

        private static string FormatDouble(double? value)
        {
            return value.HasValue
                ? value.Value.ToString("F2", CultureInfo.InvariantCulture)
                : string.Empty;
        }

        private static double? ParseDouble(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            double result;
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
                ? result
                : (double?)null;
        }
    }
}
