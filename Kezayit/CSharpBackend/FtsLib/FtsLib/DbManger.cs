using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FtsLib
{
    using System;
    using System.Collections.Generic;
    using System.Data.SQLite;
    using System.IO;
    using Microsoft.VisualBasic;

    public class DbManager
    {
        private readonly SQLiteConnection _connection;

        public DbManager()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            string defaultPath = Path.Combine(
                appData,
                "io.github.kdroidfilter.seforimapp",
                "databases",
                "seforim.db"
            );

            string dbPath = Interaction.GetSetting(
                "ZayitApp",
                "Database",
                "Path",
                defaultPath
            );

            _connection = new SQLiteConnection($"Data Source={dbPath};Version=3;Page Size=4096;");
            _connection.Open();
        }

        public IEnumerable<(string Content, int BookId)> Lines => GetLines();

        private IEnumerable<(string Content, int BookId)> GetLines()
        {
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "SELECT content, bookId FROM line";

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (reader.IsDBNull(0))
                            continue;

                        string content = reader.GetString(0);
                        int bookId = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);

                        yield return (content, bookId);
                    }
                }
            }
        }
    }
}
