using System.Text.Json;
using KitveiHakodeshService.Catalog;
using KitveiHakodeshService.Dictionary;
using KitveiHakodeshService.HebrewBooks;
using KitveiHakodeshService.Http;
using KitveiHakodeshService.LocalFiles;
using KitveiHakodeshService.SeforimDb;
using KitveiHakodeshService.UserSettings;

namespace KitveiHakodeshService.Ipc;

/// <summary>
/// Routes a decoded RPC request to the owning handler and returns the
/// serialized response envelope. This is the single place ops are registered —
/// each future capability adds one case here.
/// </summary>
public sealed class Dispatcher(
    DocumentLocatorService locator,
    HebrewBooksService hebrewBooks,
    DictionaryService dictionary,
    SeforimDbService seforim,
    FullTextSearchService fts,
    CatalogTocSearchService catalogToc,
    UserSettingsService userSettings,
    HttpHostState httpState,
    LocalFileGrants localFileGrants,
    KitveiHakodeshService.Pdf.WordConversionService wordConversion,
    IHostApplicationLifetime lifetime)
{
    public async Task<byte[]> DispatchAsync(byte[] request, CancellationToken ct)
    {
        IdleMemoryTrimmer.Touch(); // re-arm the idle memory trimmer on every RPC
        RpcRequest? req;
        try
        {
            req = MessagePack.MessagePackSerializer.Deserialize<RpcRequest>(request, MsgPack.Options);
        }
        catch (Exception ex)
        {
            return RpcResponse.Err("Invalid request: " + ex.Message);
        }

        if (req is null || string.IsNullOrEmpty(req.Op))
            return RpcResponse.Err("Missing 'op'.");

        try
        {
            switch (req.Op)
            {
                case "ping":
                    return RpcResponse.Ok(MsgPack.Ser(new PongResult()));

                // The loopback HTTP host's port AND bearer token, handed to the spawner over
                // this PRIVATE pipe (never a file). Awaits the bind so an early call doesn't
                // see 0. The pipe's ACL is what scopes who can get these; the token is then
                // required on every HTTP data request, making the localhost endpoint an
                // enforced boundary. NOTE: this op must never be exposed over HTTP itself —
                // it is, but answering it requires already having the token, so it leaks
                // nothing new to an unauthenticated caller (the 401 gate runs first).
                case "getHttpPort":
                    return RpcResponse.Ok(MsgPack.Ser(new HttpPortResult
                    {
                        Port = await httpState.GetPortAsync(ct),
                        Token = httpState.Token,
                    }));

                // Authorize a local file for serving over GET /file. Fully validates the path
                // (absolute, no traversal, no UNC/device, allowed extension, exists) and mints
                // an unguessable capability handle — the ONLY way a path becomes servable. This
                // op is token-gated on the HTTP host, so a caller must already be authenticated
                // to obtain a handle; GET /file then serves strictly by handle (no raw paths).
                //
                // For HTML files, a FOLDER grant is also minted so the browser can load sibling
                // CSS/JS/image assets. The URL returned uses the folder handle:
                //   /khs-file/<folderHandle>/filename.html
                // This mirrors the hosted mode's SetVirtualHostNameToFolderMapping which already
                // serves the whole containing folder. The file-scoped handle is still returned
                // but is not needed when a folder handle is present.
                //
                // manifest.json next to an HTML file marks it as an Otzaria addin — the Vue
                // HtmlViewPage activates the addin bridge when IsOtzariaAddin is true.
                case "openLocalFile":
                {
                    var a = MsgPack.De<OpenLocalFileArgs>(req.Args);
                    if (!localFileGrants.TryValidateSource(a.Path, out string full, out bool needsConvert, out string error))
                        return RpcResponse.Ok(MsgPack.Ser(new OpenLocalFileResult { Error = error }));

                    // Word-family types are rendered first — PDF via Word, or the Office-free
                    // OOXML→HTML fallback (wiki-style footnotes) when Word is unavailable. The
                    // FileName's extension (.pdf/.html) tells the client which viewer to use.
                    string servePath = full;
                    string fileName = System.IO.Path.GetFileName(full);
                    if (needsConvert)
                    {
                        bool isHtml;
                        try { (servePath, isHtml) = await wordConversion.RenderAsync(full, ct); }
                        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                        {
                            // User pressed ביטול — abort quietly (partial output already deleted).
                            return RpcResponse.Ok(MsgPack.Ser(new OpenLocalFileResult { Cancelled = true }));
                        }
                        catch (Exception ex)
                        {
                            return RpcResponse.Ok(MsgPack.Ser(new OpenLocalFileResult { Error = ex.Message }));
                        }
                        fileName = System.IO.Path.GetFileNameWithoutExtension(full) + (isHtml ? ".html" : ".pdf");
                    }

                    string fileHandle = localFileGrants.Grant(servePath);
                    string servedExt = System.IO.Path.GetExtension(fileName).ToLowerInvariant();
                    bool isHtmlFile = servedExt == ".html" || servedExt == ".htm";

                    // Folder grant for HTML so siblings (CSS/JS/images) load correctly.
                    string folderHandle = "";
                    bool isOtzariaAddin = false;
                    if (isHtmlFile)
                    {
                        string folder = System.IO.Path.GetDirectoryName(full) ?? full;
                        folderHandle = localFileGrants.GrantFolder(folder);
                        // Otzaria addins have a manifest.json in the same directory.
                        isOtzariaAddin = File.Exists(System.IO.Path.Combine(folder, "manifest.json"));
                    }

                    return RpcResponse.Ok(MsgPack.Ser(new OpenLocalFileResult
                    {
                        Handle = fileHandle,
                        FileName = fileName,
                        FolderHandle = folderHandle,
                        IsOtzariaAddin = isOtzariaAddin,
                    }));
                }

                // Show the NATIVE open-file dialog on the service's desktop (dev's replacement for
                // the browser <input type=file>, which yields only a blob — no absolute path, so no
                // reload persistence). Returns just the chosen PATH; the client then authorizes it
                // through the normal openLocalFile op, so the capability model is unchanged —
                // picking a file grants nothing by itself.
                case "pickLocalFile":
                {
                    string? picked = await LocalFiles.NativeFilePicker.PickAsync();
                    return RpcResponse.Ok(MsgPack.Ser(picked is null
                        ? new PickLocalFileResult { Cancelled = true }
                        : new PickLocalFileResult { Path = picked, FileName = System.IO.Path.GetFileName(picked) }));
                }

                // Show the NATIVE browse-for-folder dialog on the service's desktop. The browser has
                // no folder picker that yields an absolute path, so dev could not otherwise feed a
                // real folder to the settings page. Returns only the chosen PATH — no grant is
                // created and no bytes are served as a result of picking.
                case "pickFolder":
                {
                    var a = MsgPack.De<StringArg>(req.Args);
                    string title = string.IsNullOrWhiteSpace(a.Value) ? "בחר תיקייה" : a.Value!;
                    string? picked = await LocalFiles.NativeFolderPicker.PickAsync(title);
                    return RpcResponse.Ok(MsgPack.Ser(picked is null
                        ? new PickFolderResult { Cancelled = true }
                        : new PickFolderResult { Path = picked }));
                }

                // Excluded folders for file search — persisted in excluded_folders.json inside the
                // index directory via the SAME shared ExcludedFoldersPersistence the hosted
                // DocumentLocator service uses, so the file name and format are identical. Applied
                // at search time, so an edit takes effect immediately with no reindex.
                case "getExcludedFolders":
                    return RpcResponse.Ok(MsgPack.Ser(new ExcludedFoldersResult
                    {
                        Folders = locator.GetExcludedFolders(),
                    }));

                case "setExcludedFolders":
                {
                    var a = MsgPack.De<ExcludedFoldersArgs>(req.Args);
                    try
                    {
                        locator.SetExcludedFolders(a.Folders ?? []);
                        return RpcResponse.Ok(MsgPack.Ser(new ExcludedFoldersResult
                        {
                            Folders = locator.GetExcludedFolders(),
                        }));
                    }
                    catch (Exception ex)
                    {
                        return RpcResponse.Ok(MsgPack.Ser(new ExcludedFoldersResult
                        {
                            Folders = locator.GetExcludedFolders(), Error = ex.Message,
                        }));
                    }
                }

                // Hand a local file off to the OS's registered default program (shell-execute) —
                // the dev-mode equivalent of the hosted app's "openInDefaultApp" bridge action.
                // Token-gated like every /rpc op; the path is validated (absolute, canonical, no
                // UNC/device, exists) but ANY extension is allowed since the user is deliberately
                // launching the associated program. No bytes are served over HTTP as a result.
                case "openFileInDefaultApp":
                {
                    var a = MsgPack.De<OpenInDefaultAppArgs>(req.Args);
                    if (!localFileGrants.TryValidateForShellOpen(a.Path, out string full, out string error))
                        return RpcResponse.Ok(MsgPack.Ser(new OpenInDefaultAppResult { Error = error }));
                    try
                    {
                        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = full,
                            UseShellExecute = true,
                        });
                        return RpcResponse.Ok(MsgPack.Ser(new OpenInDefaultAppResult { Ok = true }));
                    }
                    catch (Exception ex)
                    {
                        return RpcResponse.Ok(MsgPack.Ser(new OpenInDefaultAppResult { Error = ex.Message }));
                    }
                }

                // System font families that can render Hebrew — the dev-mode equivalent of the
                // hosted app's "getFonts" bridge action. Hosted asks WPF; this service is
                // native-AOT with no WPF, so HebrewFontsProvider goes to DirectWrite (the API WPF
                // wraps) and applies the SAME test: does the family have a glyph for א? Returns
                // an empty list rather than failing if DirectWrite is unavailable, and the
                // frontend falls back to its canvas probe.
                case "getFonts":
                    return RpcResponse.Ok(MsgPack.Ser(new FontsResult
                    {
                        Fonts = LocalFiles.HebrewFontsProvider.GetHebrewFonts(),
                    }));

                // Open assembled HTML as a new Word document — the dev-mode equivalent of the
                // hosted app's "exportToWord" bridge action (WordExporter.ExportCore). Word
                // imports HTML only by opening a file, so the service writes a temp .html named
                // after the book and opens it; the file stays put because Word holds it open.
                case "exportToWord":
                {
                    var a = MsgPack.De<ExportToWordArgs>(req.Args);
                    if (string.IsNullOrEmpty(a.Html))
                        return RpcResponse.Ok(MsgPack.Ser(new ExportToWordResult { Error = "No HTML to export." }));
                    string? exportError = await DocConvertLib.AotWordPaste.ExportAsync(a.Html!, a.Title ?? "");
                    return RpcResponse.Ok(MsgPack.Ser(exportError is null
                        ? new ExportToWordResult { Ok = true }
                        : new ExportToWordResult { Error = exportError }));
                }

                // Paste the Windows clipboard into Word at the cursor — the dev-mode equivalent
                // of the hosted app's "pasteIntoWord" bridge action (WordExporter.PasteAtCursor).
                // The frontend has ALREADY put the formatted HTML on the clipboard via the copy
                // event, so nothing travels over the wire; this only drives Word. Reuses a running
                // Word instance when there is one and never Quits it, so the user is left looking
                // at their document. Runs on an STA thread inside PasteAsync.
                case "pasteIntoWord":
                {
                    string? error = await DocConvertLib.AotWordPaste.PasteAsync();
                    return RpcResponse.Ok(MsgPack.Ser(error is null
                        ? new PasteIntoWordResult { Ok = true }
                        : new PasteIntoWordResult { Error = error }));
                }

                // Graceful shutdown: triggers host stop → FtsIndexingStarter.StopAsync
                // cancels the build cleanly (aborts any merge, releases the index lock)
                // before the process exits. The dev killer calls this before taskkill so
                // a restart never hard-kills a build in progress (index-corruption path).
                case "shutdown":
                    lifetime.StopApplication();
                    return RpcResponse.Ok(MsgPack.Ser(new ShuttingDownResult()));

                // ── Seforim DB location (settings page / setup wizard) ─────────
                // Persisted in the SAME registry value KitveiHakodeshLib uses
                // (KitveiHakodesh\Database\Path), so the hosted app and this service
                // always agree. Changing it restarts the service (after the reply
                // flushes) so every component re-resolves; the dev courier respawns
                // the service on the next request. A stale FTS index is detected and
                // rebuilt on startup via the fts.ver source-DB marker.
                case "getSeforimDbPath":
                {
                    string p = SeforimDbLocator.Resolve();
                    return RpcResponse.Ok(MsgPack.Ser(new DbPathResult
                    {
                        Path = p,
                        IsCustom = SeforimDbLocator.IsCustom(),
                        Exists = File.Exists(p),
                    }));
                }

                case "setSeforimDbPath":
                {
                    var a = MsgPack.De<DbPathArgs>(req.Args);
                    string p = (a.Path ?? "").Trim().Trim('"');
                    if (string.IsNullOrWhiteSpace(p))
                        return RpcResponse.Ok(MsgPack.Ser(new DbPathResult { Error = "empty path" }));
                    if (!File.Exists(p))
                        return RpcResponse.Ok(MsgPack.Ser(new DbPathResult { Path = p, Error = "file not found" }));

                    SeforimDbLocator.SaveRegistryPath(p);
                    RestartSoon();
                    return RpcResponse.Ok(MsgPack.Ser(new DbPathResult
                    {
                        Path = p, IsCustom = true, Exists = true, Restarting = true,
                    }));
                }

                // Native open-file dialog for the seforim DB, then persist + restart — the dev
                // equivalent of the hosted app's __webviewPickDbPath (which doesn't exist in a
                // browser). Filtered to SQLite files. Cancelling changes nothing; the reply
                // carries Cancelled so the settings page leaves the field untouched.
                case "pickSeforimDbPath":
                {
                    string? picked = await LocalFiles.NativeFilePicker.PickAsync(
                        LocalFiles.NativeFilePicker.DatabaseFilter, "בחר קובץ מסד נתונים");
                    if (picked is null)
                        return RpcResponse.Ok(MsgPack.Ser(new DbPathResult { Cancelled = true }));
                    if (!File.Exists(picked))
                        return RpcResponse.Ok(MsgPack.Ser(new DbPathResult { Path = picked, Error = "file not found" }));

                    SeforimDbLocator.SaveRegistryPath(picked);
                    RestartSoon();
                    return RpcResponse.Ok(MsgPack.Ser(new DbPathResult
                    {
                        Path = picked, IsCustom = true, Exists = true, Restarting = true,
                    }));
                }

                case "clearSeforimDbPath":
                {
                    SeforimDbLocator.ClearRegistryPath();
                    string p = SeforimDbLocator.Resolve();
                    RestartSoon();
                    return RpcResponse.Ok(MsgPack.Ser(new DbPathResult
                    {
                        Path = p, IsCustom = false, Exists = File.Exists(p), Restarting = true,
                    }));
                }

                case "locateDocuments":
                {
                    var args = MsgPack.De<LocateDocumentsArgs>(req.Args);
                    int max = args.Max > 0 ? args.Max : 200;
                    var result = await locator.LocateAsync(args.Query ?? "", max, ct);
                    return RpcResponse.Ok(
                        MsgPack.Ser(result));
                }

                case "locateDocumentsWarmup":
                    locator.Warmup();
                    return RpcResponse.Ok(MsgPack.Ser(new StartedResult()));

                // App-load warm-up: an app that just loaded is about to need the seforim
                // DB — pay the service's one-time cold costs (native lib, first connection,
                // catalog cache, JIT) in the background now, not on the first book click.
                case "dbWarmup":
                    seforim.Warmup();
                    return RpcResponse.Ok(MsgPack.Ser(new StartedResult()));

                case "resetDocumentLocatorIndex":
                    await locator.ReindexAsync(ct);
                    return RpcResponse.Ok(MsgPack.Ser(new ResetResult()));

                case "hbSearch":
                {
                    var args = MsgPack.De<HbSearchArgs>(req.Args);
                    var result = hebrewBooks.Search(args.Query ?? "", args.LocalFolder, args.Limit);
                    return RpcResponse.Ok(
                        MsgPack.Ser(result));
                }

                // Acquire a HebrewBooks PDF ENTIRELY in the service (HttpClient, no browser
                // download interception) and hand back a GET /file capability handle. Lookup
                // order = user's local folder → app cache → download. A missing book (server
                // returns a non-PDF body) comes back as NotFound; a network error as NoInternet.
                // The folder falls back to the shared registry value when the client sends none.
                case "triggerHbDownload":
                {
                    var a = MsgPack.De<HbDownloadArgs>(req.Args);
                    string folder = string.IsNullOrWhiteSpace(a.LocalFolder)
                        ? AppSettingsRegistry.GetHbLocalFolder() : a.LocalFolder!;
                    if (!a.IsOnline)
                    {
                        // No connectivity — only a local/cache hit can satisfy; otherwise report offline.
                        var offline = await hebrewBooks.AcquireAsync(a.BookId ?? "", folder, allowDownload: false, ct);
                        return RpcResponse.Ok(MsgPack.Ser(offline.Path is null
                            ? new HbDownloadResult { NoInternet = true }
                            : new HbDownloadResult { Handle = localFileGrants.Grant(offline.Path) }));
                    }
                    var r = await hebrewBooks.AcquireAsync(a.BookId ?? "", folder, allowDownload: true, ct);
                    if (r.Path != null) return RpcResponse.Ok(MsgPack.Ser(new HbDownloadResult { Handle = localFileGrants.Grant(r.Path) }));
                    if (r.NotFound) return RpcResponse.Ok(MsgPack.Ser(new HbDownloadResult { NotFound = true }));
                    if (r.Error == "cancelled") return RpcResponse.Ok(MsgPack.Ser(new HbDownloadResult { Cancelled = true }));
                    if (r.Error == "network error") return RpcResponse.Ok(MsgPack.Ser(new HbDownloadResult { NoInternet = true }));
                    return RpcResponse.Ok(MsgPack.Ser(new HbDownloadResult { Error = r.Error ?? "download failed" }));
                }

                // Poll the live byte progress of an in-flight HB download (streamed in the service).
                // The frontend calls this every ~300ms while a book tab is in the downloading state
                // and updates the loading text; Active=false means the download already finished.
                case "hbDownloadProgress":
                {
                    var a = MsgPack.De<HbProgressArgs>(req.Args);
                    var p = hebrewBooks.GetProgress(a.BookId ?? "");
                    return RpcResponse.Ok(MsgPack.Ser(p is null
                        ? new HbProgressResult { Active = false }
                        : new HbProgressResult { Active = true, Received = p.Value.received, Total = p.Value.total }));
                }

                // Abort an in-flight HB download (the ביטול button). Trips the per-book cancellation
                // source so the streamed copy unwinds and its .part temp is deleted — a real abort,
                // not just a UI dismiss. Idempotent: reports Ok even when nothing was running.
                case "cancelHbDownload":
                {
                    var a = MsgPack.De<HbProgressArgs>(req.Args);
                    bool cancelled = hebrewBooks.Cancel(a.BookId ?? "");
                    return RpcResponse.Ok(MsgPack.Ser(new HbDeleteLocalResult { Ok = cancelled }));
                }

                // Abort an in-flight Word/document conversion (the ביטול button). Trips the
                // per-source cancellation so RenderAsync discards the result and deletes the partial
                // cache file; Word self-Quits inside the converter, so nothing orphans. Idempotent.
                case "cancelConversion":
                {
                    var a = MsgPack.De<OpenLocalFileArgs>(req.Args);
                    bool cancelled = wordConversion.Cancel(a.Path ?? "");
                    return RpcResponse.Ok(MsgPack.Ser(new HbDeleteLocalResult { Ok = cancelled }));
                }

                // Restore a persisted HB tab: local/cache only, no download. A hit returns a
                // handle; a miss returns Redownload=true so the client re-runs triggerHbDownload.
                case "restoreHbPdf":
                {
                    var a = MsgPack.De<HbDownloadArgs>(req.Args);
                    string folder = string.IsNullOrWhiteSpace(a.LocalFolder)
                        ? AppSettingsRegistry.GetHbLocalFolder() : a.LocalFolder!;
                    var r = await hebrewBooks.AcquireAsync(a.BookId ?? "", folder, allowDownload: false, ct);
                    return RpcResponse.Ok(MsgPack.Ser(r.Path != null
                        ? new HbDownloadResult { Handle = localFileGrants.Grant(r.Path) }
                        : new HbDownloadResult { Redownload = true }));
                }

                case "checkHbLocalFiles":
                {
                    var a = MsgPack.De<HbCheckLocalArgs>(req.Args);
                    string folder = string.IsNullOrWhiteSpace(a.LocalFolder)
                        ? AppSettingsRegistry.GetHbLocalFolder() : a.LocalFolder!;
                    var existing = hebrewBooks.CheckLocalFiles(a.BookIds ?? new(), folder);
                    return RpcResponse.Ok(MsgPack.Ser(new HbCheckLocalResult { ExistingIds = existing }));
                }

                case "deleteHbLocalFile":
                {
                    var a = MsgPack.De<HbDeleteLocalArgs>(req.Args);
                    string folder = string.IsNullOrWhiteSpace(a.LocalFolder)
                        ? AppSettingsRegistry.GetHbLocalFolder() : a.LocalFolder!;
                    var (ok, notFound, error) = hebrewBooks.DeleteLocalFile(a.BookId ?? "", folder);
                    return RpcResponse.Ok(MsgPack.Ser(new HbDeleteLocalResult { Ok = ok, NotFound = notFound, Error = error }));
                }

                // HB local folder in the SHARED registry (same key the hosted AppSettings uses),
                // so dev and the hosted app agree on where books are saved.
                case "getHbLocalFolder":
                    return RpcResponse.Ok(MsgPack.Ser(new StringResult { Value = AppSettingsRegistry.GetHbLocalFolder() }));

                case "setHbLocalFolder":
                {
                    var a = MsgPack.De<StringArg>(req.Args);
                    AppSettingsRegistry.SetHbLocalFolder(a.Value ?? "");
                    return RpcResponse.Ok(MsgPack.Ser(new StringResult { Value = AppSettingsRegistry.GetHbLocalFolder() }));
                }

                // Automatic update check, in the registry value SHARED with the hosted app and the
                // KleiKodesh Word add-in (KleiKodesh\UpdateChecker\TurnOffUpdates) — one toggle
                // governs all three. true = the automatic check is turned OFF.
                case "getTurnOffUpdates":
                    return RpcResponse.Ok(MsgPack.Ser(new BoolResult { Value = AppSettingsRegistry.GetTurnOffUpdates() }));

                case "setTurnOffUpdates":
                {
                    var a = MsgPack.De<BoolArg>(req.Args);
                    AppSettingsRegistry.SetTurnOffUpdates(a.Value);
                    return RpcResponse.Ok(MsgPack.Ser(new BoolResult { Value = AppSettingsRegistry.GetTurnOffUpdates() }));
                }

                // ── Dictionary (KitveiHakodesh_dictionary.db) ──────────────────
                case "dictExact":
                    return RpcResponse.Ok(MsgPack.Ser(dictionary.Exact(Term(req.Args))));

                case "dictPrefix":
                    return RpcResponse.Ok(MsgPack.Ser(new DictSensesResult { Rows = dictionary.Prefix(Term(req.Args)) }));

                case "dictContains":
                    return RpcResponse.Ok(MsgPack.Ser(new DictSensesResult { Rows = dictionary.Contains(Term(req.Args)) }));

                case "dictLinks":
                    return RpcResponse.Ok(MsgPack.Ser(new DictLinksResult { Links = dictionary.Links(Term(req.Args)) }));

                case "dictSynonyms":
                    return RpcResponse.Ok(MsgPack.Ser(new DictWordsResult { Words = dictionary.Synonyms(Term(req.Args)) }));

                case "dictVariants":
                    return RpcResponse.Ok(MsgPack.Ser(new DictWordsResult { Words = dictionary.Variants(Term(req.Args)) }));

                case "dictSpellCandidates":
                    return RpcResponse.Ok(MsgPack.Ser(new DictWordsResult { Words = dictionary.SpellCandidates(Term(req.Args)) }));

                case "dictAbbrevSenses":
                    return RpcResponse.Ok(MsgPack.Ser(dictionary.AbbrevSenses(Candidates(req.Args))));

                case "dictKetivVariants":
                    return RpcResponse.Ok(MsgPack.Ser(new DictWordsResult { Words = dictionary.KetivVariants(Candidates(req.Args)) }));

                // ── Seforim DB — catalog ───────────────────────────────────────
                case "getAllCategories":
                    return RpcResponse.Ok(MsgPack.Ser(new CategoriesResult { Rows = seforim.GetAllCategories() }));

                case "getAllBooks":
                    return RpcResponse.Ok(MsgPack.Ser(new BooksResult { Rows = seforim.GetAllBooks() }));

                case "getBookById":
                {
                    var a = MsgPack.De<BookByIdArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new BookByIdResult { Book = seforim.GetBookById(a.Id) }));
                }

                case "getUserBookFile":
                {
                    var a = MsgPack.De<BookByIdArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new UserBookFileResult { File = seforim.GetUserBookFile(a.Id) }));
                }

                case "getLinesPaged":
                {
                    var a = MsgPack.De<LinesPagedArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new LinesResult { Rows = seforim.GetLinesPaged(a.BookId, a.Limit, a.Offset) }));
                }

                // ── Seforim DB — TOC ───────────────────────────────────────────
                case "getAllTocEntries":
                {
                    var a = MsgPack.De<TocByBookArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new TocEntriesResult { Rows = seforim.GetAllTocEntries(a.BookId) }));
                }

                case "getAltTocStructures":
                {
                    var a = MsgPack.De<TocByBookArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new AltTocStructuresResult { Rows = seforim.GetAltTocStructures(a.BookId) }));
                }

                case "getAllAltTocEntries":
                {
                    var a = MsgPack.De<TocByStructureArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new TocEntriesResult { Rows = seforim.GetAllAltTocEntries(a.StructureId) }));
                }

                case "getTocTitlesForBooks":
                {
                    var a = MsgPack.De<TocTitlesArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new TocTitlesResult { Rows = seforim.GetTocTitlesForBooks(a.BookIds, a.FilterWord) }));
                }

                case "getTocEntryByTextPrefix":
                {
                    var a = MsgPack.De<TocPrefixArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new TocPrefixResult { Rows = seforim.GetTocEntryByTextPrefix(a.BookId, a.Pattern) }));
                }

                // ── Seforim DB — commentary/links ──────────────────────────────
                case "getCommentaryLinksForSourceLineRange":
                {
                    var a = MsgPack.De<LineIdsArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new CommentaryLinksResult { Rows = seforim.GetCommentaryLinksForSourceLineRange(a.LineIds) }));
                }

                case "getLineContents":
                {
                    var a = MsgPack.De<LineIdsArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new LineContentsResult { Rows = seforim.GetLineContents(a.LineIds) }));
                }

                case "getWordLinkAnchorsForLines":
                {
                    var a = MsgPack.De<LineIdsArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(seforim.GetWordLinkAnchorsForLines(a.LineIds)));
                }

                case "getAllConnectionTypes":
                    return RpcResponse.Ok(MsgPack.Ser(new ConnectionTypesResult { Rows = seforim.GetAllConnectionTypes() }));

                case "getDefaultCommentators":
                {
                    var a = MsgPack.De<BookIdArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new DefaultCommentatorsResult { Rows = seforim.GetDefaultCommentators(a.BookId) }));
                }

                case "getReverseLineData":
                {
                    var a = MsgPack.De<ReverseLineDataArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new ReverseLineDataResult { Rows = seforim.GetReverseLineData(a.LineIds, a.TypeIds) }));
                }

                case "getReverseBooks":
                {
                    var a = MsgPack.De<ReverseBooksArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new ReverseBooksResult { Rows = seforim.GetReverseBooks(a.BookId, a.TypeIds) }));
                }

                case "getStaticFilterBooks":
                {
                    var a = MsgPack.De<StaticFilterArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new StaticFilterResult { Rows = seforim.GetStaticFilterBooks(a.SourceBookId, a.TypeIds) }));
                }

                case "getSectionWithCommentary":
                {
                    var a = MsgPack.De<SectionNavArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new SectionNavResult { Rows = seforim.GetSectionWithCommentary(a.MainBookId, a.CommentaryBookId, a.LineIndex, a.Direction != "prev") }));
                }

                case "getTocSectionWithCommentary":
                {
                    var a = MsgPack.De<TocSectionArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new TocSectionResult { Rows = seforim.GetTocSectionWithCommentary(a.MainBookId, a.CommentaryBookId, a.RangePairs, a.Direction != "prev") }));
                }

                case "getLinkTargetForSourceLineAndBook":
                {
                    var a = MsgPack.De<LinkTargetArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new LinkTargetResult { Rows = seforim.GetLinkTargetForSourceLineAndBook(a.SourceLineId, a.TargetBookId) }));
                }

                case "getTocPathsForLines":
                {
                    var a = MsgPack.De<LineIdsArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new TocPathsResult { Rows = seforim.GetTocPathsForLines(a.LineIds) }));
                }

                case "getEnclosingTocPathForLineRanges":
                {
                    var a = MsgPack.De<EnclosingTocPathArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new EnclosingTocPathResult { Rows = seforim.GetEnclosingTocPathForLineRanges(a.Triples) }));
                }

                case "getBookIdsForLines":
                {
                    var a = MsgPack.De<LineIdsArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new LineBooksResult { Rows = seforim.GetBookIdsForLines(a.LineIds) }));
                }

                case "getLineIndexFromLineId":
                {
                    var a = MsgPack.De<LineIdArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new LineIndexResult { Rows = seforim.GetLineIndexFromLineId(a.LineId) }));
                }

                // ── Seforim DB — dictionary sources (מצודת/מלבי״ם/מנחם/ערוך) ────
                case "getBookIdsByTitlePattern":
                {
                    var a = MsgPack.De<TitlePatternArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new BookIdsResult { Rows = seforim.GetBookIdsByTitlePattern(a.Pattern) }));
                }

                case "getBookIdByExactTitle":
                {
                    var a = MsgPack.De<ExactTitleArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new BookIdsResult { Rows = seforim.GetBookIdByExactTitle(a.Title) }));
                }

                case "getLinesWithContentPatternForBooks":
                {
                    var a = MsgPack.De<BoldLinesArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new BoldLinesResult { Rows = seforim.GetLinesWithContentPatternForBooks(a.BookIds, a.Pattern) }));
                }

                case "getLinesWithEitherContentPattern":
                {
                    var a = MsgPack.De<EitherPatternArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new RawLinesResult { Rows = seforim.GetLinesWithEitherContentPattern(a.BookId, a.P1, a.P2) }));
                }

                case "getLineByBookAndLineIndex":
                {
                    var a = MsgPack.De<LineByIndexArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new RawLinesResult { Rows = seforim.GetLineByBookAndLineIndex(a.BookId, a.LineIndex) }));
                }

                // ── User settings (highlights/notes) — read + write ────────────
                case "userSettingsQuery":
                {
                    var a = MsgPack.De<RawSqlArgs>(req.Args);
                    using var pd = ParseParams(a.ParamsJson);
                    string rowsJson = userSettings.QueryRowsJson(a.Sql ?? "", pd.Elements);
                    return RpcResponse.Ok(MsgPack.Ser(new RawRowsResult { RowsJson = rowsJson }));
                }

                case "userSettingsExecute":
                {
                    var a = MsgPack.De<RawSqlArgs>(req.Args);
                    using var pd = ParseParams(a.ParamsJson);
                    long id = userSettings.Execute(a.Sql ?? "", pd.Elements);
                    return RpcResponse.Ok(MsgPack.Ser(new ExecuteResult { LastInsertId = id }));
                }

                // ── Full-text search (FtsLib) ──────────────────────────────────
                case "ftsSearch":
                {
                    var a = MsgPack.De<FtsSearchArgs>(req.Args);
                    var res = fts.Search(a.Query ?? "", a.Cap, a.MaxWordDistance, a.RequireOrdered, a.ContextWords, a.ExpandKetiv);
                    return RpcResponse.Ok(MsgPack.Ser(res));
                }

                case "ftsIndexingStatus":
                    return RpcResponse.Ok(MsgPack.Ser(fts.Status()));

                case "ftsResetIndex":
                    fts.ResetIndex();
                    return RpcResponse.Ok(MsgPack.Ser(new ResetResult()));

                // ── Catalog TOC-path search (Lucene) ───────────────────────────
                case "catalogTocSearch":
                {
                    var a = MsgPack.De<CatalogTocSearchArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(catalogToc.Search(a.Query ?? "")));
                }

                case "catalogTocStatus":
                    return RpcResponse.Ok(MsgPack.Ser(catalogToc.Status()));

                case "catalogTocResetIndex":
                    catalogToc.ResetIndex();
                    return RpcResponse.Ok(MsgPack.Ser(new ResetResult()));

                default:
                    return RpcResponse.Err("Unknown op: " + req.Op);
            }
        }
        catch (OperationCanceledException)
        {
            return RpcResponse.Err("Cancelled.");
        }
        catch (Exception ex)
        {
            return RpcResponse.Err(ex.Message);
        }
    }

    // ── Streaming ops: many response frames pushed over the caller's ONE connection ──
    // The connection is the stream: each emitted frame is a normal {ok,result} envelope,
    // the last one carrying the op's terminal marker, then the service closes the pipe.
    // Client disconnect (broken pipe) is the cancel signal — no cancel op, no polling.

    /// <summary>Handles the streaming ops. Returns false when <paramref name="request"/> is
    /// a regular single-response op (the caller then uses <see cref="DispatchAsync"/>).</summary>
    public async Task<bool> TryDispatchStreamAsync(byte[] request, Func<byte[], Task> writeFrame, CancellationToken ct)
    {
        RpcRequest? req;
        try { req = MessagePack.MessagePackSerializer.Deserialize<RpcRequest>(request, MsgPack.Options); }
        catch { return false; }

        switch (req?.Op)
        {
            // Full-text search: result batches stream until Done.
            case "ftsSearchStream":
            {
                IdleMemoryTrimmer.Touch();
                var a = MsgPack.De<FtsSearchStreamArgs>(req.Args);
                await fts.StreamSearch(
                    a.Query ?? "", a.MaxWordDistance, a.RequireOrdered, a.ContextWords, a.ExpandKetiv,
                    chunk => writeFrame(RpcResponse.Ok(MsgPack.Ser(chunk))), ct);
                return true;
            }

            // Index-build progress: a snapshot per change, ends at the terminal state.
            case "ftsIndexProgressStream":
            {
                IdleMemoryTrimmer.Touch();
                await fts.StreamIndexingProgress(
                    s => writeFrame(RpcResponse.Ok(MsgPack.Ser(s))), ct);
                return true;
            }

            default:
                return false;
        }
    }

    /// <summary>Graceful restart shortly after the current reply flushes: host shutdown
    /// cancels the FTS build cleanly (resumable) and releases the index lock; the dev
    /// courier (or the SCM, when installed) starts a fresh instance that re-resolves
    /// the seforim DB path.</summary>
    private void RestartSoon() =>
        _ = Task.Delay(400).ContinueWith(_ => lifetime.StopApplication());

    private static string Term(byte[]? args) => MsgPack.De<DictTermArgs>(args).Term ?? "";

    private static List<string> Candidates(byte[]? args) => MsgPack.De<DictCandidatesArgs>(args).Candidates ?? [];

    /// <summary>Parse the user-settings <c>paramsJson</c> (a JSON array string carried inside
    /// the msgpack envelope) into JsonElement bind values. The values reference the returned
    /// document's memory, so the caller must keep it alive (a <c>using</c>) until the SQL runs.</summary>
    private static ParsedParams ParseParams(string? json)
    {
        if (string.IsNullOrEmpty(json)) return new ParsedParams(null, []);
        var doc = JsonDocument.Parse(json);
        var arr = doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement.EnumerateArray().ToArray()
            : [];
        return new ParsedParams(doc, arr);
    }

    private sealed class ParsedParams(JsonDocument? doc, JsonElement[] elements) : IDisposable
    {
        public JsonElement[] Elements { get; } = elements;
        public void Dispose() => doc?.Dispose();
    }
}
