namespace FtsLibDemo.Services
{
    /// <summary>
    /// Persists user preferences across sessions.
    /// </summary>
    public interface ISettingsService
    {
        /// <summary>Path of the SQLite DB for which the index was last built.</summary>
        string IndexedDbPath { get; set; }

        void Save();
    }
}
