using KezayitLib.Db;
using System.Collections.Generic;
using System.IO;

namespace KezayitLib.Dictionary
{
    /// <summary>
    /// Pure data layer for dictionary queries.
    /// Owns the two read-only dictionary databases and exposes query methods.
    /// Has no knowledge of the web bridge — AppViewer handles messaging.
    /// </summary>
    public class DictionaryHandler
    {
        private readonly DbAccess _aramaicDictDb;

        public DictionaryHandler(string appDir)
        {
            string aramaicDictPath = Path.Combine(appDir, "dicts", "kezayit_dictionary.db");
            if (File.Exists(aramaicDictPath))
                _aramaicDictDb = new DbAccess(aramaicDictPath);
        }

        public bool IsAramaicDbReady => _aramaicDictDb != null;

        /// <summary>Runs a SQL query against the Aramaic dictionary database.</summary>
        public IEnumerable<IDictionary<string, object>> QueryAramaic(string sql, object[] parameters)
            => _aramaicDictDb.Query(sql, parameters);
    }
}
