using Dapper;
using Microsoft.VisualBasic;
using System;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Zayit.Services
{
    public class DbQueries
    {
        private static IDbConnection _connection;

        public DbQueries()
        {
            if (_connection != null)
                return;

            string dbPath = CurrentDbPath;
            if (!File.Exists(dbPath))
            {
                // Don't show dialog - let Vue handle UI
                return;
            }

            InitializeConnection(dbPath);
        }

        static void InitializeConnection(string dbPath)
        {
            // Dispose of existing connection if it exists
            if (_connection != null)
            {
                _connection.Close();
                _connection.Dispose();
                _connection = null;
            }

            _connection = new SQLiteConnection($"Data Source={dbPath};Version=3;Read Only=True;");
            _connection.Open();
        }

        static string DefaultDbPath =>
           Path.Combine(
               Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
               "io.github.kdroidfilter.seforimapp",
               "databases",
               "seforim.db"
           );

        public static string CurrentDbPath =>
            Interaction.GetSetting("ZayitApp", "Database", "Path", DefaultDbPath);

        public static void SetDatabasePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Database path cannot be null or empty", nameof(path));

            if (!File.Exists(path))
                throw new FileNotFoundException($"Database file not found: {path}");

            // Test if it's a valid SQLite database
            try
            {
                using (var testConnection = new SQLiteConnection($"Data Source={path};Version=3;Read Only=True;"))
                {
                    testConnection.Open();
                    testConnection.Close();
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Invalid SQLite database file: {ex.Message}", ex);
            }

            Interaction.SaveSetting("ZayitApp", "Database", "Path", path);
            InitializeConnection(path);
        }

        public static void ClearDatabasePath()
        {
            Interaction.DeleteSetting("ZayitApp", "Database", "Path");
            InitializeConnection(DefaultDbPath);
        }

        public static bool ValidateDatabasePath(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    return false;

                using (var testConnection = new SQLiteConnection($"Data Source={path};Version=3;Read Only=True;"))
                {
                    testConnection.Open();
                    testConnection.Close();
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsDatabaseAvailable()
        {
            string dbPath = CurrentDbPath;
            bool exists = File.Exists(dbPath);
            Console.WriteLine($"[DbQueries] IsDatabaseAvailable check - Path: {dbPath}, Exists: {exists}");
            
            if (!exists)
                return false;
            
            // Also check if the database is valid by checking for required tables
            try
            {
                using (var testConnection = new SQLiteConnection($"Data Source={dbPath};Version=3;Read Only=True;"))
                {
                    testConnection.Open();
                    using (var cmd = testConnection.CreateCommand())
                    {
                        // Check if the 'book' table exists (a core table that should always be present)
                        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='book'";
                        var result = cmd.ExecuteScalar();
                        bool isValid = result != null;
                        Console.WriteLine($"[DbQueries] Database validation - Has 'book' table: {isValid}");
                        return isValid;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DbQueries] Database validation failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Execute arbitrary SQL query sent from TypeScript
        /// SQL queries are defined in sqlQueries.ts
        /// </summary>
        public object ExecuteQuery(string sql, object[] parameters = null)
        {
            if (_connection == null)
            {
                Console.WriteLine("[DbQueries] ERROR: No database connection available.");
                return new object[0];
            }

            try
            {
                object result;
                if (parameters == null || parameters.Length == 0)
                {
                    result = _connection
                        .Query(sql)
                        .ToArray();
                }
                else
                {
                    // Convert object[] to DynamicParameters for Dapper
                    var dynamicParams = new DynamicParameters();
                    for (int i = 0; i < parameters.Length; i++)
                    {
                        dynamicParams.Add($"@p{i}", parameters[i]);
                    }

                    // Replace ? placeholders with @p0, @p1, etc. (left to right)
                    var parameterizedSql = sql;
                    int paramIndex = 0;
                    while (parameterizedSql.Contains("?") && paramIndex < parameters.Length)
                    {
                        int questionMarkIndex = parameterizedSql.IndexOf("?");
                        parameterizedSql = parameterizedSql.Substring(0, questionMarkIndex) +
                                         $"@p{paramIndex}" +
                                         parameterizedSql.Substring(questionMarkIndex + 1);
                        paramIndex++;
                    }

                    result = _connection
                        .Query(parameterizedSql, dynamicParams)
                        .ToArray();
                }

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DbQueries] ERROR executing query: {ex}");
                return new object[0];
            }
        }

        public void Dispose()
        {
            // Instance dispose - for backwards compatibility
            DisposeConnection();
        }

        public static void DisposeConnection()
        {
            if (_connection != null)
            {
                try
                {
                    _connection.Close();
                    _connection.Dispose();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DbQueries] Warning: Error disposing connection: {ex.Message}");
                }
                finally
                {
                    _connection = null;
                }
            }
        }
    }
}