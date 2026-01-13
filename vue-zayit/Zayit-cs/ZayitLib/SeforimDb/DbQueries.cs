using Dapper;
using System;
using System.Linq;

namespace Zayit.SeforimDb
{
    public class DbQueries
    {
        readonly DbManager _db = new DbManager();

        /// <summary>
        /// Execute arbitrary SQL query sent from TypeScript
        /// SQL queries are defined in sqlQueries.ts
        /// </summary>
        public object ExecuteQuery(string sql, object[] parameters = null)
        {
            System.Diagnostics.Debug.WriteLine($"Executing SQL: {sql}");
            System.Diagnostics.Debug.WriteLine($"DB Connection null: {_db?.DapperConnection == null}");
            
            if (_db?.DapperConnection == null)
            {
                System.Diagnostics.Debug.WriteLine("Database connection is null!");
                return new object[0];
            }
            
            try
            {
                object result;
                if (parameters == null || parameters.Length == 0)
                {
                    result = _db.DapperConnection
                        .Query(sql)
                        .ToArray();
                }
                else
                {
                    result = _db.DapperConnection
                        .Query(sql, parameters)
                        .ToArray();
                }
                
                System.Diagnostics.Debug.WriteLine($"Query returned {((Array)result)?.Length ?? 0} rows");
                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Database query error: {ex}");
                return new object[0];
            }
        }
    }
}
