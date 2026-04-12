using Dapper;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Text.RegularExpressions;

namespace KezayitLib.Db
{
    /// <summary>
    /// Read-write access to dictionary.db.
    /// Uses a single persistent connection so the background indexer can write
    /// while the app reads.
    /// </summary>
    public class DictionaryDbAccess
    {
        private readonly string _path;

        public DictionaryDbAccess(string path)
        {
            _path = path;
        }

        private SQLiteConnection Open(bool readOnly = false)
        {
            string cs = "Data Source=" + _path + ";Version=3;" +
                        (readOnly ? "Read Only=True;" : "");
            var conn = new SQLiteConnection(cs);
            conn.Open();
            return conn;
        }

        public IEnumerable<IDictionary<string, object>> Query(string sql, object[] parameters)
        {
            int index = 0;
            string namedSql = Regex.Replace(sql, @"\?", _ => "@p" + index++);
            var dp = new DynamicParameters();
            for (int i = 0; i < parameters.Length; i++)
                dp.Add("@p" + i, parameters[i]);

            using (var conn = Open(readOnly: true))
                return conn.Query(namedSql, dp)
                           .Cast<IDictionary<string, object>>()
                           .ToList();
        }

        public void Execute(string sql, object param = null)
        {
            using (var conn = Open())
                conn.Execute(sql, param);
        }

        public T ExecuteScalar<T>(string sql, object param = null)
        {
            using (var conn = Open())
                return conn.ExecuteScalar<T>(sql, param);
        }

        public string GetMeta(string key)
        {
            using (var conn = Open(readOnly: true))
                return conn.ExecuteScalar<string>(
                    "SELECT value FROM meta WHERE key = @key", new { key });
        }

        public void SetMeta(string key, string value)
        {
            using (var conn = Open())
                conn.Execute(
                    "INSERT OR REPLACE INTO meta (key, value) VALUES (@key, @value)",
                    new { key, value });
        }
    }
}
