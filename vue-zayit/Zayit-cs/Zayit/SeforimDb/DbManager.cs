
using System;
using System.Data;
using System.Data.SQLite;
using System.IO;

namespace Zayit.SeforimDb
{
    public class DbManager : IDisposable
    {
        public readonly SQLiteConnection Connection;
        // Expose the connection as IDbConnection for Dapper
        public IDbConnection DapperConnection;

        public DbManager(string databasePath = null)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

                databasePath = Path.Combine(
                    appData,
                    "io.github.kdroidfilter.seforimapp",
                    "databases",
                    "seforim.db"
                );
            }

            if (!File.Exists(databasePath))
            {
                var res = System.Windows.Forms.MessageBox.Show(
                    "קובץ המסד לא נמצא.\nלהתקין את תוכנת זית עכשיו?",
                    "שגיאה",
                    System.Windows.Forms.MessageBoxButtons.YesNo,
                    System.Windows.Forms.MessageBoxIcon.Warning);

                if (res == System.Windows.Forms.DialogResult.Yes)
                    System.Diagnostics.Process.Start("https://kdroidfilter.github.io/Zayit/");
                else
                    System.Windows.Forms.MessageBox.Show("בלי התקנת זית התוכנה לא תעבוד.", "שים לב");

                return;
            }


            Connection = new SQLiteConnection($"Data Source={databasePath};Version=3;");
            Connection.Open();
            DapperConnection = Connection;
        }


        public void Dispose()
        {
            Connection?.Dispose();
        }
    }

}
