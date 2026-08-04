// Stress battery for the related-forms FTS expansion (research harness).
// Runs against the dev service's real FtsIndex + the canonical expansion
// artifact. ALL word output is masked as [H:xxxx] hashes — no Hebrew on
// stdout (network filter constraint).
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using FtsLib.SeforimDb;
using Microsoft.Data.Sqlite;

const string IndexPath = @"C:\Users\Public\Documents\KleiKodeshProject\KitveiHakodesh\CSharpBackend\KitveiHakodeshService\bin\Release\net10.0-windows\FtsIndex";
const string SeforimDb = @"C:\ProgramData\otzaria\books\seforim.db";
const string ExpDb = @"C:\Users\Public\Documents\KleiKodeshProject\KitveiHakodesh\CSharpBackend\FtsLib-Csharp\SearchExpansion\expansion-routed.db";

static string Mask(string w)
{
    byte[] h = MD5.HashData(Encoding.UTF8.GetBytes(w));
    return "[H:" + Convert.ToHexString(h)[..4].ToLowerInvariant() + "]";
}

// ── the same rewrite as SearchExpansionService (kept in sync manually) ──────
var expCon = new SqliteConnection($"Data Source={ExpDb};Mode=ReadOnly");
expCon.Open();
var foldCmd = expCon.CreateCommand();
foldCmd.CommandText = "SELECT lemma FROM fold WHERE surface = @s";
var foldP = foldCmd.Parameters.Add("@s", SqliteType.Text);
var expCmd = expCon.CreateCommand();
expCmd.CommandText = "SELECT form, channel, source FROM exp WHERE lemma = @l ORDER BY rank";
var expP = expCmd.Parameters.Add("@l", SqliteType.Text);

static string BareHebrew(string tok)
{
    var sb = new StringBuilder(tok.Length);
    foreach (char c in tok)
    {
        if (c >= 'א' && c <= 'ת') sb.Append(c);
        else if (c >= '֑' && c <= 'ׇ' && c != '־') continue; // nikud
        else if (c is '"' or '\'' or '׳' or '״') continue;
        else return "";
    }
    return sb.ToString();
}

List<string> Alts(string bare, int perTerm = 5)
{
    foldP.Value = bare;
    string lemma = foldCmd.ExecuteScalar() as string ?? bare;
    expP.Value = lemma;
    var alts = new List<string>(perTerm);
    using var rd = expCmd.ExecuteReader();
    while (rd.Read() && alts.Count < perTerm)
    {
        string form = rd.GetString(0), channel = rd.GetString(1), source = rd.GetString(2);
        if (channel == "syn" && source != "tanach") continue;
        if (form == bare || alts.Contains(form)) continue;
        string bf = BareHebrew(form);
        if (bf.Length < 2 || bf.Length > 29 || bf.Length != form.Length) continue;
        alts.Add(form);
    }
    return alts;
}

string Rewrite(string query)
{
    if (query.Contains('|')) return query;
    var sb = new StringBuilder();
    foreach (string tok in query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
    {
        if (sb.Length > 0) sb.Append(' ');
        sb.Append(tok);
        string bare = BareHebrew(tok);
        if (bare.Length < 2) continue;
        foreach (string a in Alts(bare)) sb.Append(" | ").Append(a);
    }
    return sb.ToString();
}

// ── corpus word sample by frequency band ────────────────────────────────────
Console.WriteLine("loading corpus vocabulary...");
var freq = new Dictionary<string, int>();
using (var con = new SqliteConnection($"Data Source={SeforimDb};Mode=ReadOnly"))
{
    con.Open();
    var cmd = con.CreateCommand();
    // sampled lines are enough for a frequency map (1 in 8 lines)
    cmd.CommandText = "SELECT content FROM line WHERE id % 8 = 0";
    using var rd = cmd.ExecuteReader();
    var sb = new StringBuilder();
    while (rd.Read())
    {
        string c = rd.GetString(0);
        sb.Clear();
        foreach (char ch in c)
        {
            if (ch >= 'א' && ch <= 'ת') sb.Append(ch);
            else
            {
                if (sb.Length >= 2)
                {
                    string w = sb.ToString();
                    freq[w] = freq.GetValueOrDefault(w) + 1;
                }
                sb.Clear();
            }
        }
        if (sb.Length >= 2) { string w = sb.ToString(); freq[w] = freq.GetValueOrDefault(w) + 1; }
    }
}
Console.WriteLine($"vocab {freq.Count:N0} (1/8 line sample)");

var rand = new Random(42);
List<string> Band(int lo, int hi, int n) =>
    freq.Where(kv => kv.Value >= lo && kv.Value < hi && kv.Key.Length >= 3)
        .OrderBy(_ => rand.Next()).Take(n).Select(kv => kv.Key).ToList();

var bands = new (string Name, List<string> Words)[]
{
    ("rare(8x lines:2-8)",  Band(2, 8, 12)),
    ("mid(8-80)",           Band(8, 80, 12)),
    ("freq(80-800)",        Band(80, 800, 12)),
    ("hyper(800+)",         Band(800, int.MaxValue, 12)),
};

var index = new SeforimIndex(IndexPath, SeforimDb);

(long Count, double Ms) RunSearch(string q, int timeBudgetMs = 30000)
{
    var sw = Stopwatch.StartNew();
    long n = 0;
    using var cts = new CancellationTokenSource(timeBudgetMs);
    try
    {
        foreach (var _ in index.Search(q, cap: 0, expandKetiv: false, ct: cts.Token))
        {
            n++;
            if (sw.ElapsedMilliseconds > timeBudgetMs) { n = -n; break; } // timed out mid-stream
        }
    }
    catch (OperationCanceledException) { n = -Math.Abs(n); }
    return (n, sw.Elapsed.TotalMilliseconds);
}

// ── battery 1: rewrite latency ──────────────────────────────────────────────
Console.WriteLine("\n== rewrite latency (2,000 random words) ==");
{
    var words = freq.Keys.OrderBy(_ => rand.Next()).Take(2000).ToList();
    var times = new List<double>(words.Count);
    foreach (var w in words)
    {
        var sw = Stopwatch.StartNew();
        Rewrite(w);
        times.Add(sw.Elapsed.TotalMilliseconds);
    }
    times.Sort();
    Console.WriteLine($"  p50 {times[times.Count / 2]:F2} ms  p95 {times[(int)(times.Count * 0.95)]:F2} ms  max {times[^1]:F2} ms");
}

// ── battery 2: single-word literal vs expanded by band ─────────────────────
Console.WriteLine("\n== single-word: literal vs expanded (count / ms) ==");
var blowups = new List<(string W, long A, long B, double MsA, double MsB)>();
foreach (var (name, words) in bands)
{
    Console.WriteLine($"  band {name}:");
    foreach (var w in words.Take(6))
    {
        var (a, msA) = RunSearch(w);
        string rq = Rewrite(w);
        int nAlts = rq.Count(c => c == '|');
        var (b, msB) = RunSearch(rq);
        blowups.Add((w, a, b, msA, msB));
        Console.WriteLine($"    {Mask(w)} alts={nAlts}  lit {a:N0} ({msA:F0} ms) -> exp {b:N0} ({msB:F0} ms)");
    }
}

// ── battery 3: multi-word phrases from real verses ──────────────────────────
Console.WriteLine("\n== phrases (2-4 words) literal vs expanded ==");
{
    using var con = new SqliteConnection($"Data Source={SeforimDb};Mode=ReadOnly");
    con.Open();
    var cmd = con.CreateCommand();
    cmd.CommandText = "SELECT content FROM line WHERE bookId BETWEEN 1 AND 39 AND length(content) > 150 ORDER BY id LIMIT 400";
    var phrases = new List<string>();
    using var rd = cmd.ExecuteReader();
    while (rd.Read() && phrases.Count < 9)
    {
        var toks = new List<string>();
        var sb = new StringBuilder();
        foreach (char ch in rd.GetString(0))
        {
            if (ch >= 'א' && ch <= 'ת') sb.Append(ch);
            else if (ch >= '֑' && ch <= 'ׇ' && ch != '־') { }
            else { if (sb.Length >= 3) toks.Add(sb.ToString()); sb.Clear(); }
        }
        if (toks.Count >= 8)
        {
            int len = 2 + phrases.Count % 3; // 2,3,4-word rotation
            phrases.Add(string.Join(' ', toks.Skip(3).Take(len)));
        }
    }
    foreach (var p in phrases)
    {
        var (a, msA) = RunSearch(p);
        string rq = Rewrite(p);
        var (b, msB) = RunSearch(rq);
        Console.WriteLine($"    {p.Split(' ').Length}w {string.Join(' ', p.Split(' ').Select(Mask))}  lit {a:N0} ({msA:F0} ms) -> exp {b:N0} ({msB:F0} ms)");
    }
}

// ── battery 4: pathological queries ─────────────────────────────────────────
Console.WriteLine("\n== pathological ==");
{
    var hyper = bands[3].Words;
    string q10 = string.Join(' ', hyper.Take(10));
    var sw = Stopwatch.StartNew();
    string rq = Rewrite(q10);
    Console.WriteLine($"  10 hyper words: rewrite {sw.Elapsed.TotalMilliseconds:F1} ms, {rq.Count(c => c == '|')} pipes");
    var (n, ms) = RunSearch(rq, 60000);
    Console.WriteLine($"    expanded search: {n:N0} results in {ms:F0} ms{(n < 0 ? "  ** TIMED OUT (partial |count|)" : "")}");

    string rep = string.Join(' ', Enumerable.Repeat(hyper[0], 12));
    (n, ms) = RunSearch(Rewrite(rep), 60000);
    Console.WriteLine($"  same hyper word x12 expanded: {n:N0} in {ms:F0} ms{(n < 0 ? "  ** TIMED OUT" : "")}");

    string many = string.Join(' ', freq.Keys.OrderBy(_ => rand.Next()).Take(40));
    sw.Restart();
    rq = Rewrite(many);
    Console.WriteLine($"  40-word query: rewrite {sw.Elapsed.TotalMilliseconds:F1} ms, len {rq.Length}");
    (n, ms) = RunSearch(rq, 60000);
    Console.WriteLine($"    expanded search: {n:N0} in {ms:F0} ms{(n < 0 ? "  ** TIMED OUT" : "")}");
}

// ── battery 4b: nikud-carrying query tokens must still expand ──────────
Console.WriteLine("\n== nikud token expansion ==");
{
    using var con2 = new SqliteConnection($"Data Source={SeforimDb};Mode=ReadOnly");
    con2.Open();
    var cmd2 = con2.CreateCommand();
    cmd2.CommandText = "SELECT content FROM line WHERE bookId = 1 AND length(content) > 200 LIMIT 30";
    int tested = 0, expandedN = 0;
    using var rd2 = cmd2.ExecuteReader();
    while (rd2.Read() && tested < 10)
    {
        var raw = new StringBuilder();
        var toks2 = new List<string>();
        foreach (char ch in rd2.GetString(0))
        {
            bool keep = (ch >= 'א' && ch <= 'ת') ||
                        (ch >= '֑' && ch <= 'ׇ' && ch != '־');
            if (keep) raw.Append(ch);
            else { if (raw.Length >= 5) toks2.Add(raw.ToString()); raw.Clear(); }
        }
        foreach (var t in toks2)
        {
            bool pointed = false;
            foreach (char ch in t) if (ch >= '֑' && ch <= 'ׇ') { pointed = true; break; }
            if (!pointed) continue;
            tested++;
            if (Rewrite(t).Contains('|')) expandedN++;
            if (tested >= 10) break;
        }
    }
    Console.WriteLine($"  pointed tokens tested {tested}, expanded {expandedN}");
}

// ── battery 5: artifact hygiene — how many rows does the shape guard drop ──
Console.WriteLine("\n== artifact shape-guard scan ==");
{
    var cmd = expCon.CreateCommand();
    cmd.CommandText = "SELECT form FROM exp";
    long total = 0, dropped = 0;
    using var rd = cmd.ExecuteReader();
    while (rd.Read())
    {
        total++;
        string f = rd.GetString(0);
        string bf = BareHebrew(f);
        if (bf.Length < 2 || bf.Length > 29 || bf.Length != f.Length) dropped++;
    }
    Console.WriteLine($"  exp rows {total:N0}, guard-dropped {dropped:N0} ({dropped * 100.0 / total:F2}%)");
}

// ── battery 6: worst blowups summary ────────────────────────────────────────
Console.WriteLine("\n== worst expansion blowups ==");
foreach (var x in blowups.Where(x => x.A >= 0 && x.B > 0)
                         .OrderByDescending(x => (double)x.B / Math.Max(1, x.A)).Take(5))
    Console.WriteLine($"  {Mask(x.W)}: {x.A:N0} -> {x.B:N0}  (x{(double)x.B / Math.Max(1, x.A):F0}, {x.MsA:F0} -> {x.MsB:F0} ms)");

Console.WriteLine("\ndone.");
