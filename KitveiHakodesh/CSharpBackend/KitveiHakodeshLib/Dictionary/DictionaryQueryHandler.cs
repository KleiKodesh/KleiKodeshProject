using KitveiHakodeshLib.Bridge;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using CoreDictionary = KitveiHakodesh.Core.Dictionary;

namespace KitveiHakodeshLib.Dictionary
{
    /// <summary>
    /// The TYPED dictionary bridge for the hosted app: the page names a lookup and
    /// KitveiHakodesh.Core runs it — the same nine methods the dev service calls.
    ///
    /// The counterpart of SeforimQueryHandler, and it replaces the dict-sql passthrough for the
    /// same reason: with the SQL in the page, a query and the code reading its rows lived in
    /// different places, and the hosted and dev legs could answer one lookup differently.
    /// </summary>
    public sealed class DictionaryQueryHandler
    {
        private readonly WebBridge _bridge;
        private readonly CoreDictionary.DictionaryDbQueries _dictionary;

        /// <param name="databasePath">The dictionary, or null when it is not installed — then
        /// every lookup answers empty, which is a STATE and not a failure.</param>
        public DictionaryQueryHandler(WebBridge bridge, string databasePath)
        {
            _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
            if (!string.IsNullOrEmpty(databasePath))
                _dictionary = new CoreDictionary.DictionaryDbQueries(databasePath);
        }

        public bool IsReady => _dictionary != null && _dictionary.IsAvailable;

        /// <summary>
        /// Runs one named lookup and replies with the shape the frontend already expects.
        ///
        /// Runs OFF the WinForms UI thread — that thread also services WebView2 accelerator
        /// keys, so a stall here delays Ctrl+Tab / Ctrl+W reaching the page.
        /// </summary>
        public async Task HandleAsync(JsonElement root, string id)
        {
            if (!IsReady)
            {
                _bridge.Reply(id, new { error = "Dictionary database not available" });
                return;
            }

            string op = root.TryGetProperty("op", out var o) ? o.GetString() : null;
            if (string.IsNullOrEmpty(op))
            {
                _bridge.Reply(id, new { error = "dictQuery requires an op" });
                return;
            }

            // Read the arguments HERE: a JsonElement borrows the JsonDocument the message loop
            // disposes on return, so reading one on the worker thread would be a use-after-dispose.
            string term;
            List<string> candidates;
            try
            {
                root.TryGetProperty("params", out var bag);
                term = Str(bag, "term");
                candidates = Strings(bag, "candidates");
            }
            catch (Exception ex) { _bridge.Reply(id, new { error = ex.Message }); return; }

            try
            {
                object payload = await Task.Run(() => Run(op, term, candidates)).ConfigureAwait(false);
                _bridge.Reply(id, payload);
            }
            catch (Exception ex)
            {
                _bridge.Reply(id, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Maps a lookup name onto a Core query.
        ///
        /// The reply KEYS are the frontend's contract and deliberately differ from Core's
        /// clearer names — rows/isExact/matched rather than Senses/WordExists/MatchedCandidate.
        /// Translating them is what this method is for.
        /// </summary>
        private object Run(string op, string term, List<string> candidates)
        {
            switch (op)
            {
                case "dictExact":
                {
                    var result = _dictionary.Exact(term);
                    return new { rows = Senses(result.Senses), isExact = result.WordExists };
                }
                case "dictPrefix": return new { rows = Senses(_dictionary.Prefix(term)) };
                case "dictContains": return new { rows = Senses(_dictionary.Contains(term)) };

                case "dictLinks":
                {
                    var links = _dictionary.Links(term);
                    var wire = new List<object>(links.Count);
                    foreach (var link in links) wire.Add(new { kind = link.Kind, word = link.Word });
                    return new { links = wire };
                }

                case "dictSynonyms": return new { words = _dictionary.Synonyms(term) };
                case "dictVariants": return new { words = _dictionary.Variants(term) };
                case "dictSpellCandidates": return new { words = _dictionary.SpellingCandidates(term) };

                case "dictAbbrevSenses":
                {
                    var match = _dictionary.AbbreviationSenses(candidates);
                    return match.Found
                        ? new { matched = match.MatchedCandidate, rows = Senses(match.Senses) }
                        : new { matched = (string)null, rows = new List<object>() };
                }

                case "dictKetivVariants": return new { words = _dictionary.ExistingHeadwords(candidates) };

                default:
                    throw new ArgumentException("unknown dictQuery op: " + op);
            }
        }

        /// <summary>
        /// Projects senses onto the frontend's SenseRow. Hand-built rather than serialized from
        /// Core's type because of source_id: it is snake_case on this wire, and because the
        /// bridge serializes with no naming policy a Core model would otherwise arrive
        /// PascalCase and read back as undefined fields.
        /// </summary>
        private static List<object> Senses(List<CoreDictionary.DictionarySense> senses)
        {
            var rows = new List<object>(senses?.Count ?? 0);
            if (senses == null) return rows;

            foreach (var sense in senses)
            {
                rows.Add(new
                {
                    headword = sense.Headword,
                    nikud = sense.Nikud,
                    text = sense.Text,
                    source = sense.Source,
                    source_id = sense.SourceId,
                });
            }
            return rows;
        }

        private static string Str(JsonElement bag, string name) =>
            bag.ValueKind == JsonValueKind.Object
            && bag.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? "" : "";

        private static List<string> Strings(JsonElement bag, string name)
        {
            var list = new List<string>();
            if (bag.ValueKind != JsonValueKind.Object) return list;
            if (!bag.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array) return list;

            foreach (var item in value.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String) list.Add(item.GetString() ?? "");
            return list;
        }
    }
}
