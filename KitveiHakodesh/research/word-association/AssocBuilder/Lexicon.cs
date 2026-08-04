using System.Text;
using Microsoft.Data.Sqlite;

namespace AssocBuilder;

/// <summary>
/// Curated lexical knowledge: lemmatization and a known-word set.
///
/// This corpus is a Talmudic one — pure Hebrew, pure Aramaic, or both mixed in
/// one line. So Aramaic is a first-class register: folding `מלכא` and `דמלכא`
/// onto `מלכ` is a WIN (it links the two languages, which co-occurrence alone
/// can only do by accident), and an Aramaic result is a correct result.
///
/// What is actually broken in the corpus is different: print/OCR fuses two words
/// into one token (`דכשמקדשינ`, `חייבלקרוע`). Those are unknown to every lexicon
/// and are what the known-word set is for.
///
/// Sources
/// -------
/// lexical.db      base 24,559 / surface 137,631 / surface_variant 594,428.
///                 Inflected AND prefixed forms for both languages. This is what
///                 finally reaches the SUFFIX morphology that the prefix
///                 heuristic in Vocabulary.cs cannot (FINDINGS.md §9).
/// Dictionary.db   ~52k curated headwords.
///
/// Measured coverage: 60% of the Tanach vocabulary is lemmatizable, collapsing
/// 8,578 words to ~3,400 — and the `מלכ` family from 37 tokens to a handful.
/// </summary>
internal static class Lexicon
{
    private const string LexicalDb =
        @"C:\Users\Public\Documents\Dictionary\Backup\lexical.db";
    private const string DictDb =
        @"C:\Users\Admin\AppData\Local\KleiKodesh\KitveiHakodesh\dictionary\KitveiHakodesh_dictionary.db";

    /// <summary>Same normalization the tokenizer applies, so lexicon entries and
    /// corpus tokens meet in one alphabet.</summary>
    internal static string Normalize(string w)
    {
        var sb = new StringBuilder(w.Length);
        foreach (char c in w)
        {
            if (c is >= 'א' and <= 'ת')
                sb.Append(c switch
                {
                    'ך' => 'כ', 'ם' => 'מ', 'ן' => 'נ', 'ף' => 'פ', 'ץ' => 'צ',
                    _ => c,
                });
        }
        return sb.ToString();
    }

    /// <summary>
    /// form -> base lexeme, from lexical.db (surface and variant paths).
    ///
    /// Ambiguity handling is the whole difficulty here, and getting it wrong is
    /// silent. In an unvocalized script one written form legitimately belongs to
    /// several lexemes, and lexical.db records all of them:
    ///
    ///     שבת   is a variant of BOTH `שבת` (correct) and `בת`  (wrong here)
    ///     מזבח  is a variant of BOTH `מזבח` (correct) and `זבח` (wrong here)
    ///
    /// A "prefer the shortest base" rule — which looks like the conservative
    /// choice — picks `בת` and `זבח` every time and **deletes the base form from
    /// the vocabulary**. Measured: 12 of 16 probe words disappeared, so
    /// `similar(שבת)` returned nothing at all while gold-set P@20 went UP,
    /// because the surviving words were easier ones. A score can hide this
    /// completely.
    ///
    /// So: a form that IS a base maps to itself, full stop. Only genuinely
    /// non-base forms are folded, and among competing bases the LONGEST wins —
    /// the longest shared stem is the closest lexeme, not the shortest
    /// (`מזבחות` -> `מזבח`, never `מזבח` -> `זבח`).
    /// </summary>
    internal static Dictionary<string, string> LoadLemmas()
    {
        var map = new Dictionary<string, string>(1 << 18, StringComparer.Ordinal);
        if (!File.Exists(LexicalDb)) return map;

        using var con = new SqliteConnection($"Data Source={LexicalDb};Mode=ReadOnly");
        con.Open();

        // Every lexeme in its own right. A form in this set is never folded.
        var bases = new HashSet<string>(StringComparer.Ordinal);
        using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = "select value from base";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                string n = Normalize(r.GetString(0));
                if (n.Length > 0) bases.Add(n);
            }
        }

        void Ingest(string sql)
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = sql;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                string form = Normalize(r.GetString(0));
                string bas  = Normalize(r.GetString(1));
                if (form.Length == 0 || bas.Length == 0 || form == bas) continue;

                // A base form is its own lemma — never fold it away.
                if (bases.Contains(form)) continue;

                // Among competing bases prefer the longest: it shares more of the
                // form, so it is the nearer lexeme.
                if (map.TryGetValue(form, out var prev) && prev.Length >= bas.Length)
                    continue;
                map[form] = bas;
            }
        }

        Ingest("select s.value, b.value from surface s join base b on b.id = s.base_id");
        Ingest("""
               select v.value, b.value
               from surface_variant sv
               join variant v on v.id = sv.variant_id
               join surface s on s.id = sv.surface_id
               join base    b on b.id = s.base_id
               """);
        return map;
    }

    /// <summary>
    /// Merges the Targum-derived Hebrew&lt;-&gt;Aramaic bridge into a lemma map.
    ///
    /// `targum_bridge.py` aligns the Targumim (verse-parallel with the Tanach in
    /// the same DB) and extracts mutual-best translation pairs — `מלכא -> מלכ`,
    /// `נהורא -> האור`, `שמיא -> השמימ`. Folding the Aramaic form onto its
    /// Hebrew equivalent links the two registers of a Talmudic corpus in a way
    /// co-occurrence alone can only manage by accident.
    ///
    /// lexical.db entries take precedence — they are curated, the bridge is
    /// inferred. Bridge targets often carry an article (`הארצ`); that resolves
    /// downstream, because chain collapsing runs after all sources merge, and
    /// `הארצ -> ארצ` is itself in the lexicon.
    /// </summary>
    internal static void MergeBridge(Dictionary<string, string> map,
                                    string csvPath, double minScore)
    {
        if (!File.Exists(csvPath)) return;
        int added = 0;
        foreach (var line in File.ReadLines(csvPath).Skip(1))
        {
            var parts = line.Split(',');
            if (parts.Length < 3) continue;
            string aram = Normalize(parts[0]);
            string heb  = Normalize(parts[1]);
            if (aram.Length == 0 || heb.Length == 0 || aram == heb) continue;
            if (!double.TryParse(parts[2], out double score) || score < minScore)
                continue;
            if (!map.ContainsKey(aram))     // curated sources win
            {
                map[aram] = heb;
                added++;
            }
        }
        Console.WriteLine($"    targum bridge: {added:N0} Aramaic->Hebrew folds merged");
    }

    /// <summary>Every form any lexicon knows — Hebrew and Aramaic, inflected and
    /// prefixed. Used to detect glued tokens, not to judge language.</summary>
    internal static HashSet<string> LoadKnownWords()
    {
        var known = new HashSet<string>(1 << 19, StringComparer.Ordinal);

        if (File.Exists(LexicalDb))
        {
            using var con = new SqliteConnection($"Data Source={LexicalDb};Mode=ReadOnly");
            con.Open();
            foreach (var t in new[] { "surface", "variant", "base" })
            {
                using var cmd = con.CreateCommand();
                cmd.CommandText = $"select value from {t}";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    string n = Normalize(r.GetString(0));
                    if (n.Length > 0) known.Add(n);
                }
            }
        }

        if (File.Exists(DictDb))
        {
            using var con = new SqliteConnection($"Data Source={DictDb};Mode=ReadOnly");
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "select headword from word";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                string n = Normalize(r.GetString(0));
                if (n.Length > 0) known.Add(n);
            }
        }
        return known;
    }

    /// <summary>
    /// Restricts a raw lemma map to folds that are safe for THIS corpus.
    ///
    /// Two guards, both load-bearing:
    ///
    ///   1. The target must be present in this vocabulary above `minFreq`.
    ///      Folding onto a form the corpus never uses invents a node with no
    ///      statistics behind it — worse than leaving the split alone.
    ///
    ///   2. The target must share the form's first letter. This is the guard
    ///      against a real observed error: lexical.db maps `מלכת` -> `הלכ`
    ///      (a plausible reading, but wrong here). Legitimate Hebrew/Aramaic
    ///      inflection changes suffixes and adds prefixes; it does not silently
    ///      swap the first root letter, so a first-letter mismatch on an
    ///      equal-or-shorter target is a red flag.
    ///      Prefixed forms (`דמלכא` -> `מלכ`) are still allowed, because there
    ///      the form is LONGER than the target and the target appears inside it.
    /// </summary>
    internal static Dictionary<string, string> RestrictToCorpus(
        Dictionary<string, string> raw, string[] vocab, int[] counts, int minFreq)
    {
        var present = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < vocab.Length; i++)
            if (counts[i] >= minFreq) present.Add(vocab[i]);

        var vocabSet = new HashSet<string>(vocab, StringComparer.Ordinal);
        var map = new Dictionary<string, string>(raw.Count, StringComparer.Ordinal);

        foreach (var w in vocab)
        {
            if (!raw.TryGetValue(w, out var tgt)) continue;
            if (tgt == w || !vocabSet.Contains(tgt) || !present.Contains(tgt)) continue;
            // Guard 2: allow only if the target is a substring (prefix/suffix
            // inflection) or the first letters agree.
            if (!w.Contains(tgt, StringComparison.Ordinal) && w[0] != tgt[0]) continue;
            map[w] = tgt;
        }

        // Collapse chains so every form points at a final target.
        foreach (var w in map.Keys.ToList())
        {
            var t = map[w];
            var seen = new HashSet<string>(StringComparer.Ordinal) { w };
            while (map.TryGetValue(t, out var nxt) && seen.Add(t)) t = nxt;
            map[w] = t;
        }
        return map;
    }
}
