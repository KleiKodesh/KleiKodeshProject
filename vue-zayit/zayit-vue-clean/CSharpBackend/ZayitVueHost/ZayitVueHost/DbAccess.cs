using Dapper;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Text.RegularExpressions;

namespace ZayitVueHost
{
    /// <summary>
    /// Thin wrapper around SQLite. Converts positional ? params to named @p0, @p1, ...
    /// because Dapper requires named parameters.
    /// </summary>
    internal class DbAccess
    {
        private readonly string _connectionString;

        public DbAccess(string path)
        {
            _connectionString = "Data Source=" + path + ";Version=3;Read Only=True;";
        }

        public IEnumerable<IDictionary<string, object>> Query(string sql, object[] parameters)
        {
            int index = 0;
            string namedSql = Regex.Replace(sql, @"\?", _ => "@p" + index++);

            var dp = new DynamicParameters();
            for (int i = 0; i < parameters.Length; i++)
                dp.Add("@p" + i, parameters[i]);

            using (var conn = new SQLiteConnection(_connectionString))
            {
                return conn.Query(namedSql, dp)
                           .Select(row => (IDictionary<string, object>)row)
                           .ToList();
            }
        }
    }
}
