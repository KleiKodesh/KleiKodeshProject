using RegexFindLib.Properties;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace RegexFindLib.UI
{
    /// <summary>
    /// Persists recent search/replace strings in the app's user.config
    /// via ApplicationSettingsBase — one JSON string, zero file management.
    /// Max 10 entries per type, most-recent first.
    /// </summary>
    public class SearchHistory
    {
        const int MaxSize = 10;

        public static readonly SearchHistory Find    = new SearchHistory("find");
        public static readonly SearchHistory Replace = new SearchHistory("replace");

        readonly string _key;
        SearchHistory(string key) => _key = key;

        // ── Public API ────────────────────────────────────────────────────────

        public IReadOnlyList<string> Load()
        {
            try
            {
                var all = LoadAll();
                return all.ContainsKey(_key)
                    ? all[_key].Where(s => !string.IsNullOrWhiteSpace(s)).ToList()
                    : (IReadOnlyList<string>)Array.Empty<string>();
            }
            catch { return Array.Empty<string>(); }
        }

        public void Add(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            try
            {
                var all = LoadAll();
                var list = all.ContainsKey(_key) ? all[_key] : new List<string>();
                list.RemoveAll(s => s == text.Trim());
                list.Insert(0, text.Trim());
                if (list.Count > MaxSize) list = list.Take(MaxSize).ToList();
                all[_key] = list;
                SaveAll(all);
            }
            catch { }
        }

        public void Remove(string text)
        {
            try
            {
                var all = LoadAll();
                if (!all.ContainsKey(_key)) return;
                all[_key].RemoveAll(s => s == text);
                SaveAll(all);
            }
            catch { }
        }

        public void Clear()
        {
            try
            {
                var all = LoadAll();
                all[_key] = new List<string>();
                SaveAll(all);
            }
            catch { }
        }

        // ── Storage — one JSON string in user.config ──────────────────────────

        static Dictionary<string, List<string>> LoadAll()
        {
            try
            {
                var json = Settings.Default.SearchHistory;
                if (string.IsNullOrEmpty(json))
                    return new Dictionary<string, List<string>>();
                return JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json)
                    ?? new Dictionary<string, List<string>>();
            }
            catch { return new Dictionary<string, List<string>>(); }
        }

        static void SaveAll(Dictionary<string, List<string>> data)
        {
            Settings.Default.SearchHistory =
                JsonSerializer.Serialize(data);
            Settings.Default.Save();
        }
    }
}
