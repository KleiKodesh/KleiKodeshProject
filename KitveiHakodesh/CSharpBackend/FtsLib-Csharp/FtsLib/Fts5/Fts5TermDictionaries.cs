#if NET6_0_OR_GREATER
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FtsLib.Fts5
{
    /// <summary>
    /// The term dictionaries searches expand against WHILE a build runs — the same shape as
    /// FtsLib's segments. Each committed batch of lines gets its own small dictionary: the
    /// batch's terms with their document counts (term_index table, rowid = ordinal rank) plus
    /// an FM-index. Four dictionaries of the same level are merged into one of the next level
    /// (counts summed), so a build of N batches keeps at most 3 per level, about 3·log4(N).
    ///
    /// Files per dictionary: {name}.db, {name}.dat (empty, for SegmentHandle), {name}.fmi.
    /// dictionaries.txt is the manifest: line 1 is the last line id the dictionaries cover,
    /// then one "name level" per live dictionary. It is replaced atomically AFTER a
    /// dictionary's files are complete, and searches open only what it lists — so a search
    /// never sees a half-written dictionary, nor a merge's inputs together with its output.
    /// Files no longer listed are deleted when nothing holds them open.
    /// </summary>
    internal static class Fts5TermDictionaries
    {
        const string ManifestName = "dictionaries.txt", FilePrefix = "dict_";
        const int MergeFanout = 4;

        internal sealed class Manifest
        {
            public int CoveredLineId;
            public List<(string Name, int Level)> Dictionaries = new List<(string, int)>();
        }

        public static string Db(string dir, string name) => Path.Combine(dir, name + ".db");
        public static string Dat(string dir, string name) => Path.Combine(dir, name + ".dat");
        public static string Fmi(string dir, string name) => Path.Combine(dir, name + ".fmi");

        public static Manifest Read(string dir)
        {
            var manifest = new Manifest();
            string path = Path.Combine(dir, ManifestName);
            if (!File.Exists(path)) return manifest;
            string[] lines;
            try
            {
                // Share read/write/delete so the build can replace or delete the manifest while
                // a search is reading it.
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                                                  FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                lines = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
            }
            catch (FileNotFoundException) { return manifest; }
            if (lines.Length == 0) return manifest;
            manifest.CoveredLineId = int.Parse(lines[0].Trim());
            for (int i = 1; i < lines.Length; i++)
            {
                var parts = lines[i].Trim().Split(' ');
                if (parts.Length == 2) manifest.Dictionaries.Add((parts[0], int.Parse(parts[1])));
            }
            return manifest;
        }

        /// <summary>
        /// Adds one committed batch: <paramref name="documentCounts"/> = term → lines of the
        /// batch containing it; <paramref name="coveredLineId"/> = the batch's last line id.
        /// </summary>
        public static void AddBatch(string dir, Dictionary<string, int> documentCounts, int coveredLineId)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            int merges = 0;
            var manifest = Read(dir);
            if (documentCounts.Count > 0)
            {
                var sorted = documentCounts.Select(kv => (kv.Key, (long)kv.Value)).ToList();
                sorted.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
                string name = NextName(manifest);
                Write(dir, name, sorted);
                manifest.Dictionaries.Add((name, 0));
            }
            manifest.CoveredLineId = coveredLineId;
            Save(dir, manifest);

            // Tiered merging, like FtsLib's segments: once MergeFanout neighbours share a level
            // they become one dictionary of the next level.
            int n;
            while ((n = manifest.Dictionaries.Count) >= MergeFanout &&
                   Enumerable.Range(n - MergeFanout, MergeFanout).All(i => manifest.Dictionaries[i].Level == manifest.Dictionaries[n - 1].Level))
            {
                int level = manifest.Dictionaries[n - 1].Level;
                var mergedTerms = ReadTerms(dir, manifest.Dictionaries[n - MergeFanout].Name);
                for (int i = n - MergeFanout + 1; i < n; i++)
                    mergedTerms = MergeSorted(mergedTerms, ReadTerms(dir, manifest.Dictionaries[i].Name));
                string merged = NextName(manifest);
                Write(dir, merged, mergedTerms);
                manifest.Dictionaries.RemoveRange(n - MergeFanout, MergeFanout);
                manifest.Dictionaries.Add((merged, level + 1));
                Save(dir, manifest);
                merges++;
            }
            DeleteUnlisted(dir, manifest);
            FtsLib.Indexing.FtsLog.Write("Fts5TermDictionaries",
                $"batch {documentCounts.Count:N0} terms, {merges} merges, {manifest.Dictionaries.Count} live, {clock.ElapsedMilliseconds} ms");
        }

        /// <summary>Removes every dictionary and the manifest (after the final terms.db is published).</summary>
        public static void Clear(string dir)
        {
            string path = Path.Combine(dir, ManifestName);
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }   // best effort
            // Searches already running may still hold a dictionary; they finish in well under a
            // second, so give them a moment before leaving anything behind.
            for (int attempt = 0; attempt < 30; attempt++)
            {
                DeleteUnlisted(dir, new Manifest());
                if (Directory.GetFiles(dir, FilePrefix + "*").Length == 0) return;
                System.Threading.Thread.Sleep(100);
            }
        }

        // ── Files ─────────────────────────────────────────────────────

        static void Write(string dir, string name, List<(string Term, long Count)> sortedTerms)
        {
            // A crash between writing a dictionary and listing it in the manifest leaves its
            // files behind under a name the resumed build will pick again.
            foreach (string leftover in new[] { Db(dir, name), Dat(dir, name), Fmi(dir, name) })
                if (File.Exists(leftover)) File.Delete(leftover);
            using (var conn = new SqliteConnection(new SqliteConnectionStringBuilder
                   { DataSource = Db(dir, name), Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ConnectionString))
            {
                conn.Open();
                using (var setup = conn.CreateCommand())
                {
                    setup.CommandText =
                        "PRAGMA journal_mode=OFF; PRAGMA synchronous=OFF;" +
                        "CREATE TABLE term_index(term TEXT NOT NULL, skip_offset INTEGER NOT NULL DEFAULT 0, " +
                        "skip_count INTEGER NOT NULL DEFAULT 0, offset INTEGER NOT NULL DEFAULT 0, " +
                        "length INTEGER NOT NULL DEFAULT 0, count INTEGER NOT NULL)";
                    setup.ExecuteNonQuery();
                }
                using (var tx = conn.BeginTransaction())
                using (var insert = conn.CreateCommand())
                {
                    insert.Transaction = tx;
                    insert.CommandText = "INSERT INTO term_index(term, count) VALUES ($t, $c)";
                    var pTerm = insert.Parameters.Add("$t", SqliteType.Text);
                    var pCount = insert.Parameters.Add("$c", SqliteType.Integer);
                    foreach (var (term, count) in sortedTerms)   // ordinal order → rowid = rank
                    {
                        pTerm.Value = term; pCount.Value = count;
                        insert.ExecuteNonQuery();
                    }
                    tx.Commit();
                }
                using var index = conn.CreateCommand();
                index.CommandText = "CREATE UNIQUE INDEX idx_term ON term_index(term)";
                index.ExecuteNonQuery();
            }
            File.WriteAllBytes(Dat(dir, name), Array.Empty<byte>());

            var terms = new List<string>(sortedTerms.Count);
            var rowIds = new List<int>(sortedTerms.Count);
            for (int i = 0; i < sortedTerms.Count; i++) { terms.Add(sortedTerms[i].Term); rowIds.Add(i + 1); }
            if (terms.Count > 0) FmIndex.Build(Fmi(dir, name), terms, rowIds);
        }

        static List<(string Term, long Count)> ReadTerms(string dir, string name)
        {
            var result = new List<(string, long)>();
            using var conn = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = Db(dir, name), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT term, count FROM term_index ORDER BY rowid";
            using var r = cmd.ExecuteReader();
            while (r.Read()) result.Add((r.GetString(0), r.GetInt64(1)));
            return result;
        }

        static List<(string Term, long Count)> MergeSorted(List<(string Term, long Count)> a, List<(string Term, long Count)> b)
        {
            var merged = new List<(string, long)>(a.Count + b.Count);
            int i = 0, j = 0;
            while (i < a.Count || j < b.Count)
            {
                int c = i == a.Count ? 1 : j == b.Count ? -1 : string.CompareOrdinal(a[i].Term, b[j].Term);
                if (c < 0) merged.Add(a[i++]);
                else if (c > 0) merged.Add(b[j++]);
                else { merged.Add((a[i].Term, a[i].Count + b[j].Count)); i++; j++; }
            }
            return merged;
        }

        static string NextName(Manifest manifest)
        {
            int next = 1;
            foreach (var (name, _) in manifest.Dictionaries)
                next = Math.Max(next, int.Parse(name.Substring(FilePrefix.Length)) + 1);
            return FilePrefix + next.ToString("D6");
        }

        static void Save(string dir, Manifest manifest)
        {
            var lines = new List<string> { manifest.CoveredLineId.ToString() };
            foreach (var (name, level) in manifest.Dictionaries) lines.Add(name + " " + level);
            string path = Path.Combine(dir, ManifestName), tmp = path + ".tmp";
            File.WriteAllLines(tmp, lines);
            for (int attempt = 0; ; attempt++)
            {
                try { File.Move(tmp, path, overwrite: true); return; }
                catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && attempt < 40)
                {
                    System.Threading.Thread.Sleep(50);
                }
            }
        }

        /// <summary>Best effort: a file a running search still holds is retried next time.</summary>
        static void DeleteUnlisted(string dir, Manifest manifest)
        {
            var live = new HashSet<string>(manifest.Dictionaries.Select(d => d.Name), StringComparer.OrdinalIgnoreCase);
            foreach (string file in Directory.GetFiles(dir, FilePrefix + "*"))
            {
                if (live.Contains(Path.GetFileNameWithoutExtension(file))) continue;
                try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }
}
#endif
