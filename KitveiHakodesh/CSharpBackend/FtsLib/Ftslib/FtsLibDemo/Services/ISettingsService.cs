namespace FtsLibDemo.Services
{
    /// <summary>
    /// Persists user preferences across sessions.
    /// </summary>
    public interface ISettingsService
    {
        /// <summary>Path of the SQLite DB for which the index was last built.</summary>
        string IndexedDbPath { get; set; }

        /// <summary>Saved window left position in device-independent pixels. Null means no saved position.</summary>
        double? WindowLeft { get; set; }

        /// <summary>Saved window top position in device-independent pixels. Null means no saved position.</summary>
        double? WindowTop { get; set; }

        /// <summary>Saved window width in device-independent pixels. Null means use default.</summary>
        double? WindowWidth { get; set; }

        /// <summary>Saved window height in device-independent pixels. Null means use default.</summary>
        double? WindowHeight { get; set; }

        /// <summary>Whether the window was maximized when last closed.</summary>
        bool WindowMaximized { get; set; }

        void Save();
    }
}
