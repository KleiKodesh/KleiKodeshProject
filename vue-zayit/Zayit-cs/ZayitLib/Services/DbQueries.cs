using Dapper;
using Microsoft.VisualBasic;
using System;
using System.Configuration;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Zayit.Viewer;

namespace Zayit.Services
{
    public class DbQueries
    {
        private readonly IDbConnection _connection;
        private static string _customDatabasePath = null;

        public DbQueries()
        {
            try
            {
                string databasePath;
                
                // Load custom path from VB.NET settings if not already in memory
                if (string.IsNullOrEmpty(_customDatabasePath))
                {
                    try
                    {
                        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                        var defaultPath = Path.Combine(appDataPath, "io.github.kdroidfilter.seforimapp", "databases", "seforim.db");
                        
                        var settingsPath = Interaction.GetSetting("ZayitApp", "Database", "Path", defaultPath);
                        if (!string.IsNullOrEmpty(settingsPath) && File.Exists(settingsPath))
                        {
                            _customDatabasePath = settingsPath;
                            Console.WriteLine($"[DbQueries] Loaded database path from VB settings: {settingsPath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[DbQueries] Failed to load database path from VB settings: {ex.Message}");
                    }
                }
                
                // Use custom path if set, otherwise use default logic
                if (!string.IsNullOrEmpty(_customDatabasePath) && File.Exists(_customDatabasePath))
                {
                    databasePath = _customDatabasePath;
                    Console.WriteLine($"[DbQueries] Using custom database path: {databasePath}");
                }
                else
                {
                    // Default path resolution logic
                    var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    databasePath = Path.Combine(
                        appData,
                        "io.github.kdroidfilter.seforimapp",
                        "databases",
                        "seforim.db"
                    );

                    Console.WriteLine($"[DbQueries] Looking for database at: {databasePath}");
                    Console.WriteLine($"[DbQueries] AppData folder: {appData}");
                    Console.WriteLine($"[DbQueries] Database exists: {File.Exists(databasePath)}");

                    if (!File.Exists(databasePath))
                    {
                        // Try alternative locations
                        var alternativePaths = new[]
                        {
                            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "seforim.db"),
                            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "databases", "seforim.db"),
                            Path.Combine(Environment.CurrentDirectory, "seforim.db"),
                            Path.Combine(Environment.CurrentDirectory, "databases", "seforim.db")
                        };

                        Console.WriteLine($"[DbQueries] Database not found at primary location, checking alternatives:");
                        foreach (var altPath in alternativePaths)
                        {
                            Console.WriteLine($"[DbQueries] Checking: {altPath} - Exists: {File.Exists(altPath)}");
                            if (File.Exists(altPath))
                            {
                                databasePath = altPath;
                                Console.WriteLine($"[DbQueries] Using alternative database path: {databasePath}");
                                break;
                            }
                        }
                    }

                    if (!File.Exists(databasePath))
                    {
                        var error = $"Database not found at any location. Primary: {databasePath}";
                        Console.WriteLine($"[DbQueries] ERROR: {error}");
                        
                        // Show dialog to user for database not found
                        ShowDatabaseNotFoundDialog();
                        
                        // After dialog, check if custom path was set
                        if (!string.IsNullOrEmpty(_customDatabasePath) && File.Exists(_customDatabasePath))
                        {
                            databasePath = _customDatabasePath;
                            Console.WriteLine($"[DbQueries] Using database path from dialog: {databasePath}");
                        }
                        else
                        {
                            throw new FileNotFoundException(error);
                        }
                    }
                }

                var connectionString = $"Data Source={databasePath};Version=3;";
                Console.WriteLine($"[DbQueries] Connecting with: {connectionString}");
                
                _connection = new SQLiteConnection(connectionString);
                _connection.Open();
                
                Console.WriteLine($"[DbQueries] Database connection opened successfully");
                
                // Test the connection with a simple query
                var testResult = _connection.Query("SELECT COUNT(*) as count FROM sqlite_master WHERE type='table'").FirstOrDefault();
                Console.WriteLine($"[DbQueries] Database contains {testResult?.count ?? 0} tables");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DbQueries] ERROR initializing database: {ex}");
                throw;
            }
        }

        /// <summary>
        /// Execute arbitrary SQL query sent from TypeScript
        /// SQL queries are defined in sqlQueries.ts
        /// </summary>
        public object ExecuteQuery(string sql, object[] parameters = null)
        {
            // Only log GetLinks queries to reduce noise
            if (sql.Contains("connection") && sql.Contains("line_id"))
            {
                Console.WriteLine($"[DbQueries] GetLinks SQL: {sql}");
                if (parameters != null && parameters.Length > 0)
                {
                    Console.WriteLine($"[DbQueries] GetLinks parameters ({parameters.Length}): {string.Join(", ", parameters.Select(p => $"{p?.GetType().Name}:{p}"))}");
                }
            }
            
            if (_connection == null)
            {
                Console.WriteLine("[DbQueries] ERROR: Database connection is null!");
                return new object[0];
            }
            
            if (_connection.State != ConnectionState.Open)
            {
                Console.WriteLine($"[DbQueries] ERROR: Database connection state is {_connection.State}");
                try
                {
                    _connection.Open();
                    Console.WriteLine("[DbQueries] Reopened database connection");
                }
                catch (Exception reopenEx)
                {
                    Console.WriteLine($"[DbQueries] ERROR: Failed to reopen connection: {reopenEx}");
                    return new object[0];
                }
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
                
                var resultArray = result as Array;
                Console.WriteLine($"[DbQueries] Query returned {resultArray?.Length ?? 0} rows");
                
                // Log first row for debugging if available
                if (resultArray != null && resultArray.Length > 0)
                {
                    var firstRow = resultArray.GetValue(0);
                    Console.WriteLine($"[DbQueries] First row type: {firstRow?.GetType().Name}");
                }
                
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DbQueries] ERROR executing query: {ex}");
                Console.WriteLine($"[DbQueries] SQL was: {sql}");
                return new object[0];
            }
        }

        public void Dispose()
        {
            _connection?.Dispose();
        }

        /// <summary>
        /// Set custom database path - will be used by new DbQueries instances
        /// </summary>
        public static void SetCustomDatabasePath(string path)
        {
            // If path is empty or null, clear the custom path
            if (string.IsNullOrEmpty(path))
            {
                ClearCustomDatabasePath();
                return;
            }
            
            _customDatabasePath = path;
            Console.WriteLine($"[DbQueries] Custom database path set: {path}");
            
            // Persist to VB.NET settings
            try
            {
                Interaction.SaveSetting("ZayitApp", "Database", "Path", path);
                Console.WriteLine($"[DbQueries] Database path persisted to VB settings");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DbQueries] Failed to persist database path to VB settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Get current database path (custom or default)
        /// </summary>
        public static string GetCurrentDatabasePath()
        {
            // First check memory cache
            if (!string.IsNullOrEmpty(_customDatabasePath) && File.Exists(_customDatabasePath))
            {
                return _customDatabasePath;
            }

            // Try to load from VB.NET settings
            try
            {
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var defaultPath = Path.Combine(appDataPath, "io.github.kdroidfilter.seforimapp", "databases", "seforim.db");
                
                var settingsPath = Interaction.GetSetting("ZayitApp", "Database", "Path", "");
                
                // If settings path is empty or doesn't exist, use default
                if (string.IsNullOrEmpty(settingsPath))
                {
                    Console.WriteLine($"[DbQueries] No custom database path set, using default: {defaultPath}");
                    return defaultPath;
                }
                
                if (File.Exists(settingsPath))
                {
                    _customDatabasePath = settingsPath;
                    Console.WriteLine($"[DbQueries] Loaded database path from VB settings: {settingsPath}");
                    return settingsPath;
                }
                else
                {
                    Console.WriteLine($"[DbQueries] Custom database path doesn't exist, using default: {defaultPath}");
                    return defaultPath;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DbQueries] Failed to load database path from VB settings: {ex.Message}");
            }

            // Return default path
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "io.github.kdroidfilter.seforimapp", "databases", "seforim.db");
        }

        /// <summary>
        /// Clear custom database path - will revert to default logic
        /// </summary>
        public static void ClearCustomDatabasePath()
        {
            _customDatabasePath = null;
            Console.WriteLine("[DbQueries] Custom database path cleared");
            
            // Also clear from VB.NET settings by deleting the setting entirely
            try
            {
                Interaction.DeleteSetting("ZayitApp", "Database", "Path");
                Console.WriteLine("[DbQueries] Database path setting deleted from VB settings");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DbQueries] Failed to delete database path from VB settings: {ex.Message}");
                // Fallback: try to save empty string
                try
                {
                    Interaction.SaveSetting("ZayitApp", "Database", "Path", "");
                    Console.WriteLine("[DbQueries] Database path cleared from VB settings (fallback)");
                }
                catch (Exception ex2)
                {
                    Console.WriteLine($"[DbQueries] Fallback also failed: {ex2.Message}");
                }
            }
        }

        /// <summary>
        /// Show dialog when database is not found
        /// </summary>
        private static void ShowDatabaseNotFoundDialog()
        {
            try
            {
                using (var dialog = new Zayit.Viewer.DatabaseNotFoundDialog())
                {
                    var result = dialog.ShowDialog();
                    if (result == DialogResult.OK)
                    {
                        if (!string.IsNullOrEmpty(dialog.SelectedDatabasePath))
                        {
                            SetCustomDatabasePath(dialog.SelectedDatabasePath);
                        }
                        // If ShouldDownloadZayit is true, the dialog already opened the download page
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DbQueries] Error showing database not found dialog: {ex}");
                // Fallback to simple message box
                MessageBox.Show(
                    "מסד הנתונים לא נמצא. אנא הורד את אפליקציית Zayit או בחר קובץ מסד נתונים מהמחשב.",
                    "מסד נתונים לא נמצא",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
    }
}