using System.Security.Cryptography;
using System.Text.Json;
using KitveiHakodesh.Core.SeforimLibraryDownload;
using Microsoft.Data.Sqlite;
using ZstdSharp;

namespace KitveiHakodeshService.Tests;

/// <summary>
/// The library download, import and update planning.
///   --library-selftest              offline fixtures: release-asset pick (single / split / no manifest / gap),
///                                   patch route across schemas, import of a small synthetic .zst (single and
///                                   split, with and without manifest), a 6 -> 7 patch applied and verified.
///   --library-feed-live             the live GitHub releases: what a download picks, and the update routes.
///   --library-hash-real DB MANIFEST schema-7 table hashes of a real library vs a manifest's fromTableContentHashes.
/// </summary>
internal static class SeforimLibraryDownloadCheck
{
    private static int _failures;

    private static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
        if (!ok) _failures++;
    }

    // ── Offline ─────────────────────────────────────────────────────────────────

    public static int SelfTest()
    {
        _failures = 0;
        string dir = Path.Combine(AppContext.BaseDirectory, "library-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        // The app folder (books\ downloads, imports) goes to scratch, never the shared dev AppRoot.
        Directory.CreateDirectory(Path.Combine(dir, "approot"));
        Environment.SetEnvironmentVariable("KHS_APP_ROOT", Path.Combine(dir, "approot"));
        try
        {
            AssetPick().GetAwaiter().GetResult();
            Routes();
            Import(dir).GetAwaiter().GetResult();
            ReviewFixes(dir).GetAwaiter().GetResult();
            PatchSchema6To7(dir);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, true); } catch { /* scratch */ }
        }
        Console.WriteLine(_failures == 0 ? "library selftest: PASS" : $"library selftest: {_failures} FAILURES");
        return _failures;
    }

    private static SeforimLibraryReleaseAsset A(string name, long size, string? sha = null) =>
        new(name, size, "https://example.invalid/" + name, sha);

    private static async Task AssetPick()
    {
        Console.WriteLine("asset pick");
        string sha0 = new string('a', 64), sha1 = new string('b', 64), whole = new string('c', 64);
        string manifest = JsonSerializer.Serialize(new
        {
            schemaVersion = 1, archive = "seforim-schema7.db.zst", size = 300, sha256 = whole,
            parts = new[] { new { name = "seforim-schema7.db.zst.part-000", size = 200, sha256 = sha0 }, new { name = "seforim-schema7.db.zst.part-001", size = 100, sha256 = sha1 } },
        });
        Task<string> Fetch(SeforimLibraryReleaseAsset _, CancellationToken __) => Task.FromResult(manifest);

        var split = new SeforimLibraryRelease("v38-20261112163847", new[]
        {
            A("patch-v37-v38.db.zst", 5), A("seforim-schema7.db.zst.part-001", 100), A("seforim-schema7.db.zst.part-000", 200),
            A("seforim-schema7.db.zst.manifest.json", 500), A("otzaria-library-index.tar.zst.part-000", 999),
        });
        var got = await SeforimLibraryReleaseFeed.DatabaseOfAsync(split, Fetch, default);
        Check(got != null && got.SchemaVersion == 7 && got.Version == 38 && got.Size == 300 && got.Sha256 == whole
              && got.Parts.Select(p => p.Name).SequenceEqual(new[] { "seforim-schema7.db.zst.part-000", "seforim-schema7.db.zst.part-001" })
              && got.Parts[0].Sha256 == sha0 && got.Parts[1].Sha256 == sha1,
            "split + manifest: both parts in manifest order with their sha256");

        var single = new SeforimLibraryRelease("v32-20261010190601", new[] { A("seforim-schema6.db.zst", 1000, sha0) });
        got = await SeforimLibraryReleaseFeed.DatabaseOfAsync(single, Fetch, default);
        Check(got != null && got.SchemaVersion == 6 && got.Parts.Count == 1 && got.Sha256 == sha0, "single file schema 6");

        var both = new SeforimLibraryRelease("v40", new[] { A("seforim-schema7.db.zst", 10), A("seforim-schema8.db.zst", 20) });
        got = await SeforimLibraryReleaseFeed.DatabaseOfAsync(both, Fetch, default);
        Check(got?.SchemaVersion == 8, "two schemas in one release: the highest (no schema cap)");

        var noManifest = new SeforimLibraryRelease("v41", new[] { A("seforim-schema9.db.zst.part-001", 5), A("seforim-schema9.db.zst.part-000", 7) });
        got = await SeforimLibraryReleaseFeed.DatabaseOfAsync(noManifest, Fetch, default);
        Check(got != null && got.Size == 12 && got.Parts[0].Name.EndsWith("000", StringComparison.Ordinal), "parts without manifest: by number");

        var gap = new SeforimLibraryRelease("v42", new[] { A("seforim-schema9.db.zst.part-000", 5), A("seforim-schema9.db.zst.part-002", 7) });
        Check(await SeforimLibraryReleaseFeed.DatabaseOfAsync(gap, Fetch, default) == null, "a gap in the parts: no library");

        var badSize = new SeforimLibraryRelease("v43", new[] { A("seforim-schema7.db.zst.part-000", 201), A("seforim-schema7.db.zst.part-001", 100), A("seforim-schema7.db.zst.manifest.json", 1) });
        Check(await SeforimLibraryReleaseFeed.DatabaseOfAsync(badSize, Fetch, default) == null, "published part size differs from the manifest: refused");

        var none = new SeforimLibraryRelease("v44", new[] { A("library-changes.md", 5) });
        Check(await SeforimLibraryReleaseFeed.DatabaseOfAsync(none, Fetch, default) == null, "no database asset: null");
    }

    private static SeforimLibraryPatchStep Edge(int from, int to, int fromSchema, int toSchema, long size, bool rebase = false, int format = 4) =>
        new($"v{to}", new SeforimLibraryPatchManifest
        {
            FromVersion = from, ToVersion = to, FromSchemaVersion = fromSchema, ToSchemaVersion = toSchema,
            PatchFormatVersion = format, FullRebase = rebase,
            PatchFiles = { new SeforimLibraryPatchFile { File = $"patch-v{from}-v{to}.db.zst", Compression = "zstd" } },
        }, A($"patch-v{from}-v{to}.db.zst", size));

    private static void Routes()
    {
        Console.WriteLine("update routes");
        // The real graph of 2026-11 (sizes in MB, from the live releases).
        var edges = new[]
        {
            Edge(32, 36, 6, 7, 341), Edge(34, 36, 7, 7, 98), Edge(35, 36, 7, 7, 3),
            Edge(33, 37, 7, 7, 210), Edge(35, 37, 7, 7, 95), Edge(36, 37, 7, 7, 92),
            Edge(34, 38, 7, 7, 404), Edge(36, 38, 7, 7, 310), Edge(37, 38, 7, 7, 218),
            Edge(22, 38, 2, 7, 1, rebase: true), Edge(29, 37, 6, 7, 1, rebase: true),
        };
        string R(SeforimLibraryUpdatePlan p) => string.Join(" ", p.Patches.Select(s => $"{s.Manifest.FromVersion}->{s.Manifest.ToVersion}"));

        var p32 = SeforimLibraryUpdatePlanner.Plan(32, 6, 38, edges);
        Check(R(p32) == "32->36 36->38", "v32 schema 6 -> v38: schema-changing 32->36 then 36->38 (" + R(p32) + ")");
        var p37 = SeforimLibraryUpdatePlanner.Plan(37, 7, 38, edges);
        Check(R(p37) == "37->38", "v37 -> v38: one patch");
        var p36wrong = SeforimLibraryUpdatePlanner.Plan(36, 6, 38, edges);
        Check(p36wrong.NeedsFullDownload, "v36 but schema 6: no edge starts there -> full download");
        var p29 = SeforimLibraryUpdatePlanner.Plan(29, 6, 38, edges);
        Check(p29.NeedsFullDownload, "v29: only a fullRebase barrier -> full download");
        Check(SeforimLibraryUpdatePlanner.Plan(38, 7, 38, edges).IsUpToDate, "v38: up to date");
        var future = SeforimLibraryUpdatePlanner.Plan(38, 7, 39, new[] { Edge(38, 39, 7, 8, 5) });
        Check(future.NeedsFullDownload, "a patch into an unknown schema (8) is not applied -> full download");
        var badFormat = SeforimLibraryUpdatePlanner.Plan(37, 7, 38, new[] { Edge(37, 38, 7, 7, 5, format: 5) });
        Check(badFormat.NeedsFullDownload, "patch format 5 (newer than ours) -> full download");
    }

    private static byte[] MakeSqlite(string path, int rows)
    {
        using (var c = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "CREATE TABLE book(id INTEGER PRIMARY KEY, title TEXT); CREATE TABLE schema_meta(key TEXT PRIMARY KEY, value TEXT);"
                + "INSERT INTO schema_meta VALUES('db_version','38'),('db_schema_version','7');"
                + string.Concat(Enumerable.Range(1, rows).Select(i => $"INSERT INTO book VALUES({i}, 'book number {i} {Guid.NewGuid()}');"));
            cmd.ExecuteNonQuery();
        }
        return File.ReadAllBytes(path);
    }

    private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    private static async Task Import(string dir)
    {
        Console.WriteLine("import");
        byte[] db = MakeSqlite(Path.Combine(dir, "source.db"), 3000);
        byte[] zst;
        using (var compressor = new Compressor(3)) zst = compressor.Wrap(db).ToArray();

        async Task<bool> Roundtrip(string picked, string what)
        {
            var parts = SeforimLibraryArchiveImport.ResolveParts(picked);
            string outPath = Path.Combine(dir, "out-" + Guid.NewGuid().ToString("N") + ".db");
            await SeforimLibraryArchiveExtractor.ExtractAsync(parts, outPath, (_, _) => { }, default);
            bool same = File.ReadAllBytes(outPath).AsSpan().SequenceEqual(db);
            Check(same, $"{what}: {parts.Count} part(s), unpacked bytes identical ({db.Length} bytes from {zst.Length} compressed)");
            return same;
        }

        // Single file.
        string single = Path.Combine(dir, "single", "seforim-schema7.db.zst");
        Directory.CreateDirectory(Path.GetDirectoryName(single)!);
        File.WriteAllBytes(single, zst);
        await Roundtrip(single, "single .zst");

        // Split in three, no manifest; the user picks the MIDDLE part.
        string splitDir = Path.Combine(dir, "split");
        Directory.CreateDirectory(splitDir);
        int third = zst.Length / 3;
        var chunks = new[] { zst[..third], zst[third..(2 * third)], zst[(2 * third)..] };
        for (int i = 0; i < 3; i++) File.WriteAllBytes(Path.Combine(splitDir, $"seforim-schema7.db.zst.part-{i:000}"), chunks[i]);
        await Roundtrip(Path.Combine(splitDir, "seforim-schema7.db.zst.part-001"), "split, no manifest, middle part picked");

        // With a manifest: each part's sha256 checked while unpacking.
        string manifestPath = Path.Combine(splitDir, "seforim-schema7.db.zst.manifest.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(new
        {
            schemaVersion = 1, archive = "seforim-schema7.db.zst", size = zst.Length, sha256 = Sha(zst),
            parts = chunks.Select((c, i) => new { name = $"seforim-schema7.db.zst.part-{i:000}", size = c.Length, sha256 = Sha(c) }).ToArray(),
        }));
        await Roundtrip(Path.Combine(splitDir, "seforim-schema7.db.zst.part-000"), "split + manifest");
        var resolved = SeforimLibraryArchiveImport.ResolveParts(manifestPath);
        Check(resolved.All(p => p.Sha256 != null), "manifest picked: every part carries its sha256");

        // A flipped byte in part 2 (same size): caught by the sha256.
        var bad = (byte[])chunks[1].Clone();
        bad[10] ^= 0xFF;
        File.WriteAllBytes(Path.Combine(splitDir, "seforim-schema7.db.zst.part-001"), bad);
        try
        {
            await SeforimLibraryArchiveExtractor.ExtractAsync(SeforimLibraryArchiveImport.ResolveParts(manifestPath), Path.Combine(dir, "bad.db"), (_, _) => { }, default);
            Check(false, "damaged part rejected");
        }
        catch (SeforimLibraryPartChecksumException ex) { Check(true, "damaged part rejected: " + ex.Message[..Math.Min(70, ex.Message.Length)]); }
        File.WriteAllBytes(Path.Combine(splitDir, "seforim-schema7.db.zst.part-001"), chunks[1]);

        // A part missing.
        File.Delete(Path.Combine(splitDir, "seforim-schema7.db.zst.part-002"));
        try { SeforimLibraryArchiveImport.ResolveParts(manifestPath); Check(false, "missing part reported"); }
        catch (FileNotFoundException) { Check(true, "missing part reported (manifest)"); }
        File.Delete(manifestPath);
        File.Move(Path.Combine(splitDir, "seforim-schema7.db.zst.part-001"), Path.Combine(splitDir, "seforim-schema7.db.zst.part-005"));
        try { SeforimLibraryArchiveImport.ResolveParts(Path.Combine(splitDir, "seforim-schema7.db.zst.part-000")); Check(false, "gap reported"); }
        catch (FileNotFoundException) { Check(true, "gap in parts reported (no manifest)"); }

        try { SeforimLibraryArchiveImport.ResolveParts(Path.Combine(dir, "source.db")); Check(false, "non-archive refused"); }
        catch (InvalidDataException) { Check(true, "a .db is not an archive: refused"); }
    }

    /// <summary>Serves a fake GitHub: the releases API, a split manifest, and two parts.</summary>
    private sealed class FakeGithub : HttpMessageHandler
    {
        public readonly Dictionary<string, byte[]> Files = new();
        public readonly Dictionary<string, int> Hits = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string key = request.RequestUri!.AbsolutePath.Split('/').Last();
            if (request.RequestUri.Host == "api.github.com") key = "releases";
            Hits[key] = Hits.GetValueOrDefault(key) + 1;
            var response = Files.TryGetValue(key, out var bytes)
                ? new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                : new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
            return Task.FromResult(response);
        }
    }

    /// <summary>Review fixes: a bad part re-downloads ONLY that part (typed checksum error);
    /// a truncated archive is "incomplete", not unexpected; a single .zst is checked against a
    /// manifest beside it.</summary>
    private static async Task ReviewFixes(string dir)
    {
        Console.WriteLine("review fixes");
        byte[] db = MakeSqlite(Path.Combine(dir, "fix-source.db"), 3000);
        byte[] zst;
        using (var compressor = new Compressor(3)) zst = compressor.Wrap(db).ToArray();
        int half = zst.Length / 2;
        byte[] p0 = zst[..half], p1 = zst[half..];

        // 1 + 2: full download, part-001 served damaged.
        const string a = "seforim-schema7.db.zst";
        var fake = new FakeGithub();
        string url(string n) => "https://github.com/Otzaria/SeforimLibrary/releases/download/v99-1/" + n;
        string manifestJson = JsonSerializer.Serialize(new
        {
            schemaVersion = 1, archive = a, size = zst.Length, sha256 = Sha(zst),
            parts = new[] { new { name = a + ".part-000", size = p0.Length, sha256 = Sha(p0) }, new { name = a + ".part-001", size = p1.Length, sha256 = Sha(p1) } },
        });
        fake.Files["releases"] = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[]
        {
            new
            {
                tag_name = "v99-1", draft = false, prerelease = false,
                assets = new[]
                {
                    new { name = a + ".part-000", size = (long)p0.Length, browser_download_url = url(a + ".part-000") },
                    new { name = a + ".part-001", size = (long)p1.Length, browser_download_url = url(a + ".part-001") },
                    new { name = a + ".manifest.json", size = (long)manifestJson.Length, browser_download_url = url(a + ".manifest.json") },
                },
            },
        }));
        fake.Files[a + ".manifest.json"] = System.Text.Encoding.UTF8.GetBytes(manifestJson);
        fake.Files[a + ".part-000"] = p0;
        var damaged = (byte[])p1.Clone();
        damaged[5] ^= 0xFF;
        fake.Files[a + ".part-001"] = damaged;

        using var http = new HttpClient(fake);
        var full = new SeforimLibraryFullDownload(http);
        var progress = new SeforimLibraryDownloadProgressReporter();
        string dest = Path.Combine(dir, "downloaded.db");
        bool ok = await full.RunAsync(dest, progress, default);
        string part0 = SeforimLibraryDownloadLocations.DownloadPath(a + ".part-000");
        string part1 = SeforimLibraryDownloadLocations.DownloadPath(a + ".part-001");
        Check(!ok && progress.Snapshot().Failure == SeforimLibraryDownloadFailure.ChecksumMismatch, "damaged part-001: run fails with ChecksumMismatch");
        Check(File.Exists(part0) && !File.Exists(part1), "only the damaged part was deleted; part-000 kept");

        fake.Files[a + ".part-001"] = p1;
        ok = await full.RunAsync(dest, progress, default);
        Check(ok && File.ReadAllBytes(dest).AsSpan().SequenceEqual(db), "second run completes, unpacked bytes identical");
        Check(fake.Hits.GetValueOrDefault(a + ".part-000") == 1 && fake.Hits.GetValueOrDefault(a + ".part-001") == 2,
            $"part-000 fetched once, part-001 twice (hits {fake.Hits.GetValueOrDefault(a + ".part-000")}/{fake.Hits.GetValueOrDefault(a + ".part-001")})");
        Check(!File.Exists(part0) && !File.Exists(part1), "downloaded parts removed after success");

        // 2: the extractor's typed checksum error names the part.
        string sdir = Path.Combine(dir, "typed");
        Directory.CreateDirectory(sdir);
        File.WriteAllBytes(Path.Combine(sdir, a + ".part-000"), p0);
        File.WriteAllBytes(Path.Combine(sdir, a + ".part-001"), damaged);
        try
        {
            await SeforimLibraryArchiveExtractor.ExtractAsync(new[]
            {
                new SeforimLibraryArchivePart(Path.Combine(sdir, a + ".part-000"), Sha(p0)),
                new SeforimLibraryArchivePart(Path.Combine(sdir, a + ".part-001"), Sha(p1)),
            }, Path.Combine(dir, "typed.db"), (_, _) => { }, default);
            Check(false, "typed checksum exception");
        }
        catch (SeforimLibraryPartChecksumException ex) { Check(ex.PartPath.EndsWith(".part-001", StringComparison.Ordinal), "SeforimLibraryPartChecksumException names part-001"); }

        // 3: truncated archives -> SeforimLibraryArchiveTruncatedException, import reports ImportIncomplete.
        string tdir = Path.Combine(dir, "trunc");
        Directory.CreateDirectory(tdir);
        File.WriteAllBytes(Path.Combine(tdir, a + ".part-000"), p0); // part-001 missing, no manifest
        try
        {
            await SeforimLibraryArchiveExtractor.ExtractAsync(SeforimLibraryArchiveImport.ResolveParts(Path.Combine(tdir, a + ".part-000")), Path.Combine(dir, "t.db"), (_, _) => { }, default);
            Check(false, "missing last part detected");
        }
        catch (SeforimLibraryArchiveTruncatedException) { Check(true, "missing last part (no manifest): truncated"); }
        string cut = Path.Combine(tdir, "cut", a);
        Directory.CreateDirectory(Path.GetDirectoryName(cut)!);
        File.WriteAllBytes(cut, zst[..(zst.Length - 50)]);
        try
        {
            await SeforimLibraryArchiveExtractor.ExtractAsync(SeforimLibraryArchiveImport.ResolveParts(cut), Path.Combine(dir, "t2.db"), (_, _) => { }, default);
            Check(false, "cut .zst detected");
        }
        catch (SeforimLibraryArchiveTruncatedException) { Check(true, "single .zst cut short: truncated"); }

        using (var downloader = new SeforimLibraryDownloader(new HttpClient(new FakeGithub())))
        {
            await downloader.ImportAsync(cut, default);
            var f = downloader.Progress;
            Check(f.Phase == SeforimLibraryDownloadPhase.Failed && f.Failure == SeforimLibraryDownloadFailure.ImportIncomplete,
                $"import of a cut .zst reports ImportIncomplete (got {f.Phase}/{f.Failure})");
            await downloader.ImportAsync(Path.Combine(tdir, a + ".part-000"), default);
            f = downloader.Progress;
            Check(f.Failure == SeforimLibraryDownloadFailure.ImportIncomplete, $"import with the last part missing reports ImportIncomplete (got {f.Failure})");

            // 4: single .zst with a manifest beside it: whole-archive sha256 checked.
            string sd = Path.Combine(dir, "single-m", a);
            Directory.CreateDirectory(Path.GetDirectoryName(sd)!);
            var flipped = (byte[])zst.Clone();
            flipped[zst.Length / 2] ^= 0xFF;
            File.WriteAllBytes(sd, flipped);
            File.WriteAllText(sd + ".manifest.json", JsonSerializer.Serialize(new
            {
                schemaVersion = 1, archive = a, size = zst.Length, sha256 = Sha(zst),
                parts = new[] { new { name = a, size = zst.Length, sha256 = Sha(zst) } },
            }));
            Check(SeforimLibraryArchiveImport.ResolveParts(sd).Single().Sha256 == Sha(zst), "single .zst + manifest: whole-archive sha256 attached");
            await downloader.ImportAsync(sd, default);
            f = downloader.Progress;
            Check(f.Failure == SeforimLibraryDownloadFailure.ChecksumMismatch, $"damaged single .zst + manifest: ChecksumMismatch (got {f.Failure})");
            File.WriteAllBytes(sd, zst);
            string okOut = Path.Combine(dir, "single-ok.db");
            await SeforimLibraryArchiveExtractor.ExtractAsync(SeforimLibraryArchiveImport.ResolveParts(sd), okOut, (_, _) => { }, default);
            Check(File.ReadAllBytes(okOut).AsSpan().SequenceEqual(db), "intact single .zst + manifest: unpacks");
        }
        Check(!File.Exists(SeforimLibraryDownloadLocations.DatabasePath), "failed imports wrote no library");
    }

    /// <summary>A schema-changing patch (6 -> 7): migrations create a schema-7 table, upserts fill
    /// it, and the result is verified in schema 7's hash order against the manifest.</summary>
    private static void PatchSchema6To7(string dir)
    {
        Console.WriteLine("patch 6 -> 7");
        string Exec(string path, string sql)
        {
            using var c = new SqliteConnection($"Data Source={path};Pooling=False");
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
            return path;
        }
        Dictionary<string, string> Hashes(string path, int schema)
        {
            using var c = new SqliteConnection($"Data Source={path};Pooling=False");
            c.Open();
            return SeforimLibraryContentHasher.HashTables(c, SeforimLibraryPatchTables.HashOrder(schema), SeforimLibraryPatchTables.RowOrder(schema));
        }
        const string baseSql = "CREATE TABLE schema_meta(key TEXT PRIMARY KEY, value TEXT); CREATE TABLE book(id INTEGER PRIMARY KEY, title TEXT);"
            + "INSERT INTO book VALUES(1,'a'),(2,'b');";
        const string bannerDdl = "CREATE TABLE banner(id INTEGER PRIMARY KEY, text TEXT)";

        string local = Exec(Path.Combine(dir, "v1.db"), baseSql + "INSERT INTO schema_meta VALUES('db_version','1'),('db_schema_version','6');");
        string expected = Exec(Path.Combine(dir, "v2.db"), baseSql + bannerDdl + ";INSERT INTO banner VALUES(5,'hello');"
            + "INSERT INTO book VALUES(3,'c'); INSERT INTO schema_meta VALUES('db_version','2'),('db_schema_version','7');");
        string patch = Exec(Path.Combine(dir, "patch.db"),
            "CREATE TABLE patch_meta(key TEXT, value TEXT); INSERT INTO patch_meta VALUES('schema_version','4'),('from_version','1'),('to_version','2');"
            + "CREATE TABLE migrations(version INTEGER, sql TEXT); INSERT INTO migrations VALUES(1,'" + bannerDdl + "');"
            + "CREATE TABLE upsert_banner(id INTEGER, text TEXT); INSERT INTO upsert_banner VALUES(5,'hello');"
            + "CREATE TABLE upsert_book(id INTEGER, title TEXT); INSERT INTO upsert_book VALUES(3,'c');"
            + "CREATE TABLE upsert_schema_meta(key TEXT, value TEXT); INSERT INTO upsert_schema_meta VALUES('db_version','2'),('db_schema_version','7');");

        SeforimLibraryPatchManifest Manifest(Dictionary<string, string>? from, Dictionary<string, string>? to, string toWhole) => new()
        {
            FromVersion = 1, ToVersion = 2, FromSchemaVersion = 6, ToSchemaVersion = 7, PatchFormatVersion = 4,
            FromTableContentHashes = from, ToTableContentHashes = to, ToContentHash = toWhole,
            PatchFiles = { new SeforimLibraryPatchFile { File = "patch.db.zst", Compression = "zstd" } },
        };
        string whole;
        using (var c = new SqliteConnection($"Data Source={expected};Pooling=False"))
        {
            c.Open();
            whole = SeforimLibraryContentHasher.HashWhole(c, SeforimLibraryPatchTables.HashOrder(7), SeforimLibraryPatchTables.RowOrder(7));
        }
        var fromHashes = Hashes(local, 6);
        var toHashes = Hashes(expected, 7);
        Check(fromHashes.Count == 39 && toHashes.Count == 45, $"hash orders: schema 6 = {fromHashes.Count} tables, schema 7 = {toHashes.Count}");

        // Per-table verification.
        string copy = Path.Combine(dir, "apply1.db");
        File.Copy(local, copy);
        try
        {
            SeforimLibraryPatchApplier.Apply(copy, patch, Manifest(fromHashes, toHashes, whole));
            Check(SeforimLibraryLocalVersion.Read(copy) is { DbVersion: 2, SchemaVersion: 7 }, "applied 6 -> 7 (per-table hashes): now v2 schema 7");
        }
        catch (Exception ex) { Check(false, "apply 6 -> 7 per-table: " + ex.Message); }

        // Whole-database verification (no table maps).
        copy = Path.Combine(dir, "apply2.db");
        File.Copy(local, copy);
        try
        {
            SeforimLibraryPatchApplier.Apply(copy, patch, Manifest(null, null, whole));
            Check(SeforimLibraryLocalVersion.Read(copy) is { DbVersion: 2 }, "applied 6 -> 7 (whole hash)");
        }
        catch (Exception ex) { Check(false, "apply 6 -> 7 whole: " + ex.Message); }

        // A wrong expected hash rolls everything back.
        copy = Path.Combine(dir, "apply3.db");
        File.Copy(local, copy);
        var wrong = new Dictionary<string, string>(toHashes) { ["banner"] = new string('0', 64) };
        try
        {
            SeforimLibraryPatchApplier.Apply(copy, patch, Manifest(fromHashes, wrong, whole));
            Check(false, "mismatch rejected");
        }
        catch (InvalidDataException)
        {
            SqliteConnection.ClearAllPools();
            Check(SeforimLibraryLocalVersion.Read(copy) is { DbVersion: 1, SchemaVersion: 6 }, "hash mismatch: rejected and rolled back (still v1 schema 6)");
        }
    }

    // ── Live ────────────────────────────────────────────────────────────────────

    private static HttpClient Http()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("KitveiHakodesh-selftest");
        return http;
    }

    public static int FeedLive()
    {
        using var http = Http();
        var feed = new SeforimLibraryReleaseFeed(http);
        var releases = feed.ListAsync(default).GetAwaiter().GetResult();
        Console.WriteLine("stable releases (newest first): " + string.Join(", ", releases.Take(6).Select(r => r.Tag)));
        var archive = feed.FindNewestDatabaseAsync(default).GetAwaiter().GetResult();
        if (archive == null) { Console.WriteLine("no library found"); return 1; }
        Console.WriteLine($"download picks: {archive.ReleaseTag} v{archive.Version} schema {archive.SchemaVersion} {archive.ArchiveName} {archive.Size} bytes sha256={archive.Sha256 ?? "-"}");
        foreach (var p in archive.Parts) Console.WriteLine($"   part {p.Name} {p.Size} sha256={p.Sha256 ?? "-"}");

        var planner = new SeforimLibraryUpdatePlanner(http);
        foreach (var (v, s) in new (int, int?)[] { (32, 6), (34, 7), (36, 7), (37, 7), (38, 7), (22, 2), (33, 6) })
        {
            var plan = planner.PlanAsync(v, s, default).GetAwaiter().GetResult();
            string what = plan.IsUpToDate ? "up to date" : plan.NeedsFullDownload ? "FULL DOWNLOAD" :
                "patches " + string.Join(" ", plan.Patches.Select(p => $"{p.Manifest.FromVersion}->{p.Manifest.ToVersion}(s{p.Manifest.FromSchemaVersion}->{p.Manifest.ToSchemaVersion},{p.Patch.Size / 1048576}MB)"));
            Console.WriteLine($"update from v{v} schema {s}: target v{plan.TargetVersion}: {what}");
        }
        return 0;
    }

    public static int HashReal(string dbPath, string manifestPath)
    {
        var manifest = SeforimLibraryPatchManifest.Parse(File.ReadAllText(manifestPath))!;
        var expected = manifest.FromTableContentHashes!;
        string[] small =
        {
            "source", "author", "topic", "pub_place", "pub_date", "connection_type", "generation", "category",
            "book", "book_topic", "book_author", "book_base_text", "book_acronym", "book_generation", "book_has_links",
            "alt_toc_structure", "default_commentator", "default_targum", "banner", "book_banner", "book_protection", "schema_meta",
        };
        using var c = new SqliteConnection($"Data Source=file:{dbPath.Replace('\\', '/')}?mode=ro&immutable=1;Pooling=False");
        c.Open();
        var actual = SeforimLibraryContentHasher.HashTables(c, small, SeforimLibraryPatchTables.RowOrder(manifest.FromSchemaVersion));
        int bad = 0;
        foreach (string t in small)
        {
            bool ok = actual[t] == expected[t];
            if (!ok) bad++;
            Console.WriteLine($"  {(ok ? "ok  " : "DIFF")} {t,-22} {actual[t][..16]} vs {expected[t][..16]}");
        }
        Console.WriteLine(bad == 0 ? "real hash check: PASS" : $"real hash check: {bad} differ");
        return bad;
    }
}
