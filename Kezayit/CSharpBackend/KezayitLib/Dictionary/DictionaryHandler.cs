using KezayitLib.Db;
using System;
using System.Collections.Generic;
using System.IO;

namespace KezayitLib.Dictionary
{
    /// <summary>
    /// Thin data layer for the Kezayit dictionary database.
    /// SQL lives in the Vue frontend (dictionaryDb.ts) and is sent over the bridge.
    /// This class only owns the connection — it does not define any queries.
    /// The single DB (kezayit_dictionary.db) contains entry, sense, related, and source tables.
    /// </summary>
    public class DictionaryHandler : IDisposable
    {
        private readonly DbAccess _db;

        public DictionaryHandler(string appDir)
        {
            string path = Path.Combine(appDir, "dictionary", "kezayit_dictionary.db");
            if (File.Exists(path))
                _db = new DbAccess(path);
        }

        public bool IsReady => _db != null;

        public IEnumerable<IDictionary<string, object>> Query(string sql, object[] parameters)
            => _db.Query(sql, parameters);

        public void Dispose()
        {
            if (_db != null) _db.Dispose();
        }
    }
}
