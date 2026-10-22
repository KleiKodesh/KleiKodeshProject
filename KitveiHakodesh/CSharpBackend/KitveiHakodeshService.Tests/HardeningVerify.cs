// Automated verification of the multi-library hardening work (branch feature/multi-library-hardening).
//
// Run: dotnet run -c Release -- --hardening
//
// Never touches a real user's registry (AppSettingsRegistry.InMemoryStore) or any installed
// service: every database and pipe lives in a temp directory / uses a unique pipe name.
//
//   A. alt_toc_structure/alt_toc_entry (and even the regular TOC tables) are OPTIONAL for an
//      additional library: it is attached, indexed and searched without them, never skipped.
//   B. Split + zstd-compressed (SeforimLibrary schema 6+) additional libraries: lines read through
//      their OWN dictionary (two libraries, two dictionaries), FTS corpus + search, and a library
//      whose text cannot be decoded is reported with a reason instead of looking attached.
//   C. Rejection reasons instead of silent drops.
//   D. Settings writes: failure is reported not thrown, no slot reuse, concurrent Add is unique.
//   E. Startup connection holder: several clients connect while the data host is "building";
//      their requests are answered once ready; Abandon closes them instead of hanging.
//   F. Host pipe channel: a service that accepts but never answers ends in
//      KitveiHakodeshServiceUnavailableException within the read timeout.
//   G. SeforimDbService.GetAdditionalLibraries reports Attached/Reason truthfully.
//   H. Damaged additional libraries (garbled mid-file, truncated) between healthy ones: the OFFICIAL
//      full-text index is still built and searchable, healthy libraries stay searchable, the damaged
//      ones are named with a reason (log + settings rows). Restart over the result is ready at once.
//   I. A library that passed every check but fails MID-BUILD (changed in place): dropped, index rebuilt.
//   J. Startup holder: a client that connects and sends nothing is closed after a short timeout.
using System.IO.Pipes;
using System.Text;
using KitveiHakodesh.Core.SeforimDb;
using KitveiHakodesh.Core.SeforimDbCatalog;
using KitveiHakodesh.Core.SeforimDbFullTextSearch;
using KitveiHakodesh.Core.Settings;
using KitveiHakodeshLib.Service;
using KitveiHakodeshService.Hosting;
using KitveiHakodeshService.Ipc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace KitveiHakodeshService.Tests
{
    public static class HardeningVerify
    {
        private enum Shape { Plain, SplitCompressed, SplitPlain }

        public static int Run()
        {
            int failures = 0;
            void Fail(string msg) { failures++; Console.Error.WriteLine("  FAIL: " + msg); }
            void Ok(string msg) { Console.WriteLine("  OK: " + msg); }
            void Check(bool cond, string okMsg, string failMsg) { if (cond) Ok(okMsg); else Fail(failMsg); }

            AppSettingsRegistry.InMemoryStore = new();
            string dir = Path.Combine(Path.GetTempPath(), $"hardeningtest-{Environment.ProcessId}");
            Directory.CreateDirectory(dir);
            try
            {
                string primary = Path.Combine(dir, "seforim.db");
                BuildLibrary(primary, "ראשי", "ספר ראשי", "טקסט ראשי", "פרק ראשי", Shape.Plain, altToc: true, regularToc: true);

                // ── A. alt_toc optional ─────────────────────────────────────────────────
                Console.WriteLine("A. optional TOC tables");
                {
                    string noAlt = Path.Combine(dir, "no-alt.db");
                    BuildLibrary(noAlt, "אישי", "ספר ללא אלט", "תוכן ללא אלט", "פרק ללא אלט", Shape.Plain, altToc: false, regularToc: true);
                    string noToc = Path.Combine(dir, "no-toc.db");
                    BuildLibrary(noToc, "קטלוג", "ספר ללא תוכן עניינים", "תוכן בלי טוק", "פרק", Shape.Plain, altToc: false, regularToc: false);
                    var entries = new[] { new SeforimDbAdditionalLibraries.Entry(1, noAlt), new SeforimDbAdditionalLibraries.Entry(2, noToc) };

                    var statuses = SeforimDbAttachedLibraries.ResolveWithStatus(entries);
                    Check(statuses.All(s => s.Rejection is null && s.Library is not null),
                        "libraries without alt_toc_structure / without any TOC tables are attachable (not 'corrupt')",
                        "a library lacking optional TOC tables was rejected: " + string.Join(" | ", statuses.Select(s => s.Rejection)));

                    var libs = SeforimDbAttachedLibraries.Resolve(entries);
                    var skipped = new List<string>();
                    using var cat = new SeforimDbCatalogIndex(Path.Combine(dir, "CatA"), primary, libs) { SourceSkipped = (p, e) => skipped.Add(p + ": " + e.Message) };
                    int docs = cat.BuildAndSwitch(SeforimDbCatalogIndex.ComputeDbHash(primary, libs));
                    Check(skipped.Count == 0, "catalog build does not drop either library (SourceSkipped never fired)",
                        "catalog build skipped a library: " + string.Join(" | ", skipped));

                    var q = new SeforimDbQueries(primary, entries);
                    var books = q.GetAllBooks();
                    var noAltBook = books.Single(b => b.LibraryLabel == "no-alt");
                    var noTocBook = books.Single(b => b.LibraryLabel == "no-toc");
                    Check(cat.Search("ללא אלט", CancellationToken.None).Any(h => h.BookId == noAltBook.Id),
                        "book of the library WITHOUT alt_toc is found by title, with the catalog's global id",
                        "book of the no-alt library not found in the catalog index");
                    Check(cat.Search("פרק ללא אלט", CancellationToken.None).Any(h => h.BookId == noAltBook.Id && h.FullTocPath.Contains("פרק")),
                        "its REGULAR TOC entries are indexed too (only alt-TOC is absent)",
                        "regular TOC entry of the no-alt library missing from the index");
                    Check(cat.Search("ללא תוכן עניינים", CancellationToken.None).Any(h => h.BookId == noTocBook.Id),
                        "a library with no TOC tables at all is indexed by book title",
                        "book of the no-TOC library not found");

                    using var corpus = new CompositeFtsCorpus(primary, libs);
                    Check(corpus.CountDocuments() == 3, "full-text corpus covers primary + both libraries (3 lines)",
                        "FTS corpus count " + corpus.CountDocuments() + ", expected 3");
                }

                // ── B. split / compressed libraries ─────────────────────────────────────
                Console.WriteLine("B. split + compressed libraries");
                {
                    string c1 = Path.Combine(dir, "compressed1.db");
                    string c2 = Path.Combine(dir, "compressed2.db");
                    string sp = Path.Combine(dir, "splitplain.db");
                    BuildLibrary(c1, "דחוס א", "ספר דחוס א", "אלפא דחוס ראשון", "פרק", Shape.SplitCompressed, altToc: false, regularToc: true, dictSeed: 1);
                    BuildLibrary(c2, "דחוס ב", "ספר דחוס ב", "בטא דחוס שני", "פרק", Shape.SplitCompressed, altToc: false, regularToc: true, dictSeed: 2);
                    BuildLibrary(sp, "מפוצל", "ספר מפוצל", "גמא מפוצל פשוט", "פרק", Shape.SplitPlain, altToc: false, regularToc: true);
                    var entries = new[]
                    {
                        new SeforimDbAdditionalLibraries.Entry(1, c1),
                        new SeforimDbAdditionalLibraries.Entry(2, c2),
                        new SeforimDbAdditionalLibraries.Entry(3, sp),
                    };
                    var libs = SeforimDbAttachedLibraries.Resolve(entries);
                    Check(libs.Count == 3, "all three split/compressed libraries attach", "attached " + libs.Count + " of 3");

                    var q = new SeforimDbQueries(primary, entries);
                    var books = q.GetAllBooks();
                    string ReadFirst(string label)
                    {
                        var book = books.Single(b => b.LibraryLabel == label);
                        var lines = q.GetLinesPaged(book.Id, 10, 0);
                        return lines.Count == 1 ? lines[0].Content : "<" + lines.Count + " lines>";
                    }
                    Check(ReadFirst("compressed1") == "אלפא דחוס ראשון", "compressed library 1: GetLinesPaged returns the decoded text (was NotSupportedException)",
                        "compressed1 text: " + ReadFirst("compressed1"));
                    Check(ReadFirst("compressed2") == "בטא דחוס שני", "compressed library 2 is decoded with ITS OWN dictionary (no cross-library dictionary reuse)",
                        "compressed2 text: " + ReadFirst("compressed2"));
                    Check(ReadFirst("splitplain") == "גמא מפוצל פשוט", "split (line_content sidecar, uncompressed) library reads correctly",
                        "splitplain text: " + ReadFirst("splitplain"));
                    Check(q.GetLinesPaged(books.Single(b => b.LibraryLabel == null).Id, 10, 0).Single().Content == "טקסט ראשי",
                        "the primary database is unaffected", "primary text changed");

                    // Repeated reads across pooled connections stay correct (alias/dictionary caching).
                    bool stable = true;
                    for (int i = 0; i < 20; i++)
                        stable &= ReadFirst("compressed1") == "אלפא דחוס ראשון" && ReadFirst("compressed2") == "בטא דחוס שני";
                    Check(stable, "20 alternating reads of two compressed libraries on pooled connections stay correct",
                        "alternating reads produced a wrong text");

                    // Full-text corpus + a real search.
                    using (var corpus = new CompositeFtsCorpus(primary, libs))
                    {
                        var all = corpus.ReadDocumentsInResultOrder(0).ToList();
                        Check(all.Count == 4 && all.Any(d => d.Text == "אלפא דחוס ראשון") && all.Any(d => d.Text == "בטא דחוס שני"),
                            "FTS corpus reads decoded text from both compressed libraries",
                            "FTS corpus docs: " + string.Join(" | ", all.Select(a => a.Text)));
                    }
                    string fidx = Path.Combine(dir, "FtsB");
                    var indexer = new SeforimDbFtsIndexer(fidx, primary, libs);
                    using var engine = new SeforimDbFtsEngine(fidx, primary, libs);
                    Check(indexer.Build(engine), "FTS index builds with compressed libraries configured", "FTS build returned false");
                    var searcher = new SeforimDbFtsSearcher(q);
                    var hits = searcher.SearchInBatches(engine, "בטא", 0, false, 6, false).SelectMany(b => b).ToList();
                    var c2Book = books.Single(b => b.LibraryLabel == "compressed2");
                    Check(hits.Count == 1 && hits[0].BookId == c2Book.Id,
                        "FTS search finds a word that exists only inside a COMPRESSED additional library, with the right book",
                        "FTS hits: " + string.Join(" | ", hits.Select(h => (h.LineId, h.BookId))) + " expected book " + c2Book.Id);

                    Check(SeforimDbAttachedLibraries.CheckTextReadable(c1) is null && SeforimDbAttachedLibraries.CheckTextReadable(sp) is null,
                        "CheckTextReadable accepts healthy compressed/split libraries", "CheckTextReadable rejected a healthy library");

                    // A compressed library whose zstd_dict is EMPTY cannot be decoded: reported, not 'attached'.
                    string broken = Path.Combine(dir, "broken-dict.db");
                    BuildLibrary(broken, "שבור", "ספר שבור", "טקסט שבור", "פרק", Shape.SplitCompressed, altToc: false, regularToc: true, dictSeed: 3);
                    using (var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = broken }.ConnectionString))
                    {
                        conn.Open();
                        Exec(conn, "DELETE FROM zstd_dict");
                    }
                    SqliteConnection.ClearAllPools();
                    string? why = SeforimDbAttachedLibraries.CheckTextReadable(broken);
                    Check(why is not null && why.Contains("unreadable"), "a library with an emptied zstd_dict is reported unreadable with a reason ('" + why + "')",
                        "broken-dictionary library was not flagged: " + (why ?? "<null>"));
                }

                // ── C. rejection reasons ────────────────────────────────────────────────
                Console.WriteLine("C. rejection reasons");
                {
                    string missing = Path.Combine(dir, "nope.db");
                    string notDb = Path.Combine(dir, "notdb.db");
                    File.WriteAllBytes(notDb, Enumerable.Range(0, 4000).Select(i => (byte)(i * 17 + 3)).ToArray());
                    string noBook = Path.Combine(dir, "nobook.db");
                    using (var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = noBook }.ConnectionString))
                    {
                        conn.Open();
                        Exec(conn, "CREATE TABLE category(id INTEGER PRIMARY KEY, parentId INTEGER, title TEXT, level INTEGER)");
                    }
                    SqliteConnection.ClearAllPools();
                    var st = SeforimDbAttachedLibraries.ResolveWithStatus(new[]
                    {
                        new SeforimDbAdditionalLibraries.Entry(1, missing),
                        new SeforimDbAdditionalLibraries.Entry(2, notDb),
                        new SeforimDbAdditionalLibraries.Entry(3, noBook),
                        new SeforimDbAdditionalLibraries.Entry(3, primary), // duplicate index
                    });
                    Check(st[0].Rejection!.StartsWith("file not found"), "missing file -> 'file not found' reason", "missing: " + st[0].Rejection);
                    Check(st[1].Rejection!.StartsWith("cannot be opened"), "non-SQLite file -> 'cannot be opened' reason", "notdb: " + st[1].Rejection);
                    Check(st[2].Rejection!.StartsWith("missing required table"), "missing tables -> names them", "nobook: " + st[2].Rejection);
                    Check(st[3].Rejection == "duplicate library index", "duplicate slot -> explicit reason", "dup: " + st[3].Rejection);
                }

                // ── D. settings writes ──────────────────────────────────────────────────
                Console.WriteLine("D. settings persistence");
                {
                    AppSettingsRegistry.InMemoryStore = new();
                    AppSettingsRegistry.FailWritesForTest = false;
                    string[] paths = Enumerable.Range(0, 8).Select(i => Path.Combine(dir, $"conc-{i}.db")).ToArray();
                    var outcomes = new SeforimDbAdditionalLibraries.AddOutcome[8];
                    Task.WaitAll(Enumerable.Range(0, 8).Select(i => Task.Run(() => outcomes[i] = SeforimDbAdditionalLibraries.Add(paths[i]))).ToArray());
                    var all = SeforimDbAdditionalLibraries.GetAllEntriesRaw();
                    Check(outcomes.All(o => o == SeforimDbAdditionalLibraries.AddOutcome.Added) && all.Select(e => e.Index).Distinct().Count() == 8,
                        "8 concurrent Add() calls -> 8 distinct slots (in-memory store)", "concurrent adds: " + string.Join(",", outcomes));

                    // A lost counter must never cause slot reuse.
                    AppSettingsRegistry.Set("KitveiHakodesh", "Database", "AdditionalPathsNextIndex", "");
                    var again = SeforimDbAdditionalLibraries.Add(Path.Combine(dir, "after-lost-counter.db"));
                    int maxBefore = all.Max(e => e.Index);
                    var newest = SeforimDbAdditionalLibraries.GetAllEntriesRaw().Single(e => e.Path.EndsWith("after-lost-counter.db"));
                    Check(again == SeforimDbAdditionalLibraries.AddOutcome.Added && newest.Index > maxBefore,
                        "a lost/reset counter never re-issues an existing slot (new slot " + newest.Index + " > " + maxBefore + ")",
                        "slot reuse after counter loss: new " + newest.Index + " max before " + maxBefore);

                    int before = SeforimDbAdditionalLibraries.GetAllEntriesRaw().Count;
                    AppSettingsRegistry.FailWritesForTest = true;
                    SeforimDbAdditionalLibraries.AddOutcome failedAdd = default;
                    Exception? addEx = null;
                    try { failedAdd = SeforimDbAdditionalLibraries.Add(Path.Combine(dir, "will-fail.db")); } catch (Exception ex) { addEx = ex; }
                    Check(addEx is null && failedAdd == SeforimDbAdditionalLibraries.AddOutcome.SaveFailed,
                        "Add() with a failing store returns SaveFailed instead of throwing", "Add under failing store: " + (addEx?.ToString() ?? failedAdd.ToString()));
                    bool removeThrewClearly = false;
                    try { SeforimDbAdditionalLibraries.Remove(paths[0]); } catch (SettingsWriteException) { removeThrewClearly = true; }
                    AppSettingsRegistry.FailWritesForTest = false;
                    Check(removeThrewClearly, "Remove() under a failing store throws SettingsWriteException (the dispatcher turns it into an error result)",
                        "Remove did not throw SettingsWriteException");
                    Check(SeforimDbAdditionalLibraries.GetAllEntriesRaw().Count == before, "a failed write left the configured list untouched", "list changed by failed writes");
                    var okAfter = SeforimDbAdditionalLibraries.Add(Path.Combine(dir, "recovered.db"));
                    Check(okAfter == SeforimDbAdditionalLibraries.AddOutcome.Added, "Add() works again once the store recovers", "Add after recovery: " + okAfter);
                    AppSettingsRegistry.FailWritesForTest = true;
                    Check(!AppSettingsRegistry.TrySet("a", "b", "c", "d", out var err) && err is not null, "TrySet reports failure with the cause", "TrySet did not report failure");
                    AppSettingsRegistry.FailWritesForTest = false;
                    AppSettingsRegistry.InMemoryStore = new();
                }

                // ── E. startup connection holder ────────────────────────────────────────
                Console.WriteLine("E. startup listener while the data host builds");
                {
                    string pipeName = "khs-hardening-" + Guid.NewGuid().ToString("N");
                    NamedPipeServerStream NewPipe() => new(pipeName, PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                    // Without the holder, a single held instance (the old behaviour) -> others cannot connect.
                    using (var single = NewPipe())
                    {
                        var wait = single.WaitForConnectionAsync();
                        using var c1 = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                        c1.ConnectAsync(2000).GetAwaiter().GetResult();
                        wait.GetAwaiter().GetResult();
                        // `single` is now connected and nothing re-armed: a second client cannot connect.
                        bool second;
                        try
                        {
                            using var c2 = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                            c2.ConnectAsync(400).GetAwaiter().GetResult();
                            second = true;
                        }
                        catch (TimeoutException) { second = false; }
                        Check(!second, "(baseline) with one connected instance and nothing re-armed, a second client gets 'busy' - the old bug",
                            "baseline: second client unexpectedly connected");
                    }

                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                    var holder = new StartupConnectionHolder(NewPipe, 3, cts.Token);
                    holder.Start();

                    const int clients = 8;
                    var replies = new Task<string>[clients];
                    for (int i = 0; i < clients; i++)
                    {
                        int n = i;
                        replies[i] = Task.Run(async () =>
                        {
                            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                            await client.ConnectAsync(5000); // would throw TimeoutException on PIPE_BUSY
                            await FrameProtocol.WriteFrameAsync(client, Encoding.UTF8.GetBytes("req" + n), CancellationToken.None);
                            var reply = await FrameProtocol.ReadFrameAsync(client, CancellationToken.None);
                            return reply is null ? "<closed>" : Encoding.UTF8.GetString(reply);
                        });
                    }
                    var deadline = DateTime.UtcNow.AddSeconds(10);
                    while (holder.HeldCount < clients && DateTime.UtcNow < deadline) Thread.Sleep(20);
                    Check(holder.HeldCount == clients, $"{clients} clients connected and had their request read while the host was still 'building'",
                        "only " + holder.HeldCount + " of " + clients + " clients were accepted during startup");

                    holder.Ready(async (pipe, request, ct) =>
                    {
                        using (pipe) await FrameProtocol.WriteFrameAsync(pipe, Encoding.UTF8.GetBytes("echo:" + Encoding.UTF8.GetString(request)), ct);
                    });
                    holder.StopAcceptingAsync().GetAwaiter().GetResult();
                    bool allAnswered = Task.WaitAll(replies, 10_000)
                        && replies.Select((r, i) => r.Result == "echo:req" + i).All(x => x);
                    Check(allAnswered, "every held request is answered once the data host is ready", "held requests not answered: " + string.Join(",", replies.Select(r => r.IsCompletedSuccessfully ? r.Result : r.Status.ToString())));

                    // Abandon: clients must not hang.
                    string pipe2 = "khs-hardening-" + Guid.NewGuid().ToString("N");
                    NamedPipeServerStream NewPipe2() => new(pipe2, PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    var holder2 = new StartupConnectionHolder(NewPipe2, 2, cts.Token);
                    holder2.Start();
                    var abandoned = Task.Run(async () =>
                    {
                        using var client = new NamedPipeClientStream(".", pipe2, PipeDirection.InOut, PipeOptions.Asynchronous);
                        await client.ConnectAsync(5000);
                        await FrameProtocol.WriteFrameAsync(client, new byte[] { 1, 2, 3 }, CancellationToken.None);
                        try { return (await FrameProtocol.ReadFrameAsync(client, CancellationToken.None)) is null ? "closed" : "data"; }
                        catch (IOException) { return "closed"; }
                    });
                    deadline = DateTime.UtcNow.AddSeconds(5);
                    while (holder2.HeldCount < 1 && DateTime.UtcNow < deadline) Thread.Sleep(20);
                    holder2.Abandon();
                    holder2.StopAcceptingAsync().GetAwaiter().GetResult();
                    Check(abandoned.Wait(5000) && abandoned.Result == "closed", "Abandon() closes held connections (client sees a closed pipe, not a hang)",
                        "abandoned client: " + (abandoned.IsCompleted ? abandoned.Result : "STILL WAITING"));
                }

                // ── F. host channel read timeout ────────────────────────────────────────
                Console.WriteLine("F. host pipe channel round-trip timeout");
                {
                    string pipeName = "khs-silent-" + Guid.NewGuid().ToString("N");
                    using var stop = new CancellationTokenSource();
                    var server = Task.Run(async () =>
                    {
                        // Accepts and reads the request, then never answers (a hung service).
                        while (!stop.IsCancellationRequested)
                        {
                            var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut,
                                NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                            try { await pipe.WaitForConnectionAsync(stop.Token); } catch { pipe.Dispose(); break; }
                            _ = Task.Run(async () =>
                            {
                                try { await FrameProtocol.ReadFrameAsync(pipe, stop.Token); await Task.Delay(Timeout.Infinite, stop.Token); }
                                catch { }
                                finally { pipe.Dispose(); }
                            });
                        }
                    });
                    var channel = new KitveiHakodeshServicePipeChannel(pipeName, 2000, readTimeoutMilliseconds: 500);
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    Exception? thrown = null;
                    try { channel.CallAsync(new byte[] { 9 }, CancellationToken.None).Wait(15_000); }
                    catch (AggregateException ex) { thrown = ex.InnerException; }
                    sw.Stop();
                    Check(thrown is KitveiHakodeshServiceUnavailableException && sw.ElapsedMilliseconds < 5000,
                        $"a silent service ends in KitveiHakodeshServiceUnavailableException after {sw.ElapsedMilliseconds} ms (was: waits forever)",
                        $"silent service: thrown={thrown?.GetType().Name ?? "none"} after {sw.ElapsedMilliseconds} ms");

                    // An honest answer still passes through.
                    string echoPipe = "khs-echo-" + Guid.NewGuid().ToString("N");
                    var echoServer = Task.Run(async () =>
                    {
                        using var pipe = new NamedPipeServerStream(echoPipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                        await pipe.WaitForConnectionAsync();
                        var req = await FrameProtocol.ReadFrameAsync(pipe, CancellationToken.None);
                        await FrameProtocol.WriteFrameAsync(pipe, req!, CancellationToken.None);
                    });
                    var reply = new KitveiHakodeshServicePipeChannel(echoPipe, 2000, 5000).CallAsync(new byte[] { 7, 7 }, CancellationToken.None).GetAwaiter().GetResult();
                    Check(reply.SequenceEqual(new byte[] { 7, 7 }), "a normal reply is unaffected by the timeout", "echo reply wrong");
                    stop.Cancel();
                    try { server.Wait(2000); } catch { }
                }

                // ── G. service-level status rows ────────────────────────────────────────
                Console.WriteLine("G. SeforimDbService.GetAdditionalLibraries");
                {
                    AppSettingsRegistry.InMemoryStore = new();
                    string good = Path.Combine(dir, "svc-good.db");
                    BuildLibrary(good, "טוב", "ספר טוב", "טקסט טוב", "פרק", Shape.SplitCompressed, altToc: false, regularToc: true, dictSeed: 5);
                    string broken = Path.Combine(dir, "svc-broken.db");
                    BuildLibrary(broken, "רע", "ספר רע", "טקסט רע", "פרק", Shape.SplitCompressed, altToc: false, regularToc: true, dictSeed: 6);
                    using (var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = broken }.ConnectionString))
                    { conn.Open(); Exec(conn, "DELETE FROM zstd_dict"); }
                    SqliteConnection.ClearAllPools();
                    string gone = Path.Combine(dir, "svc-gone.db");
                    SeforimDbAdditionalLibraries.Add(good);
                    SeforimDbAdditionalLibraries.Add(broken);
                    SeforimDbAdditionalLibraries.Add(gone);

                    var service = new KitveiHakodeshService.SeforimDb.SeforimDbService(NullLogger<KitveiHakodeshService.SeforimDb.SeforimDbService>.Instance);
                    var rows = service.GetAdditionalLibraries();
                    var rGood = rows.Single(r => r.Path == good);
                    var rBroken = rows.Single(r => r.Path == broken);
                    var rGone = rows.Single(r => r.Path == gone);
                    Check(rGood.Attached && rGood.Reason is null, "healthy compressed library: Attached=true, no reason", $"good: attached={rGood.Attached} reason={rGood.Reason}");
                    Check(!rBroken.Attached && rBroken.Reason is { } br && br.Contains("unreadable"), "unreadable-text library: Attached=false WITH a reason (no misleading Attached=true)", $"broken: attached={rBroken.Attached} reason={rBroken.Reason}");
                    Check(!rGone.Attached && !rGone.Exists && rGone.Reason is { } gr && gr.StartsWith("file not found"), "missing file: Exists=false, Attached=false, reason given", $"gone: {rGone.Exists}/{rGone.Attached}/{rGone.Reason}");
                    AppSettingsRegistry.InMemoryStore = new();
                }

                // ── H. a damaged additional library never costs the official index ────────
                Console.WriteLine("H. damaged additional libraries vs the primary / healthy libraries' full-text index");
                {
                    AppSettingsRegistry.InMemoryStore = new();
                    string hdir = Path.Combine(dir, "H");
                    Directory.CreateDirectory(hdir);
                    string hPrimary = Path.Combine(hdir, "seforim.db");
                    BuildBulkLibrary(hPrimary, "ראשי", 3);
                    string goodA = Path.Combine(hdir, "good-a.db");
                    BuildBulkLibrary(goodA, "טובא", 400);
                    string garbled = Path.Combine(hdir, "garbled-mid.db");
                    BuildBulkLibrary(garbled, "משובש", 6000);
                    string truncated = Path.Combine(hdir, "truncated.db");
                    BuildBulkLibrary(truncated, "קטוע", 6000);
                    string goodB = Path.Combine(hdir, "good-b.db");
                    BuildBulkLibrary(goodB, "טובב", 400);

                    var gbytes = File.ReadAllBytes(garbled);
                    var rnd = new Random(1);
                    for (int p = gbytes.Length / 2; p < gbytes.Length / 2 + 40000 && p < gbytes.Length; p++) gbytes[p] = (byte)rnd.Next(256);
                    File.WriteAllBytes(garbled, gbytes);
                    var tbytes = File.ReadAllBytes(truncated);
                    File.WriteAllBytes(truncated, tbytes.Take((int)(tbytes.Length * 0.6)).ToArray());

                    // Order matters: healthy libraries sit BEFORE and AFTER the damaged ones.
                    SeforimDbAdditionalLibraries.Add(goodA);
                    SeforimDbAdditionalLibraries.Add(garbled);
                    SeforimDbAdditionalLibraries.Add(truncated);
                    SeforimDbAdditionalLibraries.Add(goodB);

                    var hLog = new List<string>();
                    var coordinator = new SeforimDbFtsIndexCoordinator(
                        hPrimary, Path.Combine(hdir, "fts"), indexIsExternal: false,
                        (level, msg, ex) => { lock (hLog) hLog.Add($"{level}: {msg}" + (ex is null ? "" : " :: " + ex.GetType().Name)); },
                        additionalLibraries: SeforimDbAdditionalLibraries.GetAllEntries());
                    try
                    {
                        var status = WaitForIndex(coordinator, TimeSpan.FromMinutes(3));
                        Check(status.IsReady && !status.IsIndexing && !status.Failed,
                            "official full-text index is built and ready although two attached libraries are damaged",
                            $"index not ready: ready={status.IsReady} indexing={status.IsIndexing} failed={status.Failed}\n    log: {string.Join("\n         ", hLog)}");

                        int Hits(string term) => coordinator.Search(term, 0, 0, false, 6, false).Results.Count;
                        Check(Hits("מילהראשי") >= 1, "search in the OFFICIAL library returns its line", "primary search returned nothing");
                        Check(Hits("מילהטובא") >= 1, "healthy library BEFORE the damaged ones stays attached and searchable", "healthy library A returned nothing");
                        Check(Hits("מילהטובב") >= 1, "healthy library AFTER the damaged ones stays attached and searchable", "healthy library B returned nothing");
                        Check(Hits("מילהמשובש") == 0 && Hits("מילהקטוע") == 0, "the damaged libraries contribute nothing (and searching does not throw)", "damaged library results leaked into the index");
                        Check(hLog.Any(l => l.StartsWith("Warning") && l.Contains("garbled-mid.db")),
                            "the library damaged mid-file is named, with its reason, in the log", "no per-library skip warning in the log:\n    " + string.Join("\n    ", hLog));

                        var service = new KitveiHakodeshService.SeforimDb.SeforimDbService(NullLogger<KitveiHakodeshService.SeforimDb.SeforimDbService>.Instance);
                        var rows = service.GetAdditionalLibraries();
                        var rGarbled = rows.Single(r => r.Path == garbled);
                        var rTruncated = rows.Single(r => r.Path == truncated);
                        var rGoodA = rows.Single(r => r.Path == goodA);
                        Check(!rGarbled.Attached && rGarbled.Reason is { } gr1 && gr1.Contains("full-text") && !rTruncated.Attached && rTruncated.Reason is not null,
                            "settings dialog rows say WHY each damaged library is not searchable (Reason)", $"rows: garbled={rGarbled.Attached}/{rGarbled.Reason} truncated={rTruncated.Attached}/{rTruncated.Reason}");
                        Check(rGoodA.Attached && rGoodA.Reason is null, "the healthy library's row stays Attached with no reason", $"good row: {rGoodA.Attached}/{rGoodA.Reason}");
                    }
                    finally { coordinator.Shutdown(); }

                    // A second start over the finished index is ready at once: no wipe, no rebuild.
                    var coordinator2 = new SeforimDbFtsIndexCoordinator(
                        hPrimary, Path.Combine(hdir, "fts"), false, (_, _, _) => { }, additionalLibraries: SeforimDbAdditionalLibraries.GetAllEntries());
                    try
                    {
                        var st2 = WaitForIndex(coordinator2, TimeSpan.FromMinutes(3));
                        Check(st2.IsReady && !st2.Failed && coordinator2.Search("מילהראשי", 0, 0, false, 6, false).Results.Count >= 1,
                            "restart over the same libraries: official index is ready and searchable again", $"restart: ready={st2.IsReady} failed={st2.Failed}");
                    }
                    finally { coordinator2.Shutdown(); }

                    // ── I. a library that was readable when checked but rots before the build ──
                    Console.WriteLine("I. library that fails mid-build after passing the pre-check");
                    AppSettingsRegistry.InMemoryStore = new();
                    string idir = Path.Combine(dir, "I");
                    Directory.CreateDirectory(idir);
                    string iPrimary = Path.Combine(idir, "seforim.db");
                    BuildBulkLibrary(iPrimary, "ראשי", 3);
                    string victim = Path.Combine(idir, "victim.db");
                    BuildBulkLibrary(victim, "קורבן", 6000);
                    string iGood = Path.Combine(idir, "good.db");
                    BuildBulkLibrary(iGood, "שלם", 400);
                    SeforimDbAdditionalLibraries.Add(victim);
                    SeforimDbAdditionalLibraries.Add(iGood);

                    var first = new SeforimDbFtsIndexCoordinator(iPrimary, Path.Combine(idir, "fts-first"), false, (_, _, _) => { },
                        additionalLibraries: SeforimDbAdditionalLibraries.GetAllEntries());
                    try
                    {
                        var st = WaitForIndex(first, TimeSpan.FromMinutes(3));
                        Check(st.IsReady && first.Search("מילהקורבן", 0, 0, false, 6, false).Results.Count >= 1,
                            "while intact, the library is indexed and searchable", $"intact victim not indexed: ready={st.IsReady}");
                    }
                    finally { first.Shutdown(); }

                    // Damage it IN PLACE keeping its size and timestamp: everything that caches a verdict
                    // per (path, size, mtime) still believes it is healthy.
                    SqliteConnection.ClearAllPools();
                    var stamp = File.GetLastWriteTimeUtc(victim);
                    var vbytes = File.ReadAllBytes(victim);
                    var rnd2 = new Random(7);
                    for (int p = vbytes.Length / 2; p < vbytes.Length / 2 + 40000 && p < vbytes.Length; p++) vbytes[p] = (byte)rnd2.Next(256);
                    File.WriteAllBytes(victim, vbytes);
                    File.SetLastWriteTimeUtc(victim, stamp);

                    var iLog = new List<string>();
                    var second = new SeforimDbFtsIndexCoordinator(iPrimary, Path.Combine(idir, "fts-second"), false,
                        (level, msg, ex) => { lock (iLog) iLog.Add($"{level}: {msg}" + (ex is null ? "" : " :: " + ex.GetType().Name)); },
                        additionalLibraries: SeforimDbAdditionalLibraries.GetAllEntries());
                    try
                    {
                        var st = WaitForIndex(second, TimeSpan.FromMinutes(3));
                        Check(st.IsReady && !st.IsIndexing && !st.Failed,
                            "library failing MID-BUILD does not fail the build: it is dropped and the index is rebuilt without it",
                            $"mid-build failure killed the index: ready={st.IsReady} indexing={st.IsIndexing} failed={st.Failed}\n    log: {string.Join("\n         ", iLog)}");
                        Check(second.Search("מילהראשי", 0, 0, false, 6, false).Results.Count >= 1 && second.Search("מילהשלם", 0, 0, false, 6, false).Results.Count >= 1,
                            "official library and the healthy library are both searchable after the recovery", "post-recovery search missing results");
                    }
                    finally { second.Shutdown(); }
                    AppSettingsRegistry.InMemoryStore = new();
                }

                // ── J. startup holder: a client that connects and sends nothing ─────────────
                Console.WriteLine("J. startup connection holder: silent client");
                {
                    string pipeName = "khs-silentclient-" + Guid.NewGuid().ToString("N");
                    using var cts = new CancellationTokenSource();
                    NamedPipeServerStream NewPipe() => new(pipeName, PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    var holder = new StartupConnectionHolder(NewPipe, 2, cts.Token, null, TimeSpan.FromMilliseconds(400));
                    holder.Start();
                    var silent = Task.Run(async () =>
                    {
                        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                        await client.ConnectAsync(5000);
                        // Sends nothing; waits to see whether the service lets go of it.
                        try
                        {
                            var buffer = new byte[1];
                            using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                            int n = await client.ReadAsync(buffer, wait.Token);
                            return n == 0 ? "closed" : "data";
                        }
                        catch (IOException) { return "closed"; }
                        catch (OperationCanceledException) { return "still-held"; }
                    });
                    Check(silent.Wait(10_000) && silent.Result == "closed",
                        "a client that connects and never sends a request is closed after the short read timeout (frees its listener)",
                        "silent client result: " + (silent.IsCompleted ? silent.Result : "STILL WAITING"));

                    // A well-behaved client is still served normally.
                    var honest = Task.Run(async () =>
                    {
                        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                        await client.ConnectAsync(5000);
                        await FrameProtocol.WriteFrameAsync(client, new byte[] { 4, 2 }, CancellationToken.None);
                        var reply = await FrameProtocol.ReadFrameAsync(client, CancellationToken.None);
                        return reply is null ? "<closed>" : string.Join(",", reply);
                    });
                    var deadline2 = DateTime.UtcNow.AddSeconds(5);
                    while (holder.HeldCount < 1 && DateTime.UtcNow < deadline2) Thread.Sleep(20);
                    holder.Ready(async (pipe, request, ct) => { using (pipe) await FrameProtocol.WriteFrameAsync(pipe, request, ct); });
                    holder.StopAcceptingAsync().GetAwaiter().GetResult();
                    Check(honest.Wait(10_000) && honest.Result == "4,2", "a normal request after the silent one is answered as before", "honest client: " + (honest.IsCompleted ? honest.Result : "STILL WAITING"));
                    cts.Cancel();
                }
            }
            catch (Exception ex)
            {
                Fail("unhandled exception: " + ex);
            }
            finally
            {
                AppSettingsRegistry.FailWritesForTest = false;
                AppSettingsRegistry.InMemoryStore = null;
                SqliteConnection.ClearAllPools();
                try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
            }

            Console.WriteLine(failures == 0 ? "ALL HARDENING CHECKS PASSED" : $"{failures} HARDENING CHECK(S) FAILED");
            return failures;
        }

        // ── Fixtures ────────────────────────────────────────────────────────────────

        private static void BuildLibrary(string path, string category, string book, string line, string toc,
            Shape shape, bool altToc, bool regularToc, int dictSeed = 0)
        {
            using var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ConnectionString);
            conn.Open();
            bool split = shape != Shape.Plain;
            Exec(conn, @"
                CREATE TABLE category(id INTEGER PRIMARY KEY, parentId INTEGER, title TEXT, level INTEGER);
                CREATE TABLE book(id INTEGER PRIMARY KEY, categoryId INTEGER, title TEXT, orderIndex INTEGER,
                    hasTeamim INTEGER, totalLines INTEGER, hasTargumConnection INTEGER, hasReferenceConnection INTEGER,
                    hasSourceConnection INTEGER, hasCommentaryConnection INTEGER, hasOtherConnection INTEGER);
                CREATE TABLE author(id INTEGER PRIMARY KEY, name TEXT);
                CREATE TABLE book_author(bookId INTEGER, authorId INTEGER);");
            Exec(conn, split
                ? "CREATE TABLE line(id INTEGER PRIMARY KEY, bookId INTEGER, lineIndex INTEGER); CREATE TABLE line_content(id INTEGER PRIMARY KEY, content)"
                : "CREATE TABLE line(id INTEGER PRIMARY KEY, bookId INTEGER, lineIndex INTEGER, content TEXT)");
            if (regularToc)
                Exec(conn, @"
                    CREATE TABLE tocText(id INTEGER PRIMARY KEY, text TEXT);
                    CREATE TABLE tocEntry(id INTEGER PRIMARY KEY, bookId INTEGER, parentId INTEGER, textId INTEGER,
                        lineId INTEGER, level INTEGER, hasChildren INTEGER);
                    CREATE TABLE line_toc(lineId INTEGER, tocEntryId INTEGER);");
            if (altToc)
                Exec(conn, @"
                    CREATE TABLE alt_toc_structure(id INTEGER PRIMARY KEY, bookId INTEGER, title TEXT, heTitle TEXT);
                    CREATE TABLE alt_toc_entry(id INTEGER PRIMARY KEY, structureId INTEGER, parentId INTEGER, textId INTEGER, lineId INTEGER);");

            Exec(conn, $"INSERT INTO category VALUES (1, NULL, '{category}', 0)");
            Exec(conn, $"INSERT INTO book VALUES (1, 1, '{book}', 0, 0, 1, 0, 0, 0, 0, 0)");

            if (shape == Shape.Plain)
            {
                Exec(conn, $"INSERT INTO line VALUES (1, 1, 0, '{line}')");
            }
            else
            {
                Exec(conn, "INSERT INTO line VALUES (1, 1, 0)");
                using var cmd = conn.CreateCommand();
                if (shape == Shape.SplitCompressed)
                {
                    var (dict, frame) = Compress(line, dictSeed);
                    Exec(conn, "CREATE TABLE zstd_dict(id INTEGER PRIMARY KEY, dict BLOB)");
                    cmd.CommandText = "INSERT INTO zstd_dict VALUES (1, $d); INSERT INTO line_content VALUES (1, $c)";
                    cmd.Parameters.AddWithValue("$d", dict);
                    cmd.Parameters.AddWithValue("$c", frame);
                }
                else
                {
                    cmd.CommandText = "INSERT INTO line_content VALUES (1, $c)";
                    cmd.Parameters.AddWithValue("$c", line);
                }
                cmd.ExecuteNonQuery();
            }

            if (regularToc)
            {
                Exec(conn, $"INSERT INTO tocText VALUES (1, '{toc}')");
                Exec(conn, "INSERT INTO tocEntry VALUES (1, 1, NULL, 1, 1, 1, 0)");
                Exec(conn, "INSERT INTO line_toc VALUES (1, 1)");
            }
        }

        /// <summary>A real zstd dictionary (trained on synthetic Hebrew-ish samples with a per-seed
        /// vocabulary, so two libraries get DIFFERENT dictionaries with different ids) and a frame
        /// compressed with it.</summary>
        private static (byte[] Dictionary, byte[] Frame) Compress(string text, int seed)
        {
            var rnd = new Random(1000 + seed);
            string[] words = Enumerable.Range(0, 400).Select(i => "מילה" + seed + "x" + i.ToString("x")).ToArray();
            var samples = new List<byte[]>();
            for (int i = 0; i < 4000; i++)
                samples.Add(Encoding.UTF8.GetBytes(string.Join(" ", Enumerable.Range(0, 12).Select(_ => words[rnd.Next(words.Length)])) + " " + i));
            byte[] dict = ZstdSharp.DictBuilder.TrainFromBuffer(samples, 8 * 1024);
            using var compressor = new ZstdSharp.Compressor(3);
            compressor.LoadDictionary(dict);
            byte[] frame = compressor.Wrap(Encoding.UTF8.GetBytes(text)).ToArray();
            return (dict, frame);
        }

        private static FtsIndexStatus WaitForIndex(SeforimDbFtsIndexCoordinator coordinator, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            FtsIndexStatus status = coordinator.Status();
            while (DateTime.UtcNow < deadline)
            {
                status = coordinator.Status();
                if ((status.IsReady && !status.IsIndexing) || status.Failed || status.DbMissing) break;
                Thread.Sleep(100);
            }
            return status;
        }

        /// <summary>A seforim-shaped library whose book has <paramref name="lines"/> lines of bulky
        /// pseudo-random text (so the file spans many pages), each line starting with
        /// "מילה{tag}" - searchable by that token.</summary>
        private static void BuildBulkLibrary(string path, string tag, int lines)
        {
            using (var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ConnectionString))
            {
                conn.Open();
                Exec(conn, @"
                    CREATE TABLE category(id INTEGER PRIMARY KEY, parentId INTEGER, title TEXT, level INTEGER);
                    CREATE TABLE book(id INTEGER PRIMARY KEY, categoryId INTEGER, title TEXT, orderIndex INTEGER,
                        hasTeamim INTEGER, totalLines INTEGER, hasTargumConnection INTEGER, hasReferenceConnection INTEGER,
                        hasSourceConnection INTEGER, hasCommentaryConnection INTEGER, hasOtherConnection INTEGER);
                    CREATE TABLE author(id INTEGER PRIMARY KEY, name TEXT);
                    CREATE TABLE book_author(bookId INTEGER, authorId INTEGER);
                    CREATE TABLE line(id INTEGER PRIMARY KEY, bookId INTEGER, lineIndex INTEGER, content TEXT);
                    CREATE TABLE tocText(id INTEGER PRIMARY KEY, text TEXT);
                    CREATE TABLE tocEntry(id INTEGER PRIMARY KEY, bookId INTEGER, parentId INTEGER, textId INTEGER, lineId INTEGER, level INTEGER, hasChildren INTEGER);
                    CREATE TABLE line_toc(lineId INTEGER, tocEntryId INTEGER);
                    CREATE TABLE alt_toc_structure(id INTEGER PRIMARY KEY, bookId INTEGER, title TEXT, heTitle TEXT);
                    CREATE TABLE alt_toc_entry(id INTEGER PRIMARY KEY, structureId INTEGER, parentId INTEGER, textId INTEGER, lineId INTEGER);");
                Exec(conn, $"INSERT INTO category VALUES (1, NULL, 'קט {tag}', 0)");
                Exec(conn, $"INSERT INTO book VALUES (1, 1, 'ספר {tag}', 0, 0, {lines}, 0, 0, 0, 0, 0)");
                using var tx = conn.BeginTransaction();
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO line VALUES ($i, 1, $i, $t)";
                var pi = cmd.Parameters.Add("$i", SqliteType.Integer);
                var pt = cmd.Parameters.Add("$t", SqliteType.Text);
                var rnd = new Random(5);
                for (int i = 1; i <= lines; i++)
                {
                    pi.Value = i;
                    pt.Value = "מילה" + tag + " " + string.Join(" ", Enumerable.Range(0, 60).Select(_ => rnd.Next(1000000).ToString("x")));
                    cmd.ExecuteNonQuery();
                }
                tx.Commit();
                Exec(conn, $"INSERT INTO tocText VALUES (1, 'פרק {tag}'); INSERT INTO tocEntry VALUES (1, 1, NULL, 1, 1, 1, 0); INSERT INTO line_toc VALUES (1, 1)");
            }
            SqliteConnection.ClearAllPools();
        }

        private static void Exec(SqliteConnection conn, string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
    }
}
