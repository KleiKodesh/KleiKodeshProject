using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace HebrewNakdan
{
    internal class Token
    {
        public char Base;
        public string VowelsAfter;
        public int RunIndex;
        public int PosInRun;
    }

    internal class RunInfo
    {
        public int Index;
        public XElement Element;
        public XElement TextEl;
        public string OrigText;
    }

    public enum DictaGenre { Modern, Poetry, Bible, Rabbinic }

    public class HebrewNakdan
    {
        private const char HebrewLetterFirst = '\u05D0';
        private const char HebrewLetterLast = '\u05EA';
        private const char NikkudFirst = '\u05B0';
        private const char NikkudLast = '\u05C7';
        private const char Meteg = '\u05BD';
        private const char ShinDot = '\u05C1';
        private const char ShinDotSin = '\u05C2';

        private static readonly string[] DictaEndpoints =
        {
            "https://nakdan-u1-0.loadbalancer.dicta.org.il/api",
            "https://nakdan-5-1.loadbalancer.dicta.org.il/api"
        };
        private const int DictaMaxChars = 5000;
        private const int HttpTimeoutSec = 30;

        private static readonly XNamespace W =
            "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(HttpTimeoutSec)
        };

        private readonly string _genre;

        public HebrewNakdan(DictaGenre genre = DictaGenre.Modern)
        {
            _genre = genre.ToString().ToLowerInvariant();
        }

        // ── PUBLIC ENTRY POINT ──────────────────────────────────
        // Pass Document.WordOpenXML in, get back the modified XML.
        public async Task<string> VowelizeOoxmlAsync(string ooxml)
        {
            XDocument doc = XDocument.Parse(ooxml);

            List<RunInfo> runs = CollectRuns(doc);
            if (runs.Count == 0) return ooxml;

            List<Token> tokens = BuildTokenStream(runs);
            if (tokens.Count == 0) return ooxml;

            List<List<Token>> chunks = ChunkTokens(tokens, DictaMaxChars);

            // All chunks in parallel
            Task<string>[] tasks = chunks
                .Select(chunk => CallDictaAsync(TokensToPlainText(chunk)))
                .ToArray();

            string[] vowelizedChunks = await Task.WhenAll(tasks);

            for (int ci = 0; ci < chunks.Count; ci++)
                FillVowels(chunks[ci], vowelizedChunks[ci]);

            WriteBackToRuns(tokens, runs);

            return doc.ToString(SaveOptions.DisableFormatting);
        }

        // ── STEP 2: collect runs scoped to w:body only ──────────
        private List<RunInfo> CollectRuns(XDocument doc)
        {
            var runs = new List<RunInfo>();
            int idx = 0;

            // w:body scope prevents touching runs in styles.xml,
            // numbering.xml etc. bundled inside WordOpenXML flat OPC.
            XElement body = doc.Descendants(W + "body").FirstOrDefault();
            if (body == null) return runs;

            foreach (XElement r in body.Descendants(W + "r"))
            {
                XElement t = r.Element(W + "t");
                if (t == null) { idx++; continue; }

                runs.Add(new RunInfo
                {
                    Index = idx++,
                    Element = r,
                    TextEl = t,
                    OrigText = t.Value
                });
            }
            return runs;
        }

        // ── STEP 3: build token stream ──────────────────────────
        private List<Token> BuildTokenStream(List<RunInfo> runs)
        {
            var tokens = new List<Token>();
            foreach (RunInfo run in runs)
            {
                string stripped = StripNikkud(run.OrigText);
                int pos = 0;
                foreach (char c in stripped)
                {
                    tokens.Add(new Token
                    {
                        Base = c,
                        VowelsAfter = string.Empty,
                        RunIndex = run.Index,
                        PosInRun = pos++
                    });
                }
            }
            return tokens;
        }

        // ── STEP 4: chunk at word boundaries ───────────────────
        private List<List<Token>> ChunkTokens(List<Token> tokens, int maxChars)
        {
            var chunks = new List<List<Token>>();
            var current = new List<Token>();
            int currentLen = 0;

            for (int i = 0; i < tokens.Count; i++)
            {
                if (currentLen >= maxChars)
                {
                    int rollback = current.Count - 1;
                    while (rollback > 0 && !char.IsWhiteSpace(current[rollback].Base))
                        rollback--;

                    if (rollback > 0)
                    {
                        var overflow = current.GetRange(rollback + 1, current.Count - rollback - 1);
                        current.RemoveRange(rollback + 1, current.Count - rollback - 1);
                        chunks.Add(current);
                        current = overflow;
                        currentLen = current.Count;
                    }
                    else
                    {
                        chunks.Add(current);
                        current = new List<Token>();
                        currentLen = 0;
                    }
                }

                current.Add(tokens[i]);
                currentLen++;
            }

            if (current.Count > 0) chunks.Add(current);
            return chunks;
        }

        // ── STEP 5: call Dicta ──────────────────────────────────
        private async Task<string> CallDictaAsync(string plainText)
        {
            if (string.IsNullOrWhiteSpace(plainText)) return plainText;

            var payload = new
            {
                task = "nakdan",
                data = plainText,
                genre = _genre,
                addmorph = true,
                keepmetagim = true,
                useTokenization = true,
                keepqq = false,
                nodageshdefmem = false,
                patachma = false,
                matchpartial = true,
                userData = "gave permission"
            };

            string body = JsonSerializer.Serialize(payload);
            Exception lastError = null;

            foreach (string url in DictaEndpoints)
            {
                try
                {
                    var content = new StringContent(body, Encoding.UTF8, "application/json");
                    var response = await _http.PostAsync(url, content);
                    response.EnsureSuccessStatusCode();

                    string json = await response.Content.ReadAsStringAsync();
                    JsonNode root = JsonNode.Parse(json);
                    return ExtractNiqqud(root?["data"]?.AsArray());
                }
                catch (Exception ex) { lastError = ex; }
            }

            throw new Exception("Dicta API unavailable: " + lastError?.Message, lastError);
        }

        // Mirrors extractNiqqud() from index.html
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
                    w = w.Replace("|", "");
                    w = w.Replace(Meteg.ToString(), "");
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

        // ── STEP 6: fill vowels into token stream ───────────────
        private void FillVowels(List<Token> tokens, string vowelized)
        {
            int tokenIdx = 0;
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

        // ── STEP 7: write back to <w:t> elements ────────────────
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
                {
                    run.TextEl.SetAttributeValue(XNamespace.Xml + "space", "preserve");
                }
            }
        }

        // ── Helpers ─────────────────────────────────────────────
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