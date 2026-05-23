using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Nakdan
{
    // ═══════════════════════════════════════════════════════════
    //  TOKEN — one base character + the nikkud that follows it
    // ═══════════════════════════════════════════════════════════
    internal class Token
    {
        public char   Base;
        public string VowelsAfter;
        public int    RunIndex;
        public int    PosInRun;
    }

    // ═══════════════════════════════════════════════════════════
    //  RUN INFO — mirrors one <w:r> element
    // ═══════════════════════════════════════════════════════════
    internal class RunInfo
    {
        public int      Index;
        public XElement Element;
        public XElement TextEl;
        public string   OrigText;
        public string   StyleName;   // paragraph style that owns this run
    }

    // ═══════════════════════════════════════════════════════════
    //  DICTA GENRE
    // ═══════════════════════════════════════════════════════════
    public enum DictaGenre
    {
        Bible,
        Rabbinic,
        Modern,
        Poetry,       
    }

    // ═══════════════════════════════════════════════════════════
    //  OPTIONS passed in from the API wrapper
    // ═══════════════════════════════════════════════════════════
    public class NakdanOptions
    {
        /// <summary>
        /// Style names to skip (Hebrew or English, case-insensitive).
        /// e.g. new[] { "כותרת 1", "Heading 2", "הדגשה" }
        /// </summary>
        public ICollection<string> IgnoredStyles { get; set; } = new List<string>();

        public DictaGenre Genre { get; set; } = DictaGenre.Modern;
    }

    // ═══════════════════════════════════════════════════════════
    //  CORE ENGINE
    // ═══════════════════════════════════════════════════════════
    public class NakdanEngine
    {
        // ── Unicode ─────────────────────────────────────────────
        private const char NikkudFirst  = '\u05B0';
        private const char NikkudLast   = '\u05C7';
        private const char Meteg        = '\u05BD';
        private const char ShinDot      = '\u05C1';
        private const char ShinDotSin   = '\u05C2';

        // ── Dicta ───────────────────────────────────────────────
        private static readonly string[] DictaEndpoints =
        {
            "https://nakdan-u1-0.loadbalancer.dicta.org.il/api",
            "https://nakdan-5-1.loadbalancer.dicta.org.il/api"
        };
        private const int DictaMaxChars  = 5000;
        private const int HttpTimeoutSec = 30;

        // ── OOXML ────────────────────────────────────────────────
        private static readonly XNamespace W  =
            "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(HttpTimeoutSec)
        };

        // ════════════════════════════════════════════════════════
        //  PUBLIC ENTRY POINT
        //  ooxml  — Document.WordOpenXML or Range.WordOpenXML
        //  opts   — genre + ignored styles
        //  cancellationToken — allows cancellation of the operation
        // ════════════════════════════════════════════════════════
        public async Task<string> VowelizeOoxmlAsync(string ooxml, NakdanOptions opts, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            if (opts == null) opts = new NakdanOptions();

            XDocument doc = XDocument.Parse(ooxml);

            // Build a fast lookup for ignored styles (lowercase)
            var ignored = new HashSet<string>(
                (opts.IgnoredStyles ?? Enumerable.Empty<string>())
                    .Select(s => s.ToLowerInvariant()),
                StringComparer.OrdinalIgnoreCase);

            // Collect runs from w:body only, filtered by style
            List<RunInfo> runs = CollectRuns(doc, ignored);
            if (runs.Count == 0) return ooxml;

            List<Token> tokens = BuildTokenStream(runs);
            
            if (tokens.Count == 0) return ooxml;

            cancellationToken.ThrowIfCancellationRequested();
            
            List<List<Token>> chunks = ChunkTokens(tokens, DictaMaxChars);
            

            // Call Dicta for all chunks in parallel
            string genre = opts.Genre.ToString().ToLowerInvariant();
            Task<string>[] tasks = chunks
                .Select(c => CallDictaAsync(TokensToPlainText(c), genre, cancellationToken))
                .ToArray();

            string[] vowelizedChunks = await Task.WhenAll(tasks);

            cancellationToken.ThrowIfCancellationRequested();
            
            for (int ci = 0; ci < chunks.Count; ci++)
                FillVowels(chunks[ci], vowelizedChunks[ci]);

            WriteBackToRuns(tokens, runs);

            return doc.ToString(SaveOptions.DisableFormatting);
        }

        // ════════════════════════════════════════════════════════
        //  STEP 2 — collect runs scoped to w:body,
        //           skipping paragraphs whose style is ignored
        // ════════════════════════════════════════════════════════
        private List<RunInfo> CollectRuns(XDocument doc, HashSet<string> ignored)
        {
            var runs = new List<RunInfo>();
            int idx  = 0;

            // Scope to w:body — avoids styles.xml / numbering.xml runs
            XElement body = doc.Descendants(W + "body").FirstOrDefault();
            if (body == null) return runs;

            foreach (XElement para in body.Descendants(W + "p"))
            {
                // Resolve paragraph style name
                string styleName = GetParaStyleName(para);

                // Skip entire paragraph if its style is in the ignore list
                if (ignored.Count > 0 &&
                    ignored.Contains(styleName.ToLowerInvariant()))
                    continue;

                foreach (XElement r in para.Descendants(W + "r"))
                {
                    XElement t = r.Element(W + "t");
                    if (t == null) { idx++; continue; }

                    runs.Add(new RunInfo
                    {
                        Index     = idx++,
                        Element   = r,
                        TextEl    = t,
                        OrigText  = t.Value,
                        StyleName = styleName
                    });
                }
            }
            return runs;
        }

        /// <summary>
        /// Reads w:pPr/w:pStyle/@w:val from the paragraph element.
        /// Falls back to empty string if none set (Normal / default style).
        /// </summary>
        private string GetParaStyleName(XElement para)
        {
            return para
                .Element(W + "pPr")
                ?.Element(W + "pStyle")
                ?.Attribute(W + "val")
                ?.Value
                ?? string.Empty;
        }

        // ════════════════════════════════════════════════════════
        //  STEP 3 — build token stream
        // ════════════════════════════════════════════════════════
        private List<Token> BuildTokenStream(List<RunInfo> runs)
        {
            var tokens = new List<Token>();
            foreach (RunInfo run in runs)
            {
                string stripped = StripNikkud(run.OrigText);
                int pos = 0;
                foreach (char c in stripped)
                    tokens.Add(new Token
                    {
                        Base        = c,
                        VowelsAfter = string.Empty,
                        RunIndex    = run.Index,
                        PosInRun    = pos++
                    });
            }
            return tokens;
        }

        // ════════════════════════════════════════════════════════
        //  STEP 4 — chunk at word boundaries
        // ════════════════════════════════════════════════════════
        private List<List<Token>> ChunkTokens(List<Token> tokens, int maxChars)
        {
            var chunks  = new List<List<Token>>();
            var current = new List<Token>();
            int len     = 0;

            foreach (Token tok in tokens)
            {
                if (len >= maxChars)
                {
                    // Walk back to last whitespace
                    int rb = current.Count - 1;
                    while (rb > 0 && !char.IsWhiteSpace(current[rb].Base)) rb--;

                    if (rb > 0)
                    {
                        var overflow = current.GetRange(rb + 1, current.Count - rb - 1);
                        current.RemoveRange(rb + 1, current.Count - rb - 1);
                        chunks.Add(current);
                        current = overflow;
                        len     = current.Count;
                    }
                    else
                    {
                        chunks.Add(current);
                        current = new List<Token>();
                        len     = 0;
                    }
                }
                current.Add(tok);
                len++;
            }

            if (current.Count > 0) chunks.Add(current);
            return chunks;
        }

        // ════════════════════════════════════════════════════════
        //  STEP 5 — call Dicta
        // ════════════════════════════════════════════════════════
        private async Task<string> CallDictaAsync(string plainText, string genre, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(plainText)) return plainText;

            var payload = new
            {
                task            = "nakdan",
                data            = plainText,
                genre,
                addmorph        = true,
                keepmetagim     = true,
                useTokenization = true,
                keepqq          = false,
                nodageshdefmem  = false,
                patachma        = false,
                matchpartial    = true,
                userData        = "gave permission"
            };

            string body = JsonSerializer.Serialize(payload);
            Exception lastError = null;

            foreach (string url in DictaEndpoints)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var content  = new StringContent(body, Encoding.UTF8, "application/json");
                    var response = await _http.PostAsync(url, content, cancellationToken);
                    response.EnsureSuccessStatusCode();

                    string   json = await response.Content.ReadAsStringAsync();
                    JsonNode root = JsonNode.Parse(json);
                    return ExtractNiqqud(root?["data"]?.AsArray());
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }

            throw new Exception("Dicta API unavailable: " + lastError?.Message, lastError);
        }

        private string ExtractNiqqud(JsonArray data)
        {
            if (data == null) return string.Empty;
            var sb = new StringBuilder();

            foreach (JsonNode item in data)
            {
                JsonArray options = item?["nakdan"]?["options"]?.AsArray();
                if (options != null && options.Count > 0)
                {
                    string w = options[0]?["w"]?.GetValue<string>() ?? string.Empty;
                    w = w.Replace("|", "").Replace(Meteg.ToString(), "");
                    sb.Append(w);
                }
                else
                {
                    string fallback = item?["nakdan"]?["word"]?.GetValue<string>()
                                   ?? item?["str"]?.GetValue<string>()
                                   ?? string.Empty;
                    sb.Append(fallback);
                }
            }
            return sb.ToString();
        }

        // ════════════════════════════════════════════════════════
        //  STEP 6 — fill vowels into token stream
        // ════════════════════════════════════════════════════════
        private void FillVowels(List<Token> tokens, string vowelized)
        {
            int tokenIdx         = 0;
            int lastBaseTokenIdx = -1;

            foreach (char c in vowelized)
            {
                if (IsNikkud(c))
                {
                    if (lastBaseTokenIdx >= 0)
                        tokens[lastBaseTokenIdx].VowelsAfter += c;
                }
                else
                {
                    if (tokenIdx < tokens.Count)
                    {
                        lastBaseTokenIdx = tokenIdx;
                        tokenIdx++;
                    }
                }
            }
        }

        // ════════════════════════════════════════════════════════
        //  STEP 7 — write back to <w:t> elements
        // ════════════════════════════════════════════════════════
        private void WriteBackToRuns(List<Token> tokens, List<RunInfo> runs)
        {
            var runLookup = runs.ToDictionary(r => r.Index);

            foreach (var group in tokens.GroupBy(t => t.RunIndex))
            {
                if (!runLookup.TryGetValue(group.Key, out RunInfo run)) continue;

                var sb = new StringBuilder();
                foreach (Token tok in group.OrderBy(t => t.PosInRun))
                {
                    sb.Append(tok.Base);
                    sb.Append(tok.VowelsAfter);
                }

                string newText = sb.ToString();
                if (newText == run.OrigText) continue;

                run.TextEl.Value = newText;

                if (newText.Length > 0 &&
                    (newText[0] == ' ' || newText[newText.Length - 1] == ' '))
                    run.TextEl.SetAttributeValue(XNamespace.Xml + "space", "preserve");
            }
        }

        // ════════════════════════════════════════════════════════
        //  HELPERS
        // ════════════════════════════════════════════════════════
        private static bool IsNikkud(char c)
            => (c >= NikkudFirst && c <= NikkudLast)
            || c == ShinDot
            || c == ShinDotSin;

        private static string StripNikkud(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                if (!IsNikkud(c)) sb.Append(c);
            return sb.ToString();
        }

        private static string TokensToPlainText(List<Token> tokens)
        {
            var sb = new StringBuilder(tokens.Count);
            foreach (Token t in tokens) sb.Append(t.Base);
            return sb.ToString();
        }
    }
}
