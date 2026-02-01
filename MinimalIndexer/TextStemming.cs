using System;
using System.Collections.Generic;

namespace MinimalIndexer
{
    internal static class SmartStemmer
    {
        static LruCache<string, HashSet<string>> Cache =
            new LruCache<string, HashSet<string>>(100000);

        const int MinStemLength = 3;

        internal static void ResetCache() =>
            Cache = new LruCache<string, HashSet<string>>(100000);

        // ================= ENTRY =================
        internal static IEnumerable<string> Generate(string word)
        {
            if (string.IsNullOrWhiteSpace(word))
                yield break;

            if (Cache.TryGet(word, out var cached))
            {
                foreach (var w in cached)
                    yield return w;
                yield break;
            }

            var set = new HashSet<string>(StringComparer.Ordinal);
            Apply(word, UnifiedRules.Rules, set);

            Cache.Add(word, set);

            foreach (var w in set)
                yield return w;
        }

        // ================= APPLY =================
        private static void Apply(string w, Rules r, HashSet<string> set)
        {
            string cur = w;
            set.Add(cur);

            // 1. Suffix
            cur = StemmerCore.Suffix(cur, r.Suffixes);
            set.Add(cur);
            if (cur.Length < MinStemLength) return;

            // 2. Prefix
            var p = StemmerCore.Prefix(cur, r.Prefixes);
            if (p != cur)
            {
                cur = p;
                set.Add(cur);
                if (cur.Length < MinStemLength) return;
            }

            // 3. Infinitive
            var inf = StemmerCore.RemoveInfinitiveVowel(cur);
            if (inf != null)
            {
                cur = inf;
                set.Add(cur);
                if (cur.Length < MinStemLength) return;
            }

            // 4. Ktiv Haser
            var haser = StemmerCore.RemoveKtivHaser(cur);
            if (haser != null)
            {
                cur = haser;
                set.Add(cur);
                if (cur.Length < MinStemLength) return;
            }

            // 5. Smichut
            var sm = StemmerCore.RemoveSmichutYod(cur);
            if (sm != null)
                set.Add(sm);
        }
    }

    // ================= RULES =================
    internal sealed class Rules
    {
        internal string[] Prefixes;
        internal string[] Suffixes;
    }

    internal static class UnifiedRules
    {
        internal static readonly Rules Rules = new Rules
        {
            Prefixes = new[]
            {
                "וכש","וכ","כש",
                "וה","ול","וב","ומ",
                "ו","ה","ב","כ","ל","מ","ש"
            },

            Suffixes = new[]
            {
                "יהם","יהן","יכם","יכן","יות","ונות","נות","יו","יה",
                "ים","ות","תי","נו","תם","תן","ו","ת","י","ה","ם","ן",
                "ני","ך","הו","כם"
            }
        };
    }

    // ================= CORE =================
    internal static class StemmerCore
    {
        internal static string RemoveKtivHaser(string w)
        {
            if (w.Length >= 3 && (w[1] == 'י' || w[1] == 'ו'))
                return w[0] + w.Substring(2);
            return null;
        }

        internal static string RemoveInfinitiveVowel(string w)
        {
            if (w.Length >= 4 && w[0] == 'ל')
            {
                int i = w.Length - 2;
                if (w[i] == 'ו' || w[i] == 'י')
                    return w.Remove(i, 1);
            }
            return null;
        }

        internal static string RemoveSmichutYod(string w)
        {
            if (w.Length >= 3 && w.EndsWith("י"))
                return w.Substring(0, w.Length - 1);
            return null;
        }

        internal static string Prefix(string w, string[] rules)
        {
            for (int i = 0; i < rules.Length; i++)
            {
                var r = rules[i];
                if (w.StartsWith(r) && w.Length > r.Length + 2)
                    return w.Substring(r.Length);
            }
            return w;
        }

        internal static string Suffix(string w, string[] rules)
        {
            for (int i = 0; i < rules.Length; i++)
            {
                var r = rules[i];
                if (w.EndsWith(r) && w.Length > r.Length + 2)
                    return w.Substring(0, w.Length - r.Length);
            }
            return w;
        }
    }
}


namespace MinimalIndexer
{
    internal class LruCache<TKey, TValue>
    {
        readonly int _capacity;
        readonly Dictionary<TKey, LinkedListNode<CacheItem>> _cache;
        readonly LinkedList<CacheItem> _lru;

        struct CacheItem
        {
            internal TKey Key;
            internal TValue Value;
        }

        internal LruCache(int capacity)
        {
            _capacity = capacity;
            _cache = new Dictionary<TKey, LinkedListNode<CacheItem>>(capacity);
            _lru = new LinkedList<CacheItem>();
        }

        internal bool TryGet(TKey key, out TValue value)
        {
            if (_cache.TryGetValue(key, out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                value = node.Value.Value;
                return true;
            }

            value = default(TValue);
            return false;
        }

        internal void Add(TKey key, TValue value)
        {
            if (_cache.TryGetValue(key, out var node))
            {
                _lru.Remove(node);
                node.Value = new CacheItem { Key = key, Value = value };
                _lru.AddFirst(node);
                return;
            }

            if (_cache.Count >= _capacity)
            {
                var last = _lru.Last;
                _lru.RemoveLast();
                _cache.Remove(last.Value.Key);
            }

            var newNode = _lru.AddFirst(new CacheItem { Key = key, Value = value });
            _cache[key] = newNode;
        }
    }
}

