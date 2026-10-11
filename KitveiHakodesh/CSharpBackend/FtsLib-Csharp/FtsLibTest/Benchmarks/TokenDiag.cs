using FtsLib.Tokenization;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// Malformation audit of tokenizer output over the seforim corpus.
    /// Drives the REAL internal <see cref="TokenStream"/> (exact parity with the indexer),
    /// counts every normalized term, collects anomalous tokens with source line id + raw span.
    ///
    /// Emits TWO reports:
    ///   • tokdiag_source_he.html  — DB/source anomalies, HEBREW (RTL), REVIEW-ONLY (no auto-drop).
    ///   • tokdiag_tokenizer_en.html — tokenizer faults, ENGLISH, with fix analysis.
    ///
    /// Lesson (learned repeatedly): structural rules cannot tell garbage from legitimate rare
    /// Hebrew scholarship — gematria (ךב=22, אבגדה letter-sums), Sefirot alef-bet groupings,
    /// roshei-teivot, Judeo-Arabic (Tafsir). So pure-Hebrew tokens are REVIEW-ONLY. lexical.db
    /// (a large morphological lexicon) is used to RESCUE real tokens and to raise confidence
    /// when a suggested correction is itself a known lexicon form — never to condemn (absence of
    /// a token from the lexicon does NOT imply garbage: ~40% of common corpus terms are absent).
    ///
    /// Usage:  FtsLibTest.exe tokdiag [rowsPerCategory=80]
    /// </summary>
    internal static class TokenDiag
    {
        const int EXAMPLES = 3, WIN = 45, PART = 200;
        const string LEXICAL_DB = @"C:\Users\Public\Documents\Dictionary\Backup\lexical.db";
        static readonly char[] Finals = { 'ך', 'ם', 'ן', 'ף', 'ץ' };
        static char Fold(char c) => c switch { 'ך'=>'כ','ם'=>'מ','ן'=>'נ','ף'=>'פ','ץ'=>'צ', _=>c };
        static readonly HashSet<string> EntityWords = new(StringComparer.Ordinal)
            { "amp","nbsp","quot","ensp","emsp","shy","lt","gt","apos","zwnj","zwj","ndash","mdash","laquo","raquo","hellip","middot","deg","thinsp" };
        static HashSet<string> LEX = new(StringComparer.Ordinal);   // normalized known Hebrew forms

        sealed class Ex { public long LineId; public string HeRef; public string Book; public string SnippetHtml; }
        sealed class Mal { public string Term; public long Occ; public long QuoteOcc, ContigOcc; public readonly List<Ex> Examples = new(); }

        public static void Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            int rowsPerCat = args.Length > 1 && int.TryParse(args[1], out var rp) ? rp : 80;
            string db = BuildTest.ResolveDbPath();
            if (!System.IO.File.Exists(db)) { Console.WriteLine($"seforim.db not found at {db}"); return; }
            Console.WriteLine($"tokdiag: {db}");
            LoadLexicon();

            var bookTitle = new Dictionary<long, string>();
            using (var c = OpenRo(db)) { var cmd = c.CreateCommand(); cmd.CommandText = "SELECT id,title FROM book";
                using var r = cmd.ExecuteReader(); while (r.Read()) bookTitle[r.GetInt64(0)] = r.IsDBNull(1) ? "" : r.GetString(1); }

            var cnt = new Dictionary<string, int>(1 << 21, StringComparer.Ordinal);
            var mal = new Dictionary<string, Mal>(StringComparer.Ordinal);
            var ts = new TokenStream();
            long scanned = 0; var sw = System.Diagnostics.Stopwatch.StartNew();

            using (var c = OpenRo(db))
            {
                var cmd = c.CreateCommand(); cmd.CommandText = "SELECT id, content, heRef, bookId FROM line";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    if (r.IsDBNull(1)) continue;
                    long lineId = r.GetInt64(0); string content = r.GetString(1);
                    string heRef = r.IsDBNull(2) ? "" : r.GetString(2);
                    long bookId = r.IsDBNull(3) ? -1 : r.GetInt64(3);
                    scanned++;
                    foreach (var tok in ts.Tokenize(content))
                    {
                        string w = tok.Normalized;
                        cnt.TryGetValue(w, out int cc); cnt[w] = cc + 1;
                        if (!IsAnomalousShape(w)) continue;
                        if (IsPureHebrew(w) && LEX.Contains(w)) continue;   // rescue: it's a real lexicon form
                        int rs = tok.RawStart, re = Math.Min(tok.RawEnd, content.Length);
                        string span = content.Substring(rs, re - rs);
                        bool quote = span.IndexOfAny(new[] { '"', '\'', '׳', '״' }) >= 0;
                        bool amp = span.IndexOf('&') >= 0 || (rs > 0 && content[rs - 1] == '&');
                        if (!mal.TryGetValue(w, out var m)) { m = new Mal { Term = w }; mal[w] = m; }
                        m.Occ++; if (quote) m.QuoteOcc++; else if (!amp) m.ContigOcc++;
                        if (m.Examples.Count < EXAMPLES)
                            m.Examples.Add(new Ex { LineId = lineId, HeRef = heRef,
                                Book = bookId >= 0 && bookTitle.TryGetValue(bookId, out var bt) ? bt : "",
                                SnippetHtml = Snippet(content, rs, tok.RawEnd) });
                    }
                    if ((scanned & 0x3FFFF) == 0) Console.WriteLine($"  {scanned:N0} lines, {mal.Count:N0} anomalies, {sw.Elapsed.TotalSeconds:F0}s");
                }
            }
            sw.Stop();
            Console.WriteLine($"scanned {scanned:N0} lines in {sw.Elapsed.TotalSeconds:F0}s; {mal.Count:N0} distinct anomalous terms; {cnt.Count:N0} total terms");
            BuildReports(mal, cnt, scanned, rowsPerCat);
        }

        static void LoadLexicon()
        {
            if (!System.IO.File.Exists(LEXICAL_DB)) { Console.WriteLine($"WARN: lexical.db not found at {LEXICAL_DB} — running without lexicon oracle"); return; }
            using var c = OpenRo(LEXICAL_DB);
            foreach (var tbl in new[] { "base", "surface", "variant" })
            {
                var cmd = c.CreateCommand(); cmd.CommandText = $"SELECT value FROM {tbl}";
                using var r = cmd.ExecuteReader();
                while (r.Read()) { if (r.IsDBNull(0)) continue; string n = LexNorm(r.GetString(0)); if (n.Length >= 2) LEX.Add(n); }
            }
            Console.WriteLine($"lexicon: {LEX.Count:N0} normalized known forms");
        }
        static string LexNorm(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s) { if (c >= 'א' && c <= 'ת') sb.Append(c); else if (c >= 'a' && c <= 'z') sb.Append(c); else if (c >= 'A' && c <= 'Z') sb.Append((char)(c | 32)); }
            return sb.ToString();
        }

        static bool IsPureHebrew(string w) { foreach (char c in w) if (!(c >= 'א' && c <= 'ת')) return false; return w.Length > 0; }

        static bool IsAnomalousShape(string w)
        {
            bool lat = false, heb = false, midFinal = false;
            for (int i = 0; i < w.Length; i++)
            {
                char c = w[i];
                if (c >= 'a' && c <= 'z') lat = true;
                else if (c >= 'א' && c <= 'ת') { heb = true; if (Array.IndexOf(Finals, c) >= 0 && i != w.Length - 1) midFinal = true; }
            }
            if (lat && heb) return true;
            if (lat && !heb) return false;
            if (midFinal) return true;
            if (w.Length >= 3 && w.All(x => x == w[0])) return true;
            return false;
        }

        // An anomaly gets INDEPENDENT source-side and tokenizer-side assessments; a mixed-script
        // token is usually BOTH a source malformation (bad entity / OCR bleed in the DB) AND a
        // tokenizer weakness — so it appears in both reports. report: "TOK" (English) or "SRC" (Hebrew).
        static List<(string report, string bucket, string label, string note)> Classify(Mal m)
        {
            var res = new List<(string, string, string, string)>();
            string w = m.Term;
            int latLen = w.Count(c => c >= 'a' && c <= 'z');
            bool heb = w.Any(c => c >= 'א' && c <= 'ת');
            if (latLen > 0)
            {
                if (LatinRuns(w).Any(EntityWords.Contains))
                {
                    res.Add(("TOK", "T1", "HTML entity residue", $"real text: {StripLatin(w)}"));
                    res.Add(("SRC", "S2", "שארית ישות HTML במקור (למשל &amp ללא ;)", $"טקסט אמיתי: {StripLatin(w)}"));
                }
                else if (heb && latLen == 1)
                {
                    res.Add(("TOK", "T2", "Stray single Latin letter glued to Hebrew", $"Hebrew: {StripLatin(w)}"));
                    res.Add(("SRC", "S3", "אות לטינית זרה דבוקה (OCR) — במקור", $"עברית: {StripLatin(w)}"));
                }
                else
                {
                    res.Add(("TOK", "T3", "Hebrew+Latin script merge", SplitScripts(w)));
                    if (heb) res.Add(("SRC", "S4", "מיזוג עברית+לטינית — לבדיקה (OCR או תוכן דו-לשוני לגיטימי)", SplitScripts(w)));
                }
                return res;
            }
            // pure Hebrew → source report only
            if (w.Length >= 3 && w.All(x => x == w[0]))
            { res.Add(("SRC", "S7", "אות בודדת חוזרת", "לבדיקה (לרוב ר\"ת/גימטריה כמו קק\"ק, ממ\"מ — או OCR)")); return res; }
            // mid-word final: gershayim/geresh in source ⇒ gematria/abbrev (legitimate);
            // contiguous letters ⇒ genuine OCR letter error.
            string fw = FoldWord(w);
            var sj = SplitJoin(w);
            bool viaQuote = m.QuoteOcc > 0 && m.QuoteOcc >= m.ContigOcc;   // gershayim is the dominant form ⇒ gematria/abbrev
            if (viaQuote)
                res.Add(("SRC", "S6", "אות סופית בתוך ר\"ת/גימטריה (לגיטימי — מופיע עם גרש/גרשיים)",
                         fw != null ? $"מקביל: {fw}" : "גימטריה/ר\"ת — כנראה תקין"));
            else if (fw != null)
                res.Add(("SRC", "S1", "אות סופית באמצע מילה רציפה — כנראה שגיאת OCR (יעד קיים במילון)", $"תיקון: {fw} ✓מילון"));
            else if (sj != null)
                res.Add(("SRC", "S1", "אות סופית באמצע מילה רציפה — כנראה שגיאת OCR (יעד קיים במילון)", $"פיצול: {sj.Value.l} | {sj.Value.r} ✓מילון"));
            else
                res.Add(("SRC", "S5", "אות סופית באמצע — לבדיקה (ללא התאמה במילון)", "ייתכן גימטריה/ר\"ת/ארמית/צירוף לגיטימי"));
            return res;
        }

        // fold target must be a REAL lexicon form (high confidence), else corpus-frequent
        static string FoldWord(string w)
        {
            var sb = new StringBuilder(w.Length); bool ch = false;
            for (int i = 0; i < w.Length; i++) { char f = i != w.Length - 1 ? Fold(w[i]) : w[i]; if (f != w[i]) ch = true; sb.Append(f); }
            if (!ch) return null; string f2 = sb.ToString();
            return LEX.Contains(f2) ? f2 : null;
        }
        static (string l, string r)? SplitJoin(string w)
        {
            for (int i = 0; i < w.Length - 1; i++)
                if (Array.IndexOf(Finals, w[i]) >= 0)
                { string l = w[..(i + 1)], r = w[(i + 1)..];
                  if (l.Length >= 2 && r.Length >= 2 && LEX.Contains(l) && LEX.Contains(r)) return (l, r); }
            return null;
        }
        static List<string> LatinRuns(string w) { var res = new List<string>(); var sb = new StringBuilder();
            foreach (char c in w) { if (c >= 'a' && c <= 'z') sb.Append(c); else if (sb.Length > 0) { res.Add(sb.ToString()); sb.Clear(); } }
            if (sb.Length > 0) res.Add(sb.ToString()); return res; }
        static string StripLatin(string w) => new string(w.Where(c => !(c >= 'a' && c <= 'z')).ToArray());
        static string SplitScripts(string w) => $"{new string(w.Where(c => c >= 'א' && c <= 'ת').ToArray())} + {new string(w.Where(c => c >= 'a' && c <= 'z').ToArray())}";

        static string Snippet(string content, int rs, int re)
        {
            int from = Math.Max(0, rs - WIN), to = Math.Min(content.Length, re + WIN);
            var sb = new StringBuilder(); if (from > 0) sb.Append('…');
            AppendStripped(sb, content, from, rs); sb.Append("<mark>");
            AppendStripped(sb, content, rs, Math.Min(re, content.Length)); sb.Append("</mark>");
            AppendStripped(sb, content, Math.Min(re, content.Length), to);
            if (to < content.Length) sb.Append('…'); return sb.ToString();
        }
        static void AppendStripped(StringBuilder sb, string s, int from, int to)
        {
            bool inTag = false;
            for (int k = from - 1; k >= 0; k--) { if (s[k] == '>') break; if (s[k] == '<') { inTag = true; break; } }
            for (int i = from; i < to; i++) { char c = s[i];
                if (inTag) { if (c == '>') inTag = false; continue; }
                if (c == '<') { inTag = true; continue; }
                switch (c) { case '&': sb.Append("&amp;"); break; case '<': sb.Append("&lt;"); break; case '>': sb.Append("&gt;"); break; default: sb.Append(c); break; } }
        }
        static string Esc(string s) { if (string.IsNullOrEmpty(s)) return ""; var sb = new StringBuilder(s.Length);
            foreach (char c in s) switch (c) { case '&': sb.Append("&amp;"); break; case '<': sb.Append("&lt;"); break; case '>': sb.Append("&gt;"); break; case '"': sb.Append("&quot;"); break; default: sb.Append(c); break; } return sb.ToString(); }

        static void BuildReports(Dictionary<string, Mal> mal, Dictionary<string, int> cnt, long scanned, int rowsPerCat)
        {
            var groups = new Dictionary<string, (string report, string label, List<(Mal m, string note)> rows)>();
            var order = new List<string>();
            foreach (var m in mal.Values)
                foreach (var (rep, bk, label, note) in Classify(m))
                {
                    if (!groups.TryGetValue(bk, out var g)) { g = (rep, label, new()); groups[bk] = g; order.Add(bk); }
                    g.rows.Add((m, note));
                }
            string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp");

            // ── SOURCE report (Hebrew, RTL) ──
            var he = new StringBuilder();
            he.Append("<div class='wrap' dir='rtl'>");
            he.Append("<h1>דו\"ח חריגות מקור (מאגר) — לבדיקה אנושית</h1>");
            long srcTerms = order.Where(b => groups[b].report == "SRC").Sum(b => groups[b].rows.Count);
            he.Append($"<p class='meta'>נסרקו {scanned:N0} שורות · {srcTerms:N0} טוקנים חריגים · מקור: seforim.db · מילון: lexical.db ({LEX.Count:N0} צורות)</p>");
            he.Append("<p class='warn'>⚠ <b>לבדיקה בלבד — אין למחוק אוטומטית.</b> טוקנים רבים הנראים \"משובשים\" הם לגיטימיים: גימטריה (ךב=22), צירופי א\"ב לפי ספירות, ר\"ת, וארמית/ערבית-יהודית. \"כנראה שגיאת OCR\" = אות סופית בתוך מילה רציפה שהתיקון שלה קיים במילון (הכי ניתן לפעולה). קטגוריות שארית-ישות / אות-לטינית / מיזוג-כתבים הן <b>מקורן במאגר</b> ומופיעות גם בדו\"ח הטוקנייזר (ניתן לתקן בשני הצדדים).</p>");
            EmitSummary(he, order, groups, "SRC", he: true);
            foreach (var bk in order.Where(b => groups[b].report == "SRC").OrderBy(b => b))
                EmitTable(he, groups[bk], rowsPerCat, he: true);
            he.Append("</div>");
            string p1 = System.IO.Path.Combine(dir, "tokdiag_source_he.html");
            System.IO.File.WriteAllText(p1, Page("דו\"ח חריגות מקור", he.ToString(), rtl: true), new UTF8Encoding(false));

            // ── TOKENIZER report (English, LTR) ──
            var en = new StringBuilder();
            en.Append("<div class='wrap'>");
            en.Append("<h1>Tokenizer Fault Report — fix candidates</h1>");
            long tokTerms = order.Where(b => groups[b].report == "TOK").Sum(b => groups[b].rows.Count);
            en.Append($"<p class='meta'>Scanned {scanned:N0} lines · {tokTerms:N0} anomalous tokens · exact parity via internal TokenStream.</p>");
            en.Append("<div class='fixbox'><h2>Root causes &amp; proposed fixes</h2><ul>"
                + "<li><b>HTML entity residue</b> (e.g. <code>בשבתamp</code>): a malformed entity such as <code>&amp;amp</code> with no trailing <code>;</code> leaks the letters <code>amp/nbsp</code> into the adjacent word — <code>HtmlWordScanner</code> skips only the <code>&amp;</code> and lets the rest through. <b>Fix:</b> treat <code>&amp;</code> as a word boundary (flush), and/or recognize known entity names even without a <code>;</code>.</li>"
                + "<li><b>Stray single Latin letter</b> (e.g. <code>bדה</code>): one OCR/artifact Latin char fused to Hebrew. <b>Fix:</b> emit a token boundary at a Hebrew↔Latin transition (the single letter then drops as length&lt;2).</li>"
                + "<li><b>Hebrew+Latin script merge</b> (e.g. <code>הou</code>=\"the OU\"): legitimate bilingual content indexed as one unsearchable token. <b>Same fix</b> (script-boundary split) makes the Latin part searchable. NOTE: this one is a recall improvement, not garbage.</li></ul>"
                + "<p class='note-en'><b>Script-boundary split is an agreed upgrade.</b> All three are addressed in <code>FtsLib/Tokenization/HtmlWordScanner.cs</code> (the <code>'&amp;'</code> branch and the letter-append loop); requires a full index rebuild. NOTE: most of these also originate in the source data (a bad <code>&amp;</code>, an OCR Latin char) and are listed again in the Hebrew source report — they can be fixed on either side.</p></div>");
            EmitSummary(en, order, groups, "TOK", he: false);
            foreach (var bk in order.Where(b => groups[b].report == "TOK").OrderBy(b => b))
                EmitTable(en, groups[bk], rowsPerCat, he: false);
            en.Append("</div>");
            string p2 = System.IO.Path.Combine(dir, "tokdiag_tokenizer_en.html");
            System.IO.File.WriteAllText(p2, Page("Tokenizer Fault Report", en.ToString(), rtl: false), new UTF8Encoding(false));

            // ── CSV companions (ALL rows, no cap; UTF-8 BOM for Excel) ──
            string c1 = System.IO.Path.Combine(dir, "tokdiag_source_he.csv");
            string c2 = System.IO.Path.Combine(dir, "tokdiag_tokenizer_en.csv");
            WriteCsv(c1, order, groups, "SRC");
            WriteCsv(c2, order, groups, "TOK");

            Console.WriteLine($"source report → {p1}");
            Console.WriteLine($"tokenizer report → {p2}");
            Console.WriteLine($"source csv → {c1}");
            Console.WriteLine($"tokenizer csv → {c2}");
            foreach (var p in new[] { p1, p2, c1, c2 }) { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(p) { UseShellExecute = true }); } catch { } }
        }

        static void EmitSummary(StringBuilder sb, List<string> order, Dictionary<string, (string report, string label, List<(Mal m, string note)> rows)> groups, string rep, bool he)
        {
            sb.Append(he ? "<h2>סיכום</h2><table class='sum'><thead><tr><th>קטגוריה</th><th>טוקנים</th><th>מופעים</th></tr></thead><tbody>"
                         : "<h2>Summary</h2><table class='sum'><thead><tr><th>Category</th><th>Tokens</th><th>Occurrences</th></tr></thead><tbody>");
            foreach (var bk in order.Where(b => groups[b].report == rep).OrderBy(b => b))
            { var g = groups[bk]; long occ = g.rows.Sum(x => x.m.Occ);
              sb.Append($"<tr><td>{Esc(g.label)}</td><td>{g.rows.Count:N0}</td><td>{occ:N0}</td></tr>"); }
            sb.Append("</tbody></table>");
        }

        static void EmitTable(StringBuilder sb, (string report, string label, List<(Mal m, string note)> rows) g, int rowsPerCat, bool he)
        {
            g.rows.Sort((a, b) => b.m.Occ.CompareTo(a.m.Occ));
            string tokCol = he ? "טוקנים" : "tokens";
            sb.Append($"<h3>{Esc(g.label)} <span class='cnt'>({g.rows.Count:N0} {tokCol})</span></h3>");
            sb.Append(he ? "<table class='data'><thead><tr><th>מילה</th><th>הערה / אפשרות</th><th>תדירות</th><th>הקשר מודגש</th><th>מקור</th></tr></thead><tbody>"
                         : "<table class='data'><thead><tr><th>token</th><th>note</th><th>freq</th><th>context (highlighted)</th><th>source</th></tr></thead><tbody>");
            string lineW = he ? "שורה" : "line";
            foreach (var (m, note) in g.rows.Take(rowsPerCat))
            {
                var ex = m.Examples.Count > 0 ? m.Examples[0] : null;
                string snip = ex != null ? $"<span class='snip' dir='rtl'>{ex.SnippetHtml}</span>" : "";
                string trace = ex != null ? $"{lineW} {ex.LineId}"
                    + (string.IsNullOrEmpty(ex.HeRef) ? "" : $"<br><small dir='rtl'>{Esc(ex.HeRef)}</small>")
                    + (string.IsNullOrEmpty(ex.Book) ? "" : $"<br><small class='book' dir='rtl'>{Esc(ex.Book)}</small>") : "";
                sb.Append($"<tr><td class='term' dir='rtl'>{Esc(m.Term)}</td><td class='note' dir='rtl'>{Esc(note)}</td><td class='freq'>{m.Occ:N0}</td><td>{snip}</td><td class='trace'>{trace}</td></tr>");
            }
            sb.Append("</tbody></table>");
        }

        static string Page(string title, string body, bool rtl) =>
            $"<!doctype html><html lang='{(rtl ? "he" : "en")}' dir='{(rtl ? "rtl" : "ltr")}'><head><meta charset='utf-8'><title>{Esc(title)}</title><style>"
            + "body{font-family:'Segoe UI','Arial Hebrew',Arial,sans-serif;background:#f6f7f9;color:#1a1a1a;margin:0;padding:24px;line-height:1.5}"
            + ".wrap{max-width:1200px;margin:0 auto}h1{font-size:24px}h2{margin-top:30px;padding-bottom:6px;border-bottom:2px solid #ccc}"
            + "h3{margin-top:22px;color:#333}.cnt{font-weight:normal;color:#888;font-size:14px}.meta{color:#555}"
            + ".warn{background:#fff6e5;border:1px solid #e8c072;padding:10px 14px;border-radius:6px}"
            + ".fixbox{background:#fff;border:1px solid #ddd;border-radius:6px;padding:6px 22px;margin:10px 0}.fixbox li{margin:8px 0}.note-en{color:#555;font-size:14px}"
            + "code{background:#eee;padding:1px 4px;border-radius:3px;font-family:Consolas,monospace}"
            + "table{width:100%;border-collapse:collapse;background:#fff;margin:8px 0 20px;box-shadow:0 1px 3px rgba(0,0,0,.08)}"
            + "th,td{border:1px solid #e2e2e2;padding:7px 10px;vertical-align:top;text-align:start}"
            + "thead th{background:#eef1f5;position:sticky;top:0;font-size:13px}"
            + ".term{font-size:18px;font-weight:bold;white-space:nowrap}.note{font-size:14px;color:#444}"
            + ".freq{text-align:center;color:#666}.snip{font-size:15px}mark{background:#ffe14d;padding:0 2px;border-radius:2px;font-weight:bold}"
            + ".trace{font-size:12px;color:#555;white-space:nowrap}.trace .book{color:#1c4a8a}table.sum td,table.sum th{white-space:nowrap}"
            + "</style></head><body>" + body + "</body></html>";

        static void WriteCsv(string path, List<string> order, Dictionary<string, (string report, string label, List<(Mal m, string note)> rows)> groups, string rep)
        {
            var sb = new StringBuilder();
            sb.Append("category,term,suggestion,freq,line_id,heRef,book,context\r\n");
            foreach (var bk in order.Where(b => groups[b].report == rep).OrderBy(b => b))
            {
                var g = groups[bk];
                foreach (var (m, note) in g.rows.OrderByDescending(x => x.m.Occ))
                {
                    var ex = m.Examples.Count > 0 ? m.Examples[0] : null;
                    string ctx = ex != null ? HtmlToPlain(ex.SnippetHtml) : "";
                    sb.Append(string.Join(",", new[] {
                        CsvEsc(g.label), CsvEsc(m.Term), CsvEsc(note), m.Occ.ToString(),
                        ex != null ? ex.LineId.ToString() : "", CsvEsc(ex?.HeRef ?? ""), CsvEsc(ex?.Book ?? ""), CsvEsc(ctx)
                    }));
                    sb.Append("\r\n");
                }
            }
            System.IO.File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }
        static string CsvEsc(string s)
        {
            s ??= "";
            return s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }
        static string HtmlToPlain(string html)
        {
            string s = (html ?? "").Replace("<mark>", "«").Replace("</mark>", "»");
            var sb = new StringBuilder(s.Length); bool inTag = false;
            foreach (char c in s) { if (c == '<') { inTag = true; continue; } if (c == '>') { inTag = false; continue; } if (!inTag) sb.Append(c); }
            return sb.ToString().Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"").Replace("&amp;", "&");
        }

        static SqliteConnection OpenRo(string path) { var c = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Cache=Shared"); c.Open(); return c; }
    }
}
