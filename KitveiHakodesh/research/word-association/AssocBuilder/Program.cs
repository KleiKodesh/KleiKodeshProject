using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Microsoft.Data.Sqlite;

namespace AssocBuilder;

/// <summary>
/// Builds a word-association table over seforim.db and writes it as a static,
/// reusable SQLite table.
///
/// Pipeline (mirrors FtsLib's indexing shape):
///
///   pass 1   scan corpus -> token frequencies -> vocabulary + prefix folding
///   pass 2   scan corpus -> distance-weighted pairs -> LSM segments on disk
///            (parallel across cores; sharded by book so counts stay exact)
///   merge    k-way merge segments, summing duplicates
///   score    PPMI with alpha=0.75 context smoothing, top-K prune per word
///   write    static SQLite: word, assoc  (+ covering index)
///
/// The Python builders (build_index.py / build_large.py) remain the reference
/// implementation for correctness; this is the fast path. `--verify` cross-checks
/// the two on a small corpus.
/// </summary>
internal static class Program
{
    private const string DefaultDb =
        @"C:\ProgramData\otzaria\books\seforim.db";

    // Root categories, matching build_large.py's CORPORA so the two agree.
    private static readonly Dictionary<string, string[]?> Corpora = new()
    {
        ["tanach"]     = null,                              // special: ids 1..39
        ["tanach-all"] = ["תנ״ך"],
        ["mishnah"]    = ["משנה"],
        ["bavli"]      = ["תלמוד בבלי"],
        ["yerushalmi"] = ["תלמוד ירושלמי"],
        ["midrash"]    = ["מדרש"],
        ["halacha"]    = ["הלכה"],
        ["kabbalah"]   = ["קבלה"],
        ["chasidut"]   = ["חסידות"],
        ["responsa"]   = ["שו״ת"],
        ["all"]        = [],                                // every book
    };

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var opt = Options.Parse(args);
        if (opt.SimOnly is not null)
        {
            var swSim = System.Diagnostics.Stopwatch.StartNew();
            long simEdges = SimGraph.Build(opt.SimOnly, opt.SimTopK, opt.SimProfileN,
                                           opt.SimCap, opt.Workers);
            Console.WriteLine($"  sim graph: {simEdges:N0} edges ({swSim.Elapsed.TotalSeconds:F0}s)");
            return 0;
        }
        if (opt is null) return 1;

        var sw = Stopwatch.StartNew();
        Console.WriteLine($"corpus '{opt.Corpus}'{(opt.BaseOnly ? " [base only]" : "")}"
                        + $"  window={opt.Window}  workers={opt.Workers}");

        var books = ResolveBooks(opt.Db, opt.Corpus, opt.BaseOnly);
        Console.WriteLine($"  {books.Count:N0} books");
        if (books.Count == 0) { Console.Error.WriteLine("no books matched"); return 1; }

        // ── Pass 1: vocabulary ──────────────────────────────────────
        Console.WriteLine("pass 1/2  vocabulary ...");
        var t = Stopwatch.StartNew();
        var (vocab, wordId, counts, prefixMap, nTokens, nUnits) =
            Vocabulary.Build(opt.Db, books, opt.MinCount, opt.StripPrefixes,
                             opt.MinStemFreq, opt.StemRatio, opt.Workers,
                             opt.Lemmatize, opt.BridgeCsv);
        double avgLen = nUnits > 0 ? (double)nTokens / nUnits : 1.0;
        Console.WriteLine($"  {nUnits:N0} units, {nTokens:N0} tokens, "
                        + $"{vocab.Length:N0} vocab, {prefixMap.Count:N0} folded "
                        + $"({t.Elapsed.TotalSeconds:F0}s)");

        // ── Pass 2: pairs -> LSM segments ───────────────────────────
        string tmp = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(opt.Out))!,
                                  "assoc_tmp_" + Guid.NewGuid().ToString("N")[..8]);
        Console.WriteLine($"pass 2/2  co-occurrence -> {Path.GetFileName(tmp)} ...");
        t.Restart();

        var counter = new PairCounter(opt.Db, wordId, prefixMap, opt.Window,
                                      opt.LengthNormB, avgLen, tmp,
                                      opt.BufferPairs, vocab.Length);
        var (segments, totals, grand, docFreq) = counter.Run(books, opt.Workers);
        Console.WriteLine($"  {segments.Count} segments, "
                        + $"{counter.TotalPairs:N0} pair records "
                        + $"({t.Elapsed.TotalSeconds:F0}s)");

        // ── Merge + score + write ───────────────────────────────────
        Console.WriteLine("merge + PPMI + prune + write ...");
        t.Restart();
        long edges = AssocWriter.Write(opt.Out, vocab, counts, segments, totals,
                                      grand, opt.TopK, opt.MinCooc, opt.Shift,
                                      pruneByLmi: opt.PruneLmi,
                                      scorer: opt.Scorer,
                                      meta:
                                      new BuildMeta
                                      {
                                          Corpus     = opt.Corpus,
                                          BaseOnly   = opt.BaseOnly,
                                          Books      = books.Count,
                                          Units      = nUnits,
                                          Tokens     = nTokens,
                                          Window     = opt.Window,
                                          TopK       = opt.TopK,
                                          MinCount   = opt.MinCount,
                                          LengthNormB = opt.LengthNormB,
                                          StripPrefixes = opt.StripPrefixes,
                                          MinStemFreq = opt.MinStemFreq,
                                          FoldedForms = prefixMap.Count,
                                          Lemmatize  = opt.Lemmatize,
                                          PruneByLmi = opt.PruneLmi,
                                          Scorer = opt.Scorer,
                                      });
        Console.WriteLine($"  {edges:N0} associations written "
                        + $"({t.Elapsed.TotalSeconds:F0}s)");

        try { Directory.Delete(tmp, true); } catch { /* best effort */ }

        var fi = new FileInfo(opt.Out);
        if (opt.SimTopK > 0)
        {
            var swSim = System.Diagnostics.Stopwatch.StartNew();
            long simEdges = SimGraph.Build(opt.Out, opt.SimTopK, opt.SimProfileN,
                                           opt.SimCap, opt.Workers);
            Console.WriteLine($"  sim graph: {simEdges:N0} edges ({swSim.Elapsed.TotalSeconds:F0}s)");
        }

        Console.WriteLine($"\ndone in {sw.Elapsed.TotalSeconds:F0}s -> {opt.Out}"
                        + $"  ({fi.Length / 1e6:F1} MB)");
        return 0;
    }

    // ── Corpus selection ────────────────────────────────────────────

    private static List<int> ResolveBooks(string db, string corpus, bool baseOnly)
    {
        using var con = new SqliteConnection($"Data Source={db};Mode=ReadOnly");
        con.Open();

        string sql;
        var ps = new List<string>();
        if (corpus == "tanach")
        {
            // The 39 base Tanach books are ids 1..39 in this DB.
            sql = "select id from book where id between 1 and 39";
        }
        else if (Corpora[corpus] is { Length: 0 })
        {
            sql = "select id from book where 1=1";
        }
        else
        {
            ps.AddRange(Corpora[corpus]!);
            string inList = string.Join(",", ps.Select((_, i) => $"@p{i}"));
            sql = $"""
                   select b.id from book b
                   where b.categoryId in (
                       select cc.descendantId from category_closure cc
                       join category rc on rc.id = cc.ancestorId
                       where rc.level = 0 and rc.title in ({inList}))
                   """;
        }
        if (baseOnly) sql += " and isBaseBook = 1";

        using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        for (int i = 0; i < ps.Count; i++)
            cmd.Parameters.AddWithValue($"@p{i}", ps[i]);

        var ids = new List<int>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) ids.Add(r.GetInt32(0));
        return ids;
    }

    // ── Options ─────────────────────────────────────────────────────

    internal sealed class Options
    {
        public string Db = DefaultDb;
        public string Corpus = "tanach";
        public string Out = "assoc.db";
        public bool   BaseOnly;
        public int    Window = 4;
        public int    TopK = 200;
        public int    MinCount = 3;
        public double MinCooc = 1.0;
        public double Shift;
        public double LengthNormB = 0.75;
        public bool   StripPrefixes = true;
        public int    MinStemFreq = 5;
        public double StemRatio = 0.25;

        /// <summary>Prune each word's row by LMI (count x PMI) instead of by
        /// PMI value — keeps well-supported associations over barely-attested
        /// high-PMI ones. Stored weights stay PPMI either way.</summary>
        public bool   PruneLmi = true;
        public string Scorer = "ppmi";
        public int    SimTopK = 50;
        public int    SimProfileN = 100;
        public int    SimCap = 0;
        public string? SimOnly;

        /// <summary>Targum-derived Aramaic->Hebrew fold table (targum_bridge.py).
        /// Only meaningful together with --lemmatize.</summary>
        public string? BridgeCsv;

        /// <summary>Fold inflected forms onto their lexeme using lexical.db —
        /// reaches the SUFFIX morphology the prefix heuristic cannot, and links
        /// Aramaic to Hebrew (`מלכא`/`דמלכא` -> `מלכ`).</summary>
        public bool   Lemmatize;
    public bool   PruneByLmi;
        public int    Workers = Math.Max(1, Environment.ProcessorCount - 1);

        /// <summary>Distinct pairs each shard aggregates in RAM before spilling a
        /// segment. PER SHARD, not a global budget — this is the knob that decides
        /// whether counting stays in memory or turns into tens of GB of disk I/O
        /// (16 bytes/slot, so 60M ~= 1 GB per shard).</summary>
        public int    BufferPairs = 60_000_000;

        public static Options? Parse(string[] a)
        {
            var o = new Options();
            for (int i = 0; i < a.Length; i++)
            {
                string k = a[i];
                string Next() => ++i < a.Length ? a[i] : throw new ArgumentException($"{k} needs a value");
                try
                {
                    switch (k)
                    {
                        case "--db":            o.Db = Next(); break;
                        case "--corpus":        o.Corpus = Next(); break;
                        case "--out":           o.Out = Next(); break;
                        case "--base-only":     o.BaseOnly = true; break;
                        case "--window":        o.Window = int.Parse(Next()); break;
                        case "--topk":          o.TopK = int.Parse(Next()); break;
                        case "--min-count":     o.MinCount = int.Parse(Next()); break;
                        case "--min-cooc":      o.MinCooc = double.Parse(Next()); break;
                        case "--shift":         o.Shift = double.Parse(Next()); break;
                        case "--length-norm-b": o.LengthNormB = double.Parse(Next()); break;
                        case "--no-strip-prefixes": o.StripPrefixes = false; break;
                        case "--min-stem-freq": o.MinStemFreq = int.Parse(Next()); break;
                        case "--lemmatize":     o.Lemmatize = true; break;
                        case "--prune-lmi":     o.PruneLmi = true; break;
                        case "--prune-pmi":     o.PruneLmi = false; break;
                        case "--scorer":        o.Scorer = Next(); break;
                        case "--sim-topk":      o.SimTopK = int.Parse(Next()); break;
                        case "--sim-profile":   o.SimProfileN = int.Parse(Next()); break;
                        case "--sim-cap":       o.SimCap = int.Parse(Next()); break;
                        case "--no-sim":        o.SimTopK = 0; break;
                        case "--sim-only":      o.SimOnly = Next(); break;
                        case "--bridge":        o.BridgeCsv = Next(); break;
                        case "--stem-ratio":    o.StemRatio = double.Parse(Next()); break;
                        case "--workers":       o.Workers = int.Parse(Next()); break;
                        case "--buffer-pairs":  o.BufferPairs = int.Parse(Next()); break;
                        case "--list":          ListCorpora(o.Db); return null;
                        case "-h" or "--help":  Help(); return null;
                        default:
                            Console.Error.WriteLine($"unknown option: {k}");
                            return null;
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"bad option {k}: {ex.Message}");
                    return null;
                }
            }
            if (!Corpora.ContainsKey(o.Corpus))
            {
                Console.Error.WriteLine(
                    $"unknown corpus '{o.Corpus}'. known: {string.Join(", ", Corpora.Keys)}");
                return null;
            }
            return o;
        }

        private static void Help() => Console.WriteLine("""
            AssocBuilder — word-association table over seforim.db

              --corpus NAME       tanach | mishnah | bavli | halacha | all | ...
              --base-only         exclude commentaries (the DB is only 4% base text)
              --window N          context window; scale with text-unit length
                                  (Tanach ~13 tok/line -> 4-8; rabbinic 36-86 -> 12)
              --workers N         parallel counting shards (default: cores-1)
              --buffer-pairs N    distinct pairs each shard aggregates in RAM
                                  before spilling (16 B/slot; 60M ~= 1 GB/shard).
                                  Raise it to keep counting off disk; lowering it
                                  does not reduce work, it just moves it to I/O.
              --lemmatize         fold inflections onto their lexeme via lexical.db
                                  (reaches suffixes; links Aramaic to Hebrew)
              --topk N            max associations kept per word
              --out PATH          output SQLite file
              --list              show corpora with their base-text share
            """);

        private static void ListCorpora(string db)
        {
            using var con = new SqliteConnection($"Data Source={db};Mode=ReadOnly");
            con.Open();
            Console.WriteLine($"{"corpus",-12} {"books",7} {"base",6}");
            foreach (var name in Corpora.Keys)
            {
                var all  = ResolveBooks(db, name, false).Count;
                var bse  = ResolveBooks(db, name, true).Count;
                Console.WriteLine($"{name,-12} {all,7:N0} {bse,6:N0}");
            }
        }
    }
}

internal sealed class BuildMeta
{
    public string Corpus = "";
    public bool   BaseOnly;
    public int    Books;
    public long   Units;
    public long   Tokens;
    public int    Window;
    public int    TopK;
    public int    MinCount;
    public double LengthNormB;
    public bool   StripPrefixes;
    public int    MinStemFreq;
    public int    FoldedForms;
    public bool   Lemmatize;
    public bool   PruneByLmi;
    public string Scorer = "ppmi";
}
