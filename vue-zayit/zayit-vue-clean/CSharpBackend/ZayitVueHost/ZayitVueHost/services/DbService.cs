using Dapper;
using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ZayitVueHost.services
{
    internal class DbService
    {
        public static readonly string DefaultDbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "io.github.kdroidfilter.seforimapp", "databases", "seforim.db");

        public static string LoadPath() => Interaction.GetSetting("ZayitApp", "Database", "Path", DefaultDbPath);
        public static void SavePath(string path) => Interaction.SaveSetting("ZayitApp", "Database", "Path", path);

        private readonly string _cs;

        public DbService(string path) =>
            _cs = $"Data Source={path};Version=3;Read Only=True;";

        public IEnumerable<IDictionary<string, object>> Query(string sql, object[] p)
        {
            int i = 0;
            string named = Regex.Replace(sql, @"\?", _ => $"@p{i++}");
            var dp = new DynamicParameters();
            for (int j = 0; j < p.Length; j++) dp.Add($"@p{j}", p[j]);
            using (var conn = new SQLiteConnection(_cs))
            {
                return conn.Query(named, dp).Select(r => (IDictionary<string, object>)r).ToList();
            }
        }
    }
}
