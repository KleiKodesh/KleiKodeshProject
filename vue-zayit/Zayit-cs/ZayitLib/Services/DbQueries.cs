using Dapper;
using System;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;

namespace Zayit.Services
{
    public class DbQueries
    {
        private readonly IDbConnection _connection;

        public DbQueries()
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var databasePath = Path.Combine(
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

                    if (!File.Exists(databasePath))
                    {
                        var error = $"Database not found at any location. Primary: {databasePath}";
                        Console.WriteLine($"[DbQueries] ERROR: {error}");
                        throw new FileNotFoundException(error);
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
    }
}