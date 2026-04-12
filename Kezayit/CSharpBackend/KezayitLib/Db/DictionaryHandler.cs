using KezayitLib.Bridge;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace KezayitLib.Db
{
    /// <summary>
    /// Handles "dictQuery" actions — SQL queries against dictionary.db.
    /// </summary>
    public class DictionaryHandler
    {
        private readonly WebBridge _bridge;
        private readonly DictionaryDbAccess _db;

        public DictionaryHandler(WebBridge bridge, string dictDbPath)
        {
            _bridge = bridge;
            _db = new DictionaryDbAccess(dictDbPath);
        }

        public async Task HandleQuery(JsonElement root, string id)
        {
            string sql = root.GetProperty("sql").GetString();
            try
            {
                var rows = await Task.Run(() => _db.Query(sql, ParseParams(root)));
                _bridge.Reply(id, new { rows });
            }
            catch (Exception ex)
            {
                _bridge.Reply(id, new { error = ex.Message });
            }
        }

        private static object[] ParseParams(JsonElement root)
        {
            if (!root.TryGetProperty("params", out var el) || el.ValueKind != JsonValueKind.Array)
                return Array.Empty<object>();
            var result = new object[el.GetArrayLength()];
            int i = 0;
            foreach (var item in el.EnumerateArray())
            {
                if      (item.ValueKind == JsonValueKind.String) result[i] = item.GetString();
                else if (item.ValueKind == JsonValueKind.Number) result[i] = item.TryGetInt64(out long l) ? (object)l : item.GetDouble();
                else if (item.ValueKind == JsonValueKind.True)   result[i] = true;
                else if (item.ValueKind == JsonValueKind.False)  result[i] = false;
                else                                             result[i] = null;
                i++;
            }
            return result;
        }
    }
}
