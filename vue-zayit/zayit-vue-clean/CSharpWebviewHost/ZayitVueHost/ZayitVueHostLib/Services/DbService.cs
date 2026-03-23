using System;

namespace Zayit.Services
{
    public class DbService
    {
        private readonly DbQueries _db;

        public DbService(DbQueries db) => _db = db;

        public object ExecuteQuery(string q, object[] p = null)
        {
            try
            {
                return _db.ExecuteQuery(q, p);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DbService] ERROR in ExecuteQuery: {ex.Message}");
                return new object[0];
            }
        }
    }
}