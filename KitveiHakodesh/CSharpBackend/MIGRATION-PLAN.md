# KitveiHakodesh.Core — Migration Plan

Consolidating the duplicated logic in `KitveiHakodeshLib` (net48, WebView2 host) and
`KitveiHakodeshService` (net10, dev service) into one shared project: **`KitveiHakodesh.Core`**.

Status: planning complete, no code migrated yet. Baseline branch: `pre-migration-baseline`.

> ## ⚠ THE SERVICE SHIPS AS NATIVE AOT — HOLD THIS THROUGH EVERY SLICE
> `KitveiHakodeshService` sets `PublishAot=true` and references Core, so **everything in
> Core's net10 leg is compiled by ILC**. No runtime IL generation, no reflection-driven
> behaviour. In practice, on every single slice:
> - **no Dapper** — raw ADO (`CreateCommand` / `ExecuteReader`) only
> - **no reflection serialization** — MessagePack attributes / `JsonSerializerContext`
> - **no `Activator.CreateInstance` / `Type.GetType` / `Expression.Compile` / `Emit`**
> - anything that needs those goes on the **net48 leg only** (`<Compile Remove>`)
>
> Full detail and the verified sweep: **rule 0d**.

> **This document is the single source of truth and is LIVING.**
> Every decision, correction or new constraint agreed in discussion is written back into
> this file as part of the same turn — the plan is never allowed to lag behind the
> conversation. If it is not in here, it is not decided. Chat is not a record.

**Naming convention in this document:** Core artifacts are written as
`NewName` (was `OldName`). The rename map is section 6. Renames are **planned only** —
no file has been renamed on disk yet. References to current behaviour, line numbers and
gotchas use the **current** filenames, because those point at real files today.

---

## 1. Core folder structure — THE MAP

Everything else in this document is built on this tree. Each slice in section 10 fills in
one part of it. `[new]` marks a file that does not exist today in any form.

**No `Exceptions/` folder.** Grouping by *kind* is the same mistake as `Models/` — each
exception lives in the folder of the code that throws it, shown at its site below. Still
specific types named for the failure, never one `CoreException`.

```
KitveiHakodesh.Core/
  KitveiHakodesh.Core.csproj          <TargetFrameworks>net48;net10.0-windows</TargetFrameworks>

  (no CoreOptions.cs — DELETED as a concept.) Core discovers its own files via
  Common/AppFileLocator: probe each candidate root in order, take the first that
  EXISTS, fall back to the installer's %LocalAppData%\KleiKodesh. Nothing is passed in.
  The seforim DB path stays with SeforimDbPathResolver (registry -> probe).

  Common/                             reusable — nothing here knows the app exists.
                                      FLAT: no subfolders. The names already say what each
                                      file is, so Sqlite/ Files/ Office/ ... would only group
                                      by kind — the Models/ mistake again
    AppFileLocator.cs               [new] WHERE ARE MY FILES — probes candidate roots, first
                                    that exists wins, %LocalAppData%\KleiKodesh last.
                                    ResolveWritablePath TESTS writability (the portable app
                                    may sit on a USB stick / read-only share)
    SqliteConnectionFactory.cs      [new] per-purpose pooling/pragma policy
                                    + SqliteOpenFailedException, thrown only here.
                                    FIVE policies, not three: CorpusRead, CorpusProbe
                                    (provenance — read-only, unpooled, NO pragmas, so the
                                    check causes none of the churn it exists to see through),
                                    BundledWrite (the catalog updated in place — read-write,
                                    unpooled, and it must NEVER set journal_mode=WAL: that is
                                    a persistent property of the FILE, so one write converts
                                    the shipped database for good and every later reader then
                                    needs write access to a -shm file), SegmentWrite, UserData
    DbFileFingerprint.cs            (was DbChangeStamp) size + mtime + ctime + USN
                                    + file id + -wal
    DbChangeWatcher.cs              file + sidecars, settle-then-confirm, CALLBACK only.
                                    The settle timer is a PRIVATE nested class — it is how
                                    this watcher decides when to fire, not a job of its own,
                                    and it was public only for tests (rule 0c). Its
                                    KHS_DB_WATCH_*_MS env overrides are DELETED (Core reads
                                    no environment) — the windows are ctor params with
                                    production defaults
    RegistryValueWatcher.cs         [new — MISSED BY THE ORIGINAL MAP] RegNotifyChangeKeyValue
                                    on the deepest EXISTING ancestor, so a key that does not
                                    exist yet is still watchable. Rule 11: watching a registry
                                    value is not watching a file, so it is not part of
                                    DbChangeWatcher. Extracted from the same Service class,
                                    which bundled both plus IHostedService, ILogger,
                                    IHostApplicationLifetime and direct references to
                                    FullTextSearchService + CatalogTocSearchService — ALL of
                                    which stay with the orchestrator (rule 3)
    TextEncodingDetector.cs         [new] IsValidUtf8 + charset label + DECODE.
                                    DEVIATION from "never decodes", deliberately: codepage
                                    1255 needs CodePagesEncodingProvider registered on the
                                    modern runtime or GetEncoding(1255) THROWS, and a trap
                                    that only fires on one leg when a user opens one legacy
                                    file should have exactly one home. Both existing callers
                                    (Lib LocalFileHandler, Service HttpHostServer) already do
                                    BOM-sniff + validate + fall back, so decoding here removes
                                    a duplicate rather than adding a job.
                                    Costs System.Text.Encoding.CodePages on the net10 leg
                                    (data tables only — AOT-safe)
    RunningWordFinder.cs            [new] host instance -> already-running (GetActiveObject)
                                    -> newly started, and it REPORTS WHICH: releasing or
                                    quitting the add-in's own Application is how a task pane
                                    loses its Word, so the caller has to be able to tell.
                                    FindRunning() never starts Word — the thesaurus must not
                                    launch Word because someone looked up a synonym
                                                                            [net48 leg]
    WordThesaurus.cs                (was WordThesaurusProvider) autonomous: no running
                                    Word -> empty result                    [net48 leg]
    WordExporter.cs                 SYNCHRONOUS and THROWING: it was Task.Run +
                                    Debug.WriteLine, which is a no-op in Release, so an export
                                    that failed told nobody — not the user, not the log
                                                                            [net48 leg]
    (NO WordToPdfConverter.cs)      Core REFERENCES DocConvertLib instead. That project is
                                    already net48;net10 and IsAotCompatible, and already holds
                                    both routes: AotWordConverter (manual COM/IDispatch,
                                    net10-only behind #if) and the Office-free OoxmlHtmlConverter
                                    on both legs — 1364 working lines. Copying them into Core
                                    would create the second copy rule 0b exists to prevent.
                                    Lib's PIA-based WordToPdfConverter (111 lines) belongs in
                                    DocConvertLib's net48 leg, beside its sibling, NOT in Core
    HebrewFontsProvider.cs          (merges Lib FontsProvider) per-TFM:
                                    WPF (net48) / DirectWrite (net10)
    (the updater is NOT in Common/ — see Updates/ below)
    WordInstallProbe.cs             bitness, version, path, install type, winword PE bitness
    DotNetRuntimeProbe.cs           CLR version, runtime directory
    OperatingSystemProbe.cs         OS version + bitness
    ProcessBitnessProbe.cs          process bitness + executable path
    FileLogger.cs                   (was AppLogger — `App*` banned here). Documented
                                    exception to rule 4 — see rule 10 note. INSTANCE, not
                                    static: the path was a hardcoded %TEMP% const, and two
                                    hosts writing one file interleaved is a worse log than two
    ShellRegistration.cs            HKCU\Software\Classes "Open With" handler;
                                    app values parameterized
                                    (no SqlPlaceholderRewriter — after slices 2 and 4 retire
                                    __webviewQuery/__webviewDictQuery its only caller is
                                    UserAnnotationStore, so the '?'->@p0 rewrite lives there)

  Updates/                            --- app-specific --- CORRECTION to the original map,
                                      which put these in Common/. They cannot go there:
                                      Common's own rule is that nothing in it knows the app
                                      exists, and this code knows the GitHub REPOSITORY, the
                                      installer's FILE NAME and the registry key the installer
                                      stamps. Sibling of Settings/, which is app-specific for
                                      the same reason
    GithubRelease.cs                  response model + a SOURCE-GENERATED JsonSerializerContext.
                                      JSON is correct here and rule 0e is not violated: this is
                                      GitHub's wire format, not one of ours. The generator is
                                      what makes it survive AOT
    UpdateChecker.cs                  is-installed gate, version compare, release query, asset
                                      resolution. Declares UpdateCheckFailedException
    UpdateDownloader.cs               (was DownloadManager) atomic .partial download, size
                                      verification, cross-process mutex, ReadyVersion, launch.
                                      Declares UpdateDownloadFailedException AND
                                      UpdateLaunchFailedException — two different answers: a
                                      failed download is worth retrying later, a failed LAUNCH
                                      points at the file (and the corrupt-exe case deletes it)
                                      The MessageBox at DownloadManager.cs:270 is GONE

  UpdateCheckerLib/UpdateException.cs — READ (rule 10). It was already two types, so the split
  was not the work; the work was that BOTH built a Hebrew sentence for the user inside
  `ToUserMessage()`. Core's versions carry the facts as fields (URL, received/expected bytes,
  native error code) and the host writes the words (rule 3). ~382 UI lines
  (DownloadProgressForm, DownloadProgressWindow, UpdateNotificationForm) stay net48, in the host

  Settings/                           --- app-specific ---
    AppSettingsRegistry.cs            HKCU\Software\VB and VBA Program Settings\...
                                      (Service name wins; Lib AppSettings merged in)
    SeforimDbPathResolver.cs          (was SeforimDbLocator) resolve + persist the DB path.
                                      registry (if the file EXISTS) -> probe Zayit, Otzaria
                                      (first that exists) -> report NOT FOUND. Never returns
                                      a path that is not there; DB_PATH deleted, not migrated
  SeforimDb/
    SeforimDbSqlStrings.cs            (was Sqlite.Strings.cs) SQL STRINGS ONLY
    SeforimDbUnavailableException.cs  no DB configured / not on disk. Its OWN file — thrown
                                      from more than one place (queries, path resolution)
                                      and caught by both orchestrators
    SeforimDbQueries.cs               (was Sqlite.Queries.cs / SeforimDbService + DbAccess)
                                      40 named queries -> typed methods; params + row reads only
                                      (no schema-probe file — optional-column detection is
                                      part of QUERYING: `ColumnExists` and
                                      `_linkHasTargetLineIndex` are already private members
                                      of the query class, used to pick the SQL variant
                                      (`SeforimSql.GetAllCategories(hasOrder)`). Splitting
                                      it out would undo correct co-location)
    SeforimDbContentFingerprint.cs    [MISSED BY THE ORIGINAL MAP — Service
                                      Common/DbContentStamp.cs, 132 lines] "are these the
                                      same ROWS?", the provenance counterpart to
                                      DbFileFingerprint's "did anything touch this file?".
                                      NOT in Common/: it reads `line` and `book` by name, so
                                      it knows this schema and must not pretend to be reusable.
                                      Keeping the two apart is load-bearing — using the file
                                      fingerprint for provenance made EVERY launch a rebuild,
                                      because USN and file id never return to a previous value
                                      once a checkpoint or a copy has bumped them
    SeforimDbModels.cs                29 MessagePack-annotated row types in ONE file —
                                      BookRow, CategoryRow, LineRow, TocEntryRow, …
                                      (attribute-free was the PRE-rule-0e plan; section 5
                                      supersedes it. The *Args and {Rows} envelopes that sat
                                      beside them stay in the Service.)
                                      (~210 lines; SPLIT by domain if it passes ~400)
  SeforimDbFullTextSearch/            (was Search/) FtsLib owns generic full-text search;
                                      everything here is specific to the seforim DB, and
                                      the name must say so. "Search" alone named none of
                                      this repo's five searches (full-text, catalog TOC,
                                      dictionary, HebrewBooks, file-system)
                                      NOTE: there is deliberately NO "SeforimDbFtsIndex"
                                      facade. `SeforimIndex` mixed indexing, searching,
                                      snippets, segment lifecycle and corpus counts behind
                                      one noun — rule 11 splits it by job:
    SeforimDbFtsIndexer.cs            FEEDS the index: asks FtsLib for the last committed
                                      line.id, reads seforim lines after it via
                                      SeforimDbQueries, hands them to FtsLib's writer.
                                      Also owns build PROVENANCE — writes what it built from
                                      (DbFileFingerprint + app version) at build start and
                                      checks it before resuming. Keeps NO resume state of
                                      its own — the index is the only record
    SeforimDbFtsSearcher.cs           SEARCHES the index: Search ONLY (the sole method the
                                      app calls). Ids-only search is an ENGINE capability
                                      and stays in FtsLib; SearchParallel has no caller
                                      (no snippet file here — rendering is FtsLib's, and
                                      FetchNeighborContext is a corpus query that folds
                                      into SeforimDbQueries. See rule 0a)
    SeforimDbFtsModels.cs             the corpus-shaped FTS types in one file (rule 12):
                                      search result (LineId, BookTitle, content) and the
                                      corpus snippet result. Was FtsLib/SeforimDb/
                                      SearchResult.cs + SnippetResult.cs — engine-shaped
                                      results stay in FtsLib/Snippets/
                                      (no stamps file — provenance is part of BUILDING.
                                      The builder writes what it built from at build start
                                      and checks it before resuming; FtsLib stores/compares
                                      the token, Core composes it from DbFileFingerprint +
                                      app version. No batching file either — see below)
    SeforimDbFtsRelatedFormExpander.cs
                                      (was SearchExpansion / SearchExpansionService)
                                      expands to RELATED WORD FORMS — not synonyms,
                                      stemming or wildcards. word -> "word | alt1 | alt2"
                                      via FtsLib's OR syntax. Its 2 one-line queries against
                                      expansion-routed.db stay as consts at the top (rule 9
                                      threshold) — they do not merit their own file
  FileSearch/                         (slice 9) the seam over the DocumentLocator library —
                                      in-process only; the named-pipe client and its service
                                      are deleted, not migrated
  SeforimDbCatalog/                   (was Catalog/) it indexes seforim.db — the name must
                                      say so, exactly like SeforimDbFullTextSearch.
                                      Rule 11 split of CatalogTocIndex.cs (1559 lines,
                                      6+ jobs; see section 6a).
                                      No SQL file here — the catalog reads seforim.db, so its
                                      10 statements go to SeforimDbSqlStrings and the reads
                                      to SeforimDbQueries (rule 9: SQL belongs to the
                                      DATABASE, not the feature)
                                      NOT "Toc" — it indexes book titles + authors
                                      (book, book_author, author), categories, alt_toc
                                      structures AND toc paths. It is a CATALOG; naming the
                                      files after the TOC would understate what they cover
    SeforimDbCatalogIndexer.cs        BuildAndSwitch — AND its provenance: ComputeDbHash /
                                      ActiveHash / staleness compare are the indexer's own
                                      bookkeeping, not a separate "IndexVersion" file
                                      (same noun-vs-job mistake as the FTS stamps)
    SeforimDbCatalogSearcher.cs       (was CatalogTocSearchService) Search — AND its own
                                      reader lifecycle: TryOpenActive, DocCount,
                                      ReleaseIdleReader, Dispose. A reader is not a job, it
                                      is how the searcher holds its resource; the search
                                      service already drives it (TryOpenActive for
                                      readiness, ReleaseIdleResources to let go)
    SeforimDbCatalogAnalyzer.cs       the Lucene Analyzer + its Tokenizer (was
                                      PipelineAnalyzer + PipelineTokenizer). VERIFIED as a
                                      shared contract, not bookkeeping: built at INDEX time
                                      (line 388, into IndexWriterConfig) and used again at
                                      QUERY time — they must be the same analyzer or the
                                      terms never meet. Two callers = its own file
                                      (no collectors file — DocIdSetCollector and
                                      SortKeyCollector are ICollector callbacks constructed
                                      only inside Search (1097, 1145). They are HOW the
                                      searcher collects hits; same answer as the reader)
    SeforimDbCatalogTextNormalizer.cs (was CatalogTocTextRules — "TextRules" says nothing).
                                      The normalization pipeline that folds a query and an
                                      indexed title to a common form: Tokenize/TokenizeQuery,
                                      StripQuoteGlyphs (apostrophe vs gershayim), the
                                      canonical-spelling map, greedy multi-word abbreviation
                                      matching, daf parsing, ה-prefix stripping, skeleton
                                      variants. One job — rule 11 does not split it
    SeforimDbCatalogAbbreviations.cs  275 abbreviations as compiled data — hand-edited, NOT
                                      generated. Slice 6 collapses csv + json + 2 generators
                                      + the staleness target into this one file; the two
                                      invariants the generator enforced (no quote glyph in a
                                      key; no colliding keys) become unit tests
                                      (no TanachBookTitles.cs — it gates the indexer's
                                      verse-extraction pass and has exactly ONE caller, so it
                                      is a private field of SeforimDbCatalogIndexer.
                                      "It isolates Hebrew into one file" was a reason about
                                      MY tooling, not about the code — rule 0)
  Dictionary/
    DictionaryDbQueries.cs      (was DictionaryService) 9 typed methods. Its 8 SQL
                                      statements stay as consts at the TOP of this file —
                                      ~35 lines in a 242-line file buries nothing, and 3 are
                                      already consts there. No separate SQL file (rule 9)
  HebrewBooks/                        (rule 11 split — Search vs Acquire are two jobs)
    HebrewBooksCatalogDbQueries.cs    catalog Search only (was Lib HebrewBooksDb + the
                                      Search half of HebrewBooksService). It IS a catalog —
                                      a listing of books available upstream — so the name
                                      says so, and settles the file-name split too:
                                      HebrewBooksCatalog.db (Service) wins over the vaguer
                                      HebrewBooks.db (Lib). Its 1 statement is built
                                      dynamically and stays inline (rule 9)
    HebrewBooksDownloader.cs          AcquireAsync, Cancel, CheckLocalFiles + hb-cache
    HebrewBooksCatalogUpdater.cs      [RESTORE] keeps the bundled catalog current by scraping
                                      new records from upstream. Was
                                      Lib/HebrewBooks/HebrewBooksCsvUpdater.cs (205 lines),
                                      DELETED 2026-06-02 in 79a4c159 when the catalog moved
                                      CSV -> SQLite and never rewritten. Its registry key
                                      (HebrewBooks\CsvLastUpdated) is still in AppSettings
                                      with NO caller — a live orphan pointing at dead code
  UserAnnotations/                    (was UserSettings/) holds the user's HIGHLIGHTS and
                                      NOTES — user content, not preferences. Preferences are
                                      Settings/AppSettingsRegistry. The old name came from
                                      the DB file and misled on both counts
                                      (no separate SQL file — the ~15-line CREATE TABLE
                                      block is a const at the top of the store, rule 9)
    UserAnnotationStore.cs            (was UserSettingsService + UserSettingsDbAccess)
                                      the ONLY write path in the data layer. Runs
                                      FRONTEND-SUPPLIED SQL — deliberately *Store, not
                                      *Queries, because it owns no queries (slice 1).
                                      REQUIRED header comment: the DB file stays
                                      user_settings.db — see slice 1

  Resources/
    Dictionary.db
    HebrewBooksCatalog.db
```

### FtsLib after slices 4b + 4c — the generic engine, and only that

```
FtsLib/  <TargetFrameworks>net48;net10.0</TargetFrameworks>   (4c merges the Net48 twin in)
  Indexing/                           unchanged, plus:
    IndexWriteLock.cs                 THE one cross-process lock. The Mutex-based
                                      duplicate in FtsIndexState is DELETED (gotcha 13)
    IndexBuildState.cs        [new]   IsReady / IsIndexing / TryStartBuilding /
                                      TryMarkReady / TryMarkIdle / StopAll
    (resume point)                    NO new file, and build.progress is DELETED — the
                                      index already knows. DocSourceMap is persisted per
                                      segment in the segment .db's doc_source table, so
                                      "highest committed docId -> (corpus 0, line.id)" IS
                                      the resume point. Expose it as a query on the store
    (merge-on-complete)               already an FtsLib concern — BuildIndex takes
                                      forceMergeOnComplete and calls _store.MergeAll().
                                      Keep it a build option; Core passes the flag and
                                      does NOT re-implement triggering
    IndexValidator.cs         [new]   ValidateFtsIndex, DeleteFtsIndex
  Search/                             unchanged
  Tokenization/                       unchanged
  Snippets/                           unchanged — and the ONE SnippetResult lives here
  SeforimDb/                          DELETED — all 7 files move to Core
```

FtsLib keeps SQLite for its **own segment storage**; it loses only the corpus dependency.
It must end up with no type that knows about books, line ids, `HeRef`, or `seforim.db`.

### `SeforimDbFullTextSearch/` — what each file does

Every row below is verified against the real public surface / call sites, not inferred from
doc comments.

| File | What it does | Comes from |
|---|---|---|
| `SeforimDbFtsIndexer.cs` | **Feeds** the index — asks FtsLib for the last committed `line.id`, reads seforim lines after it via `SeforimDbQueries`, hands them to the writer. Holds **no** resume state of its own; merge-on-complete stays an FtsLib build option | `SeforimIndex.BuildIndex` + `IndexingPipeline`'s DB half + Lib `FtsIndexBuilder` |
| `SeforimDbFtsSearcher.cs` | **Searches** the index — **`Search` only**. Verified production callers: `FtsSearchExecutor.cs:156`, `FullTextSearchService.cs:387, 489`. `SearchIds` (0 callers) is an *engine* capability — ids-only, no content fetch — and stays FtsLib's; `SearchParallel` (0 callers, one benchmark) is an open decision | `SeforimIndex.cs:375` |
| *(no snippet file)* | Snippet rendering — including the two-pass short-snippet policy — is **FtsLib's** (`Snippets/SnippetBuilder`, `SnippetPipeline`). Core's only part is `FetchNeighborContext`, a corpus query, which folds into `SeforimDbQueries` alongside `CountLines` | rule 0a |
| `SeforimDbFtsModels.cs` | The corpus-shaped FTS types, grouped in one file (rule 12): the hit shape (`LineId`, `BookTitle`, content) and the corpus snippet result. Engine-shaped results stay in `FtsLib/Snippets/` — which also settles the duplicate `SnippetResult.cs` | `FtsLib/SeforimDb/SearchResult.cs` + `SnippetResult.cs` |
| *(no stamps file)* | Index provenance is **part of building**: the builder writes what it built from at build start (`FtsIndexBuilder.cs:64`) and checks it before resuming. FtsLib stores/compares the token like the resume cursor; Core composes it from `DbFileFingerprint` + app version. `ComputeDbStamp` is deleted — a weaker duplicate of `DbFileFingerprint` (path+size+mtime+wal vs. that plus NTFS ChangeTime, USN, file id) | Lib `FtsIndexState.cs:453-522` |
| *(no batching file)* | The two transports batch **differently on purpose** — Service uses a fixed `SnippetBatch = 256` with pipe-write backpressure; Lib uses doubling thresholds + a 200-item cap tuned for first paint over WebView2. Extracting a shared "policy" would invent a commonality that does not exist. Stays inline in Lib | Lib `FtsSearchExecutor.cs:108-119` |
| `SeforimDbFtsRelatedFormExpander.cs` | `word` -> `"word \| alt1 \| alt2"` in **FtsLib's native OR syntax** — hence the `Fts` prefix; it exists only to build FTS queries. Alternates come from `expansion-routed.db`. **Related word forms only** — not synonyms, stemming or wildcards. Its 2 one-line queries stay inline as consts (rule 9 threshold) | Lib `SearchExpansion.cs` + Service `SearchExpansionService.cs` |

**Deliberately NOT here:** search execution, snippet rendering, tokenizing, merging, the
build state machine and the write lock — all FtsLib. The `searchBatch` / `searchComplete` /
`searchCancelled` / `searchError` envelopes and `PushEvent` / `PushProgress` — all Lib
(the Service keeps its own streaming).

**Note on placement:** `SeforimDbPathResolver` sits in `Settings/`, not `SeforimDb/` — it is
a path resolver backed by the same registry hive as `AppSettingsRegistry`, and it is a
dependency of `UserAnnotationStore` (slice 1), well before the SeforimDb slice.

### csproj pattern to copy

From `DocumentLocator.csproj` (the working SDK multi-target precedent in this repo):

- `<PlatformTarget>AnyCPU</PlatformTarget>` — else `BadImageFormatException`
- `<GenerateAssemblyInfo>false</GenerateAssemblyInfo>` if keeping `Properties/AssemblyInfo.cs`
- `<AppendTargetFrameworkToOutputPath>true</AppendTargetFrameworkToOutputPath>`
- explicit `OutputPath` per Configuration/Platform (`bin\Release-$(Platform)\`) — the SDK
  default caused **CS1566** here
- per-TFM `ItemGroup Condition` for references and `<Compile Remove>`
- `<IsAotCompatible Condition="'$(TargetFramework)'=='net10.0-windows'">true</IsAotCompatible>`
  (from `DocConvertLib`)

**FtsLib needs a conditional reference — until slice 4c.** `FtsLib` is net10-only and
`FtsLib.Net48` is v4.8-only: two separate projects sharing `RootNamespace`/`AssemblyName` =
`FtsLib`. One shared Core source tree compiles against either leg. **Slice 4c merges them into
one multi-targeted project**, after which the conditional reference collapses to a plain one.

**Legacy -> SDK reference is proven here:** `DocumentLocator.Service.csproj` (legacy v4.8)
already references `DocumentLocator.csproj` (SDK, `net48;net10.0-windows`).

---

## 2. Target architecture

```
                    KitveiHakodesh.Core
              (all logic — net48 + net10.0-windows)
                    |                    |
          KitveiHakodeshLib      KitveiHakodeshService
        (direct WebView2 access)    (HTTP/IPC to dev)
                    |                    |
                    +---- vue-frontend --+
                       ONE typed API surface
```

Lib and Service are **thin orchestrators**. They must behave identically; the only
permitted difference is transport — Lib talks straight to WebView2, dev goes through
the service. Same API, same shapes, same semantics.

`KitveiHakodeshService` is the structural paradigm. Where the two disagree on shape,
Service wins and Lib is adjusted — with the documented exceptions in section 7.

### Why net48 + net10.0-windows

- **net48** is non-negotiable: the Word VSTO add-in only supports it, and it references
  `KitveiHakodeshLib` directly (`KleiKodeshVsto.csproj:417`).
- **net10.0-windows**, not plain `net10.0`: `DocumentLocator`'s net10 leg is
  `net10.0-windows`, and a plain `net10.0` project cannot reference it. Registry access
  also needs Windows. This matches the Service exactly.

---

## 3. The rules

> ### RULE 0 — the governing rule
> **Code must be easy to understand and easy to manage.**
>
> Every rule below is an *instrument* for that, never an end in itself. When a rule and
> rule 0 disagree, **rule 0 wins**. Before applying any rule mechanically, ask: does this
> make the code easier to understand and manage? If not, do not do it.
>
> This is the test I keep failing by applying rules by reflex:
> - a `RelatedFormExpansion/` folder around 2 files — more to navigate, nothing gained
> - a `*SqlStrings.cs` file for 2 one-line queries — forces a file-jump to read one query
> - `AppAgnostic/`, `InjectedPaths`, `Rows` instead of `Common/`, `CoreOptions`, `Models` —
>   invented words are harder to understand than the conventional ones
>
> Each was "correct" by a sub-rule and wrong by rule 0.

0f. **Cleanup happens AFTER the migration — but a move is not done until the old copy is gone.**
   Two different things, and only one of them defers:
   - **Completes a move (stays in its slice):** once the merged code lands in Core, the
     originals are deleted in the *same* slice. Leaving both is not "deferred cleanup", it is
     an unfinished migration and a live duplicate (rule 0b). Same for retiring a bridge action
     once its typed replacement exists.
   - **Independent cleanup (deferred to slice 11):** dead files, stale scripts, unused
     assets, build plumbing that outlived its purpose. None of it blocks a slice and all of
     it would bloat the diffs that reviewers need to read.
   When unsure: *would leaving this in place mean the same logic exists twice?* If yes it is
   part of the move. If it is merely unused, it waits.

0e. **MessagePack is the ONLY wire format. No JSON on any wire, anywhere.**
   One format end-to-end: Core -> Service/Lib -> frontend. **Core is MessagePack-native.**

   **The trap this rule exists to prevent:** decoding MessagePack and re-encoding the same
   payload as JSON (or the reverse) somewhere in the middle. It is invisible in a diff,
   costs a full serialize+parse per hop, and is exactly what happens if one layer is
   converted and its neighbour is not. **If a payload changes format between two hops,
   that is a bug.** Trace every path end-to-end, not layer by layer.

   **Sensible exceptions — JSON stays where it is genuinely better:**
   - human-editable files (`catalog_abbreviations.json`, config, `terms/batch_*.json`)
   - anything a person reads: logs, diagnostics dumps, `--dry-run` output
   - third-party APIs that only speak JSON (e.g. the GitHub releases API)

   The line: **wire = MessagePack, at-rest-and-human-readable = JSON.** If unsure, ask
   whether a human ever reads it. If never, MessagePack.

   ⏰ **POST-MIGRATION REMINDER — a separate, app-wide refactor (see slice 10).** Today the
   hosted bridge is JSON (`JsBridge` posts JSON; `RpcJsonContext` registers 107 types), so
   this cannot land in one slice. Core is built MessagePack-first **now** so it never has to
   be redone; the sweep across Lib, the Service and the frontend follows.

0d. **The Service ships as NATIVE AOT — so Core's net10 leg must be AOT-clean.**
   `KitveiHakodeshService` sets `PublishAot=true`. It references Core, so anything Core's
   net10 leg contains is compiled by ILC. **No runtime code generation, no reflection-driven
   behaviour.** Consequences that bind this plan:
   - **NO DAPPER.** Dapper emits its materializers as IL at runtime — that cannot work under
     native AOT (hence the separate source-generated `Dapper.AOT` package). Use **raw ADO**:
     `CreateCommand` / `ExecuteReader`, exactly what the Service already does everywhere.
     Lib's Dapper sites — `DbAccess`, `HebrewBooksDb`, `UserSettingsDbAccess` — are rewritten
     as they merge. No extra work: they are merging into the Service's raw-ADO versions anyway.
   - **Serialization must be source-generated.** MessagePack needs `[MessagePackObject]` on
     the type (section 5); `System.Text.Json` needs a `JsonSerializerContext`.
   - **Lucene** discovers codecs by reflection (`NamedSPILoader` + `Activator.CreateInstance`)
     -> `<TrimmerRootAssembly Include="Lucene.Net" />` **stays in the Service csproj**.
   - **Office Interop is not AOT-safe** -> net48 leg only, via `<Compile Remove>`. The Service
     uses manual COM/IDispatch instead.
   - Prefer source-generated `LibraryImport` over `DllImport` for new P/Invoke.
   - Set `IsAotCompatible` on Core's net10 leg so warnings surface when **Core** builds,
     not at the Service's publish.

   *Swept and verified clean:* no `Activator.CreateInstance`, `Type.GetType`,
   `Expression.*`, `MakeGenericType`, `Assembly.Load` or IL `Emit` in Lib's Core-bound code
   (one comment-only hit) or in FtsLib's net10 leg. **Dapper is the only real blocker.**

0c. **Core's public surface is set by PRODUCTION callers, never by tests.**
   A method earns its place in Core because the app calls it. Never widen Core so a test
   can reach an internal — the test calls the layer that owns the thing (FtsLib for engine
   behaviour, Core for corpus behaviour). Tests adapt to the code, not the reverse.
   Worked example: `SeforimDbFtsSearcher` exposes `Search` (3 production call sites) and
   NOT `SearchIds` (0 production, engine capability, tested in `FtsLibTest`).
   Corollary: test projects that break because code moved are refactored **in the same
   slice** — `FtsLibTest` (27 files on `SeforimIndex`) in 4b, `KitveiHakodeshService.Tests`
   (`Catalog`, `DbChangeStamp`) in 6 and 7.

0b. **ONE copy of every shipped database. No duplicates, anywhere, ever.**
   A binary asset lives in exactly one place — `Core/Resources/` — and every consumer
   reaches it through Core. No "keep them identical" rule, no importer writing two files,
   no second copy under `public/`. If a consumer seems to need its own copy, that is a
   delivery problem to solve, not a copy to create. Today: `HebrewBooks.db` has **3**
   copies (one an orphan in no csproj) and `Dictionary.db` has **2** (one with no reader).
   After migration: one each.

0a. **The LOGIC lives in FtsLib. Core supplies data and CALLS it.**
   FtsLib must never open `seforim.db` — it receives documents to index, and returns hits
   and snippets. It knows nothing about books, line ids, `HeRef`, or where text came from.
   But the inverse matters just as much: **Core must not re-implement or wrap engine
   algorithms.** If Core finds itself holding search, ranking, snippet or merge logic, that
   logic is in the wrong project.
   The recurring shape — three times so far — is *engine algorithm + one corpus query*,
   which looks like Core logic and is not:
   - `CountLines` -> FtsLib needs a count; the query is `SeforimDbQueries`
   - short-snippet re-render -> rendering is FtsLib's `SnippetPipeline`; only
     `FetchNeighborContext` is a corpus query
   - build/merge -> FtsLib's; only "which lines to feed" is Core's
   Split them at that seam every time. See slice 4b — rule 0a is violated today.

1. **Core knows nothing about the orchestrators.** No `using KitveiHakodeshService.Ipc`,
   no Lib types, no transport types, no wire attributes.
2. **Core is self-sufficient — it FINDS its own files, it is not told where they are.**
   Core reads **no environment variables**. It also takes no injected path bag: there is no
   `CoreOptions`. Every file lookup goes through `Common/AppFileLocator`, which probes
   candidate roots in order and takes the first that **exists**.

   2a. **Why probing rather than injection.** No single expression is right in all three hosts:
   the **service** keeps data next to its binary on purpose; the **VSTO add-in** is
   shadow-copied, so `Assembly.Location` is a temp folder with no data and
   `AppDomain.BaseDirectory` is WINWORD's folder; the **portable DemoApp** runs from a USB
   stick or share whose path changes per run and may be read-only. Probing answers all three
   with one mechanism and nothing to pass in. Last resort is the installer's own per-user root,
   `%LocalAppData%\KleiKodesh` — keep it in step with `AddinInstaller.InstallPath`.

   2b. **Reading and writing are different questions.** `FindFile`/`FindDirectory` locate what
   already exists; `ResolveWritablePath` picks somewhere that can actually be written, and
   **tests** it — a portable app on read-only media looks fine until the first write. Prefer a
   root that already holds the item so an index is updated in place instead of a second copy
   appearing elsewhere.
3. **Core has no UI.** No `MessageBox`, no dialogs, no windows. It returns data.
4. **Core returns data or throws.** It never swallows errors (`Debug.WriteLine` is a no-op
   in Release). Orchestrators catch, decide severity, and surface to the frontend.
5. **Reusability test:** anything that does not need to know about the KitveiHakodesh app
   *may* live in Core — this is additive. Core holds both app-specific data logic and a
   generic reusable toolkit. Keep the two visibly separated (see `Common/`).
6. **Culture:** `InvariantGlobalization=true` is set on the Service leg only. Shared Core
   code must specify `Ordinal`/`Invariant` explicitly — never rely on culture-sensitive
   defaults (`StartsWith(string)`, `EndsWith(string)`, `IndexOf(string)`, `string.Compare`).
7. **Async:** Core exposes async APIs and never blocks. The Lib leg runs on a WinForms UI
   thread that also services WebView2 accelerator keys; blocking there deadlocks.
8. **No `*Service` suffix in Core.** "Service" is an orchestrator word; Core is a library.
9. **SQL strings live in their own file.** Each area gets an `<Subject>DbSqlStrings.cs` holding SQL
   constants **only**; the query class maps parameters and reads rows, nothing else.
   This is already the Service's documented convention for SeforimDb — `Sqlite.Strings.cs`
   says *"SQL strings … kept in this file ONLY, separate from the query logic … mirroring
   the frontend's `queries.sql.ts` / `seforimDb.ts` split. Add new SQL here; add the method
   that runs it in `Sqlite.Queries.cs`."*
   **SQL belongs to the DATABASE, not to the feature.** One `<Subject>DbSqlStrings.cs` +
   `<Subject>DbQueries.cs` per database, and **every** consumer of that database goes
   through them — no feature keeps its own SQL for a DB someone else already owns.
   The catalog is the worked example: it reads `line`, `tocEntry`, `tocText`, `category`,
   `book`, `book_author`, `author`, `alt_toc_structure` — all **seforim.db** — so its 10
   statements merge into `SeforimDbSqlStrings` and its reads into `SeforimDbQueries`.
   Several are outright duplicates of queries that already live there.

   **Threshold — measure DOMINANCE, not statement count.** A separate SQL file earns its
   place when the SQL would bury the code around it. Otherwise `const`s at the **top of the
   file that uses them** are clearer, because the query sits beside its caller.
   (An earlier draft said "5+ statements" — a count is the wrong measure: 8 short SELECTs in
   a 242-line file bury nothing, and splitting them forces a file-jump to read one query.)
   - **Own file: seforim.db only** — 29 consts / **379 lines** of pure SQL, plus the
     catalog's 10. It dwarfs any single caller, and the Service already separates it
   - **`const` at top of the queries file:** Dictionary.db (8 statements, ~35 lines in a
     242-line file — and 3 are already consts there), user_settings.db (one ~15-line
     `CREATE TABLE` block)
   - **Inline:** expansion-routed.db (2 one-liners), HebrewBooks.db (1, built dynamically —
     a const file would not fit it anyway)
10. **THE TEST: would ANY developer know what this is, at a glance, without opening it?**
    If no, rename it. Everything below only serves that test.

    **(a0) This applies to the CODE, not just files and folders** — types, methods,
    parameters, fields, properties. A filename is only the outermost name; a well-named file
    full of cryptic members has not met the rule. Real examples from this codebase:
    - `TryOpenActive` -> `TryOpenActiveIndex` — active *what*?
    - `ReadVer` -> `ReadVersion` — never abbreviate to save four characters
    - `_db` / `_wikiDb` (`DictionaryHandler`) -> `_dictionaryDb` / `_wikiDictionaryDb`
    - `ComputeDbStamp` -> the "stamp" noun again; it computes a **fingerprint**
    - `_ParseParams` vs `ParseParamsStatic` — two conventions for one idea in one codebase

    Proportion: a name's clarity must match its **reach**. Public/internal API, fields and
    parameters travel far, so they get the full treatment; a loop counter inside three lines
    can be `i`. The test is whether a reader meets the name far from its definition.

    **(a) Every component must mean something on its own.** `Seforim` alone means nothing —
    the subject is the **`SeforimDb`**. Never shorten a name to a fragment that cannot stand
    by itself. The existing `SeforimDbPathResolver` is the model.

    **(a2) Use the CONVENTIONAL word for the thing.** Standard words are obvious *because*
    they are standard: `Models`, `Options`, `Factory`, `Provider`, `Resources`, `Common`,
    `Store`, `Repository`, `Extensions`, `Exceptions`, `Builder`, `Reader`, `Converter`.
    **Never avoid one, and never substitute an invented synonym** — that makes the name
    less obvious, not more. `SeforimDbModels.cs` is right; `SeforimDbRows.cs` is wrong
    ("Rows" is internal jargon). Full subject **+** conventional word, then stop.

    **(a3) If a shorter term is JUST AS CLEAR, use it.** Clarity sets the floor, not the
    length. Do not pad a name with words that add no information:
    - `IndexBuilder` -> **`Indexer`** — same meaning, one word
    - `CatalogTocLuceneIndexBuilder` -> **`SeforimDbCatalogIndexer`** — "Lucene" is the
      implementation, not the job
    **Shorten only when nothing is lost — and check the shorter word is still TRUE.**
    `SeforimDbCatalogToc*` looked like harmless redundancy to trim to `SeforimDbToc*`, but
    the index also covers book titles, authors and categories, so "Toc" *understated* it.
    Dropping "Catalog" was the wrong cut; dropping "Toc" was the right one.
    Likewise `SeforimDb` never shortens to `Seforim`, and `Converter`/`Resolver`/`Detector`
    stay when they carry the verb.

    **(b) Follow the conventions of the language/framework the file lives in.**
    This repo is not C#-only — each stack keeps its own idiom, and C# habits must not be
    imposed on the others:

    | Stack | Convention |
    |---|---|
    | **C#** (Core, Lib, Service, VSTO) | PascalCase types + filenames; `Factory`, `Provider`, `Options`, `Models`, `Resources`, `Common`, `Extensions`, `Exceptions` are all correct |
    | **TypeScript / Vue** (`vue-frontend`) | camelCase modules (`seforimApi.ts`); `use*` composables (`useBookCatalogSearch.ts`); `*Store.ts` for Pinia; PascalCase `.vue` components (`ToastBanner.vue`); `*.sql.ts` for SQL modules |
    | **Python** (`scripts/`, `tools/`) | snake_case modules (`gen_catalog_abbreviations.py`) |

    The bar is clarity, not novelty. **Do not invent new taxonomy in any stack.**

    **(c) What to fix** is a name that hides or under-specifies the job:
    - `DownloadManager` -> `UpdateDownloader` (says what it downloads)
    - `SeforimQueries` -> `SeforimDbQueries` (names the real subject, and how)
    - `DictionaryDb` -> `DictionaryDbQueries` (bare `Db` says nothing)
    - `DbChangeStamp` -> `DbFileFingerprint` (its own doc calls it a fingerprint)
    **(d) Two hard bans**, both codebase-specific rather than stylistic — and both C#-side
    only, since they describe *this* architecture, not naming taste:
    - **`*Service` in Core** — it is an orchestrator word, and Core is a library.
      (`*Handler` stays correct in Lib: those really are message handlers. `*Service` also
      stays correct in `KitveiHakodeshService`, which really is one.)
    - **`App*` prefix in `Common/`** — contradicts the reusability rule.

    **(e) Pair convention** (C# side) names the FULL subject — the database, not the topic:
    **`<Subject>DbSqlStrings.cs`** (constants) + **`<Subject>DbQueries.cs`**
    (executes them, maps rows). `SeforimDb`, not `Seforim` — **`Seforim` alone means
    nothing**; matches the existing `SeforimDbPathResolver`. SQL is never inline.
    A single catch-all exception is under-specified — throw types named for the failure.
    **Put each exception in the folder of the code that THROWS it**, never in an
    `Exceptions/` folder: grouping by kind is the same noun-folder mistake as `Models/`.
    One thrower + a tiny type -> declare it in that file (rule 12); several throwers ->
    its own file, beside them.

11. **If a name will not resolve cleanly, the file must be SPLIT — but split by JOB, never
    by NOUN.** An un-nameable file is doing more than one job; the naming test *is* the
    cohesion test. Apply it before the move, not after.

    **The trap:** a noun in a class — *stamps, version, policy, state, probe, result* — is
    usually **bookkeeping owned by a job**, not a job itself. Ask: does this file DO
    something, or is it a thing another file's job maintains?
    - provenance stamps -> the **builder** writes what it built from. Part of building
    - index version / staleness hash -> same. Part of building
    - optional-column detection -> the **queries** consult it to pick the SQL variant.
      Part of querying (already a private member there — splitting it out *undoes*
      correct co-location)
    - an index **reader** (open / doc-count / release / dispose) -> that is how the
      **searcher** holds its resource. Part of searching
    - snippet rendering -> the **engine's**; only the corpus fetch was ours

    **The inverse trap:** merging things that are deliberately different. Lib and the Service
    batch results differently on purpose (fixed 256 + pipe backpressure vs. doubling
    thresholds + a 200 cap for first paint over WebView2) — a shared "policy" would invent a
    commonality that does not exist.

    **The test that actually catches it: WHO CALLS THIS, AND WOULD ANYONE ELSE EVER?**
    Exactly one caller means it is that caller's bookkeeping — merge it. Do not infer
    cohesion from method names clustering together in the source; `TryOpenActive` /
    `DocCount` / `ReleaseIdleReader` / `Dispose` look like a unit only because they are all
    plumbing for one job.

    **Deliberate exemptions** (one caller, kept separate on purpose — say so, do not let them
    look like oversights):
    - `<Db>SqlStrings.cs` / `<Db>SchemaSql.cs` — rule 9 keeps SQL out of the query class by
      the Service's own documented convention
    - `SeforimDbFtsRelatedFormExpander` — one caller (the searcher), but a self-contained
      algorithm with its own database, independently testable. A job, not bookkeeping
    - data files (`TanachBookTitles`, `*.g.cs`) — data, not jobs

    Section 6a lists the splits this rule forced; the notes above list the ones it prevented.

12. **Group tiny data-only types; split types that carry behaviour.** One-type-per-file is a
    StyleCop rule (SA1402/SA1649), it is **opt-in, and this repo has not opted in** — there is
    no `.editorconfig`, no StyleCop, no analyzer. The house convention is already grouping:
    `SeforimModels.cs` holds 79 types in 432 lines, `Ipc/Rpc.cs` holds 45 in 557.
    So a set of small DTOs read as one contract belongs in one well-named file
    (`SeforimDbModels.cs`), and a folder holding a single file is a container for nothing.
    **A folder that exists only to disambiguate a vague name is dead weight once the name
    is fixed** — `SeforimDbFtsRelatedFormExpander.cs` needed no `RelatedFormExpansion/` folder.
    Fix the name first, then see whether the folder still earns its place.
    State a size trigger rather than guessing later: **split by domain past ~400 lines.**

---

## 4. Layer map — what goes where

### Into Core

| Area | Core name | Source |
|---|---|---|
| User annotations (highlights + notes) | `UserAnnotationStore` | Lib `UserSettingsDbAccess` + Service `UserSettingsService` |
| Registry value accessors | `AppSettingsRegistry` | Lib `Settings/AppSettings` + Service `UserSettings/AppSettingsRegistry` |
| Seforim DB path resolve + persist | `SeforimDbPathResolver` | Service `SeforimDbLocator` (Lib's `ResolveDefaultDbPath` is a verbatim duplicate) |
| Seforim SQL strings | `SeforimDbSqlStrings` | Service `Sqlite.Strings.cs` (**already separated**, 29 consts) + frontend `queries.sql.ts` |
| Seforim query logic | `SeforimDbQueries` | Service `SeforimDbService` (`Sqlite.Queries.cs`) + Lib `DbAccess` |
| 27 domain row types | `SeforimDb/Rows/` | Service `SeforimModels.cs` (attribute-free subset) |
| FTS core (~1235 lines, already UI-free) | `FtsIndexState`, `FtsIndexBuilder`, `FtsSearchExecutor`, `SearchExpansion` | Lib (names unchanged) + Service `FullTextSearchService`, `SearchExpansionService` |
| Catalog TOC (Lucene) | `SeforimDbCatalog/*` — builder, reader, searcher, tokenizer, text rules | Service `Catalog/*` (2586 lines) — **Lib has none today** |
| Dictionary (9 typed methods) | `DictionaryDb` | Service `DictionaryService` |
| HebrewBooks catalog | `HebrewBooksDb` | Lib `HebrewBooksDb` + Service `HebrewBooksService` |
| DB change detection | `DbFileFingerprint`, `DbChangeWatcher` | Service `Common/*` |
| **Reusable toolkit** | see section 1 (`Common/`) | SQLite native loading, update checking, Office/Word COM, fonts, env probes, logging, shell registration |
| **Bundled databases** | `Core/Resources/` | `Dictionary.db`, `HebrewBooksCatalog.db` |

File search (`DocumentLocatorAdapter` in Lib, `DocumentLocatorService` in the Service) lands
in **slice 9**, which also absorbs the `DocumentLocator` library itself into this repo and
deletes the pipe/service architecture entirely.

### Stays in KitveiHakodeshLib (WebView2 / chrome orchestration)

`AppViewer*`, `ChromeTabsMirror`, `SplashOverlay`, `JsBridge`, `WebBridge`,
all `*Handler` classes (`DbHandler` takes a `WebView2` in its ctor; `HebrewBooksHandler`
intercepts `DownloadStarting`), `LocalFileHandler`'s per-file WebView2 host lifecycle,
`HostLink` (33 app references), the `EnvironmentDiagnostics` **report composition**,
WinForms pickers, and `UpdateCheckerLib`'s ~382 UI lines.

### Stays in KitveiHakodeshService (transport)

`Http/*` (Lib has no HTTP host at all), `Ipc/*`, `LocalFileGrants`, `FtsIndexingStarter`
(a `BackgroundService` wrapper), `Program.cs`, env-var reading, the 50 wire envelopes and
27 annotated wire mirrors + mapping, `RpcJsonContext`, the `comdlg32` native pickers.

### Explicitly NOT in Core (and why)

| Item | Reason |
|---|---|
| `comdlg32` / WinForms pickers | Show a modal window this process owns — that is UI |
| `EnvironmentDiagnostics.Collect()` | The *probe selection* encodes this app's troubleshooting history; the individual probes are reusable and do move |
| `HostLink` | 33 app-specific references |
| `*Handler` classes | Orchestration by definition |
| Wire DTOs / envelopes | MessagePack attributes = transport knowledge |

---

## 5. The MessagePack constraint (decides Core's model layer)

Every type crossing the wire carries `[MessagePack.MessagePackObject(keyAsPropertyName: true)]`:
`SeforimModels.cs` (79), `Ipc/Rpc.cs` (45), `Catalog/*` (4). MessagePack requires the
attribute **on the type** — there is no external registration, and unannotated types fall
back to reflection, which breaks under `PublishAot=true`.

(Contrast: `RpcJsonContext` registers 107 types externally via `[JsonSerializable(typeof(X))]`
with no attribute on the POCO. MessagePack cannot do this.)

**Resolution — REVISED by rule 0e (MessagePack-only).** My earlier plan kept Core's models
attribute-free and had the Service maintain 27 annotated mirrors plus ~270 lines of mapping.
That existed **only because Lib's bridge spoke JSON**. Once both transports speak MessagePack,
the mirrors and the whole mapping layer are pointless:

- **27 domain types** (`BookRow`, `CategoryRow`, `BookInfo`, `LineRow`, `TocEntryRow`,
  `FtsHit`, `CommentaryLinkRow`, `WordLinkAnchorRow`, …) → **Core, MessagePack-annotated**,
  serialized **directly** by both transports
- **50 transport envelopes** (`*Args` / `*Result`) → **stay in the Service** — they shape RPC
  calls, not data
- **27 mirror types + ~270 lines of mapping: never written**

Cost: Core references `MessagePack` 3.1.8 on both legs, so the net48 consumers (VSTO,
DemoApp) take that dependency too. That is the price of one shared model set, and it is
cheaper than a mapping layer that must be kept in sync by hand.

A MessagePack attribute is **not** orchestrator knowledge (rule 1): it names no transport,
no Lib type, no Service type. It is a serialization contract on a data shape.

---

## 6. Rename map

The `*Service` suffix is correct in `KitveiHakodeshService` and wrong in Core. Applied when
each file moves, in its slice — **not** as a separate pass.

| New (Core) | Was | Where it lives now |
|---|---|---|
| `SeforimDbPathResolver` | `SeforimDbLocator` | Service `SeforimDb/` |
| `SeforimDbSqlStrings` | `Sqlite.Strings.cs` (class `SeforimSql`) | Service `SeforimDb/` |
| `SeforimDbQueries` | `SeforimDbService` (`Sqlite.Queries.cs`) + Lib `DbAccess` | both |
| *(no new SQL files)* | Only **seforim.db** keeps a separate SQL file — it already has one (`Sqlite.Strings.cs` -> `SeforimDbSqlStrings`), and the catalog's 10 statements merge into it since they read the same DB. Everywhere else the SQL goes to `const`s at the top of its queries file: Dictionary (8), user_settings (1 DDL block), expansion (2), HebrewBooks (1) | rule 9 |
| `SearchExpansion` | `SearchExpansionService` | Service (Lib's name already correct) |
| `SeforimDbCatalog/` + `SeforimDbCatalog*` | `Catalog/` + `CatalogToc*` (incl. `CatalogTocSearchService`) — **"Toc" is too narrow**: it indexes book titles + authors, categories and alt-TOC structures as well as TOC paths | Service `Catalog/` |
| `DictionaryDb` | `DictionaryService` | Service `Dictionary/` |
| `HebrewBooksDb` | `HebrewBooksService` merged into Lib `HebrewBooksDb` | both |
| `UserAnnotations/` + `UserAnnotationStore` | `UserSettings/` + `UserSettingsService` + Lib `UserSettingsDbAccess` — it holds user CONTENT, not preferences. **The `user_settings.db` file name is NOT renamed** (real user data on existing installs) | both |
| `WordToPdf` | `WordConversionService` + Lib `WordToPdfConverter` | both |
| `HebrewFonts` | `HebrewFontsProvider` + Lib `FontsProvider` | both |
| `WordThesaurus` | `WordThesaurusProvider` | Lib `Dictionary/` |
| `AppSettingsRegistry` | Lib `AppSettings` merged in | both (Service name wins) |
| `FtsIndexState` / `FtsIndexBuilder` / `FtsSearchExecutor` | `FullTextSearchService` decomposes into Lib's three | both |
| `SqlPlaceholderRewriter` | new file (extracted from both `?`→`@p0` rewriters) | — |
| `TextEncodingDetector` | new file (extracted from duplicated `IsValidUtf8`) | — |
| `RunningWordFinder` | new file (ROT detection, extracted from `WordExporter`) | — |
| `EnvironmentProbes` | probe half of Lib `EnvironmentDiagnostics` | Lib |
| `DocumentLocatorAdapter` | `DocumentLocatorService` | **slice 9 — parked** |

Additional rule-10 renames — only where the old name hid or under-specified the job:

| New (Core) | Was | Why |
|---|---|---|
| `DbFileFingerprint` | `DbChangeStamp` | its own doc calls it a fingerprint |
| `UpdateDownloader` | `DownloadManager` | says what it downloads |
| `WordToPdfConverter` | `WordConversionService` | converts what, to what |
| `SeforimDbSchemaProbe` | `SchemaProbe` | whose schema |
| `FileLogger` | `AppLogger` | `App*` banned in `Common/` |
| `DictionaryDbQueries` | `DictionaryService` | bare `Db`/`Service` say nothing |
| `HebrewBooksCatalogDbQueries` | Lib `HebrewBooksDb` + Service `HebrewBooksService` | it is a catalog of upstream books; also settles `HebrewBooks.db` vs `HebrewBooksCatalog.db` in favour of the latter |

Everything else keeps its conventional name: `SqliteConnectionFactory`,
`DbChangeWatcher`, `ShellRegistration`, `GithubRelease`, `WordExporter`, `WordThesaurus`,
`HebrewFontsProvider`, `SearchExpansion`, `UpdateChecker`, `AppSettingsRegistry`,
`UserAnnotationStore`, the `Fts*` trio,
`SeforimDbPathResolver`, and the folders `Common/`, `Resources/`, `Diagnostics/`.
(The planned `Models/` folder is dropped — its 27 types live in one `SeforimDbModels.cs`;
see rule 12.)

`*Handler` stays correct in Lib — those genuinely are message handlers.

---

## 6a. Splits forced by rule 11

Each of these failed the "name it in plain words" test, which means it is doing more than
one job. Verified against the actual public surface, not guessed.

| File | Why the name will not resolve | Split into |
|---|---|---|
| `CatalogTocIndex.cs` (1559) | public surface spans index building, reader lifecycle, searching, version/staleness hashing, a Lucene `Tokenizer`, custom `Collector`s, and a static Tanach title set — **6+ jobs** | `SeforimDbCatalogIndexer` (absorbs the version/staleness hashing **and** `TanachBookTitles`, which gates its verse-extraction pass — both are the indexer's own bookkeeping), `SeforimDbCatalogSearcher` (absorbs the reader lifecycle **and** both `ICollector`s — a reader is how a searcher holds its resource, a collector is how it gathers hits), `SeforimDbCatalogAnalyzer` (analyzer + tokenizer — the one type genuinely shared by indexing and querying) |
| `HebrewBooksService.cs` (374) | `Search` is a catalog query; `AcquireAsync` / `Cancel` / `CheckLocalFiles` are download + local-file management. No single name covers both | `HebrewBooksCatalogDbQueries` + `HebrewBooksDownloader` |
| `EnvironmentDiagnostics.cs` probe half | "EnvironmentProbes" is a bag — Word/Office, .NET, OS and process are unrelated questions | `WordInstallProbe`, `DotNetRuntimeProbe`, `OperatingSystemProbe`, `ProcessBitnessProbe` |
| `FtsSearchExecutor.cs` (302) | takes `WebBridge` **in its constructor** (line 27) and calls `_bridge.Reply` at 50/57/64/86 — it mixes transport with the one real algorithm it owns the short-snippet pass folds into `SeforimDbFtsSnippetRenderer`, the thresholds into `SeforimDbFtsBatchingPolicy`; envelopes + `PushEvent` stay in Lib. Search execution was never its own — it already delegates to `SeforimIndex` |


**Rejected: splitting out `TanachBookTitles` to isolate Hebrew.** It gates the indexer's
verse-extraction pass and has exactly one caller, so it is a private field of the indexer.
"It would isolate the corpus text into one file" is a reason about the assistant's masking
constraint, **not** about the code being easier to understand and manage — rule 0 wins.
Working constraints never reshape the codebase.

### Still to test against rule 11 (not yet inspected)

- `CatalogTocTextRules.cs` (405) — "TextRules" is vague; needs its public surface read
- `FtsIndexState.cs` (532) — "State" is a bag word, but its doc claims a single cohesive
  job (the only locking authority). Verify before moving
- `SeforimDbQueries` (~900 after merge) — the name resolves cleanly, so rule 11 does not
  force a split; consider `SeforimBookQueries` / `TocQueries` / `CommentaryQueries` /
  `LineQueries` on size grounds alone

---

## 7. Where "Service takes precedence" does NOT apply

| Area | Decision |
|---|---|
| **FTS internals** | Service's `FullTextSearchService` is 720 lines with 9 `lock` sites. Lib decomposes into `FtsIndexState` (single locking authority) + `FtsIndexBuilder` ("contains no locks"). **Take the Service's streaming API surface over Lib's decomposition.** |
| **User settings WAL** | Lib sets `PRAGMA journal_mode=WAL` for cross-process safety with Zayit; the Service does not. **Keep WAL.** |
| **Pooling** | Lib sets `Pooling=False` explicitly. **Keep**, and make pooling per-purpose (gotcha 8). |
| **Placeholder rewrite** | Service's is quote-aware; Lib's naive `Regex.Replace` corrupts `?` inside string literals. **Service wins.** |
| **Registry key coverage** | Service's raw-`Registry` mechanism wins (VB `Interaction` is net48-only), but Lib has ~10 more key pairs. **Port them all.** |
| **Fonts** | ~~Cannot share one implementation~~ — **superseded 2026-08-20**: the Lib's WPF enumeration was replaced with a net48 port of the Service's DirectWrite implementation (WPF's `SystemFontFamilies` is a process-lifetime snapshot that never sees fonts installed while the app runs — verified live; DirectWrite's `checkForUpdates` re-scan refreshes in-process). Both providers are deliberately stateless — every call enumerates fresh, the picker shows a loading row for the ~1s it takes. The two files are marked TWIN and differ only in the factory import (`LibraryImport` net10 / `DllImport` net48), so `HebrewFonts` becomes ONE shared implementation with a single `#if` around that import — no per-TFM legs. |
| **Word COM** | Cannot share one implementation. Per-TFM legs behind one API. |

**Preserve verbatim:** the `TurnOffUpdates` setting deliberately uses app name **`KleiKodesh`**
(not `KitveiHakodesh`) with `"True"`/`"False"` casing, so the Word VSTO and the app share one
key. Changing app name / section / key / value format forks the setting.

---

## 8. Frontend consequences

- **2 of 4 generic SQL channels die**: `__webviewQuery`, `__webviewDictQuery`.
  `__webviewUserSettingsQuery` / `__webviewUserSettingsExecute` **survive** — raw SQL is the
  deliberate paradigm for user-owned mutable data (the Service does the same via `RawSqlArgs`).
- **~704 lines of shipped SQL removed**: `queries.sql.ts` (608) + `dictionaryDb.sql.ts` (96).
  `userSettingsDb.sql.ts` (68) stays.
- **40 named SQL queries -> typed methods.** 27 already have `seforimApi` equivalents (repoint);
  **13 need new typed methods**, including the `HAS_LINK_ANCHOR_TABLE` / `HAS_LINK_TARGET_LINE_INDEX`
  schema probes.
- **~692 lines of catalog heuristics deleted** (`useBookCatalogSearch.ts` 310 +
  `bookCatalogSearchTocHeuristics.ts` 382) plus the shared IDB LRU cache. **Two consumers**:
  the catalog page and `useHomeSearch.ts:298`.
- **No-DB behaviour: throw.** Hosted currently returns `[]` silently (`seforimDb.ts:120`);
  `SetupWizard` / `SettingsPageAdvancedSection` must handle the throw.
- **Messages move to the frontend.** `useToast` / `ToastBanner` already exist; the `dbOpenError`
  event channel already exists but only `console.error`s. Wire them together.
- 12 naming-drift pairs to reconcile on Service's names (see section 10, slice 8).

---

## 9. Correctness gotcha ledger

Line numbers and filenames below refer to the code **as it stands today**.

| # | Gotcha | Severity |
|---|---|---|
| 1 | `AppSettings.cs:59-62` uses `int.Parse` (not `TryParse`) on registry popout bounds — throws on a corrupt value. Distinct defaults: X/Y=-1, W=900, H=750 | med |
| 2 | `SaveHbCsvLastUpdated` writes `ToString("o")` but `LoadHbCsvLastUpdated` uses bare `DateTime.TryParse` — drops `Kind`, returns Local for a UTC value. Needs `InvariantCulture` + `DateTimeStyles.RoundtripKind` | med |
| 3 | `InvariantGlobalization` on the Service leg only — culture-sensitive defaults diverge across legs | standing rule |
| 4 | No busy timeout anywhere (only `HebrewBooksDb` `DefaultTimeout=5`). WAL allows 1 writer; a second writer (Zayit) gets `SQLITE_BUSY` immediately | med |
| 5 | **`ReadOnly=true` is silently ignored by `Microsoft.Data.Sqlite`** — must become `Mode = SqliteOpenMode.ReadOnly`. Dropping it opens the user's corpus read-write, creating `-wal`/`-shm` sidecars and failing on read-only media | **high** |
| 6 | Sync-over-async on the WinForms UI thread: `DocumentLocatorAdapter.cs:30`, `FileSystemSearchHandler.cs:62`, `FtsIndexState.cs:301` (also swallows) | med |
| 7 | **`DbAccess._EnsureIndexes` writes `idx_link_type_target_line` into the user's corpus DB**; the Service never writes to it (all 5 readers are `Mode=ReadOnly`). Hosted and dev therefore run different schemas / query plans. Failure is swallowed into `Debug.WriteLine` | **high** |
| 8 | **Connection architecture divergence**: Lib holds 8 persistent round-robin connections with `PRAGMA cache_size=-8192` (64 MB total) + `mmap_size=256 MB`; the Service opens fresh per call. Naive unification risks a hosted perf regression | **high** |
| 9 | `Encoding.GetEncoding(1255)` throws on net10 without a provider. Lib **decodes** cp1255; the Service only emits a charset **label**. Precedent: `PathIndex.cs:721` uses `CodePagesEncodingProvider.Instance.GetEncoding(1255)`. Prefer: Core returns bytes + label, never decodes | med |
| 10 | ~~`Dictionary.db` copies have DRIFTED~~ — **RETRACTED, verified false.** The two files are row-for-row identical (`word` 55,859 / `sense` 39,499 / `link` 54,606 / `link_kind` 5 / `source_kind` 9; per-table data hashes match). Same page_count 2312, page_size 4096, freelist 0. The differing md5 is **physical only** (page ordering, `sqlite_stat1`, WAL state). **Lesson: md5 is the wrong equality test for a SQLite file — compare through the tables.** The real finding is gotcha 14 | — |
| 14 | **`vue-frontend/public/dictionary/KitveiHakodesh_dictionary.db` (9.5 MB) has NO reader.** `dictionaryDb.ts` states it plainly: hosted runs SQL via the C# dict-sql bridge, dev routes through the service. No `fetch`, no sql.js, no wasm anywhere in `src/` touches it — yet vite copies it into `dist/` on every build and it ships. Delete it | **high** |
| 11 | Resource path resolution: Lib uses `AppDomain.CurrentDomain.BaseDirectory`, Service uses `AppContext.BaseDirectory`. Under VSTO, `Assembly.GetExecutingAssembly().Location` points at the shadow-copy dir where no `.db` was copied. **Inject the resource directory** | **high** |
| 12 | Lucene version split: Service **beta00018**, DocumentLocator **beta00016**, `packages/` has only **beta00016**. Two betas over one index directory risks format incompatibility. Pin one | **high** |
| 13 | **TWO cross-process build locks guard the same index directory, using different primitives**: FtsLib `IndexWriteLock` takes an **OS file lock** on `write.lock`; Lib `FtsIndexState` takes a **named Mutex** (`FtsIndexBuildLock`). A process holding one cannot see a process holding the other, so neither is authoritative. Consolidate on `IndexWriteLock` in slice 4b | **high** |

### Duplicate pairs confirmed

`ResolveDefaultDbPath`, `SearchExpansion` (incl. the same `SEARCH_EXPANSION_DB` env var read
independently), `IsValidUtf8`, the `?` placeholder rewriter, the niqqud normalizer,
`ZayitDb` vs `DbAccess` vs `SeforimDbService` (three corpus readers), the cross-process build
lock (two primitives), `HebrewBooks.db` (**3** byte-identical copies, one an orphan in no
csproj), `Dictionary.db` (**2** copies, verified identical, one with no reader).
All resolved by rule 0b: one copy, in `Core/Resources/`.

### Do NOT unify

The two niqqud normalizers differ **by design**: HebrewBooks strips U+05B0–U+05C2;
`AppViewer.cs:189` documents a wider U+0591–U+05C7 (includes cantillation) for a different
purpose. Merging them changes search behaviour.

---

## 10. Step-by-step plan

**Every slice ends with the same four checks — no slice is done until all four pass:**
1. **AOT** — Core's net10 leg builds with `IsAotCompatible` and no new ILC warnings (rule 0d).
   No Dapper, no reflection serialization, no runtime codegen crossed the line.
2. **Both legs** — net48 *and* net10.0-windows compile; the net48 consumers (VSTO, DemoApp)
   still resolve.
3. **No duplicate left behind** — the old copy is deleted, not orphaned (rule 0b).
4. **Tests moved with the code they cover** (rule 0c).

Each slice is buildable. Renames are applied when the file moves, in its slice.
Resources move with their slice so path constants and readers change in lockstep.
**Every slice extracts its SQL into an `<Subject>DbSqlStrings.cs` as part of the move (rule 9)** —
never carry inline SQL across into Core.

### Slice 0 — Foundation
- `KitveiHakodesh.Core.csproj`: `net48;net10.0-windows`, `Microsoft.Data.Sqlite` **10.0.9**
  (the FtsLib pin — 11.x breaks the segment-writer `File.Move`), conditional `FtsLib` legs,
  `PlatformTarget=AnyCPU`, explicit `OutputPath`, `IsAotCompatible` for net10
- `Common/AppFileLocator` — **no `CoreOptions` at all.** Core finds its own files by
  probing candidate roots in order and taking the first that exists; writes go to the first
  root that is genuinely writable, with `%LocalAppData%\KleiKodesh` last. See rule 2a
- `Exceptions/` — SPECIFIC types (`SeforimDbUnavailableException`, `SqliteOpenFailedException`,
  `IndexBuildFailedException`, `UpdateDownloadFailedException`), never one catch-all
- `Common/SqliteConnectionFactory` with **per-purpose** policy:
  corpus reads = pooled + pragmas + `Mode=ReadOnly`; FTS segment writes = `Pooling=false`;
  `user_settings.db` = fresh connections + `Pooling=False` + WAL + busy timeout
- the quote-aware `?` → `@p0` rewrite lives in `UserAnnotationStore`, its only caller
- `Settings/AppSettingsRegistry` (Service name; Lib `AppSettings` merged in)
- `Settings/SeforimDbPathResolver` (was `SeforimDbLocator`). **Three corrections on move:**
  1. it must call `AppSettingsRegistry.Get/Set` instead of carrying its own
     `Registry.CurrentUser.OpenSubKey` plumbing (a third copy of registry access today)
  2. **DELETE the `DB_PATH` env read — do not migrate it.** It is already unreachable in
     practice: `Resolve()` checks the registry **first** and returns on a hit, so `DB_PATH`
     only fires on a machine where the DB was never configured — and vite's fallback chain
     ends at `./data.db`, which is **0 bytes** in this repo. A developer changes the setting
     in the app exactly like a user. So: no override field anywhere, vite stops
     forwarding it, and `KitveiHakodeshService.Tests` sets the registry (or takes a path
     argument) like everything else
  3. `RegistryKeyPath` stays public — `DbChangeWatcher` subscribes to it because the hosted
     app writes that value directly, not via service RPC

  **New resolution order — verify existence at each step, never return a path that is not there:**
  ```
  1. registry value, AND File.Exists  -> use it
  2. else probe Zayit, then Otzaria; first that EXISTS -> use it
  3. else -> report NOT FOUND (do not invent a path)
  ```
  Both of the first two steps are stricter than today. Currently `Resolve()` returns the
  registry value **without checking it exists**, so a moved or deleted DB yields a stale path
  that fails later, somewhere less obvious. And `ResolveDefaultDbPath()` returns the Zayit
  path as a fallback *even when neither install is present* — handing back a path that is
  known not to exist.
  Step 3 returns a not-found result; it does **not** throw inside the resolver and does **not**
  fabricate a path. The orchestrator decides what that means: hosted shows the setup wizard,
  dev reports the error (rules 3 and 4 — Core returns data, the host does the telling).
  This is the backend half of the frontend "no DB -> throw" decision in section 8.

  *This is in slice 0, not slice 4:* `UserSettingsService.cs:18` derives `user_settings.db`
  from `SeforimDbLocator.Resolve()`, so slice 1 depends on it.

### Slice 1 — UserSettings
Pure backend merge, **zero frontend change** (raw SQL stays by design).
- Merge Lib `UserSettingsDbAccess` + Service `UserSettingsService`
  → `Core/UserAnnotations/UserAnnotationStore`

- **Rename the folder and class; keep the DB FILE name.** `UserSettings` reads as user
  *preferences* — but preferences live in the registry (`Settings/AppSettingsRegistry`), and
  this holds highlights and notes, i.e. user *content*. Two adjacent folders (`Settings/`,
  `UserSettings/`) meaning unrelated things.
  **`user_settings.db` itself does NOT get renamed** — it is real user data on existing
  installs, and renaming it would orphan people's highlights. (Unlike
  `HebrewBooks.db` -> `HebrewBooksCatalog.db`, which was a shipped read-only asset.)
  This is the one place where a name we know to be misleading stays, so the mismatch must be
  **explained in code, not left to be rediscovered**:

  ```csharp
  /// <summary>
  /// The user's own annotations — highlights and notes anchored to (bookId, lineId, offsets).
  /// The ONLY write path in Core; every other database here is opened read-only.
  ///
  /// NAME MISMATCH, deliberate: the file on disk is "user_settings.db" but this holds
  /// user CONTENT, not preferences — those are in the registry, see AppSettingsRegistry.
  /// The filename is frozen because existing installs hold real user highlights under it;
  /// renaming it would orphan them. Do not "fix" the file name to match the class.
  ///
  /// It also runs FRONTEND-SUPPLIED SQL rather than owning queries — deliberate, and why
  /// this is a *Store, not a *Queries. See rule: typed methods for the read-only shipped
  /// corpus, raw SQL for user-owned mutable data.
  /// </summary>
  ```
  Put the same two-line note on the `user_settings.db` path constant, where a reader meets
  the filename first.
- The ~15-line `CREATE TABLE` block becomes a `const` at the top of the store, not its own
  file (rule 9 — it buries nothing). Frontend-supplied
  SQL remains a passthrough **parameter**, not a stored constant
- Keep WAL + `Pooling=False`; adopt the quote-aware rewriter; add a busy timeout
- **Drop Dapper** — `UserSettingsDbAccess` uses `conn.Execute` / `conn.Query` +
  `DynamicParameters`; the Service's `UserSettingsService` is already raw ADO. The merged
  result is raw ADO (rule 0d). Same in slices 3 and 4 for `HebrewBooksDb` and `DbAccess`
- Port all ~10 extra Lib registry keys into `AppSettingsRegistry`;
  `KleiKodesh`/`TurnOffUpdates` verbatim
- Fix gotchas 1 and 2. `Rectangle` marshalling stays in Lib (Core exposes 4 ints)

### Slice 2 — Dictionary
- Service `DictionaryService` → `Core/Dictionary/DictionaryDbQueries` (9 typed methods);
  `Dictionary.db` → `Core/Resources/`
- Gather its 8 SQL statements as `const`s at the top of the queries file — 3 already are;
  no separate SQL file (rule 9). Fold in
  `dictionaryDb.sql.ts`
- **Delete `vue-frontend/public/dictionary/KitveiHakodesh_dictionary.db`** (9.5 MB, no
  reader — gotcha 14). Both transports already go through C#: hosted via the dict-sql
  bridge, dev via `serviceCall`. Removes it from `dist/` and from every build
- **Update `ADDING_SENSES.md`**: delete the *"TWO copies — keep them identical"* rule and
  the *"applied to both"* checklist item; make `scripts/hebrew_lexicon/import_hebrew_lexicon.py`
  write **one** DB (rule 0b)
- There is **no drift to resolve** — the two files were verified row-for-row identical
- Retire `__webviewDictQuery`; delete `dictionaryDb.sql.ts` (96)
- `WordThesaurusProvider` is a *separate feature* (Word COM) — slice 7, not this slice

### Slice 3 — HebrewBooks
- Lib `HebrewBooksDb` + Service `HebrewBooksService` → `Core/HebrewBooks/HebrewBooksCatalogDbQueries`;
  the DB → `Core/Resources/HebrewBooksCatalog.db`
- SQL stays inline as a `const` — 1 statement, built dynamically (rule 9 threshold)

- **RESTORE the catalog updater** as `HebrewBooksCatalogUpdater`. Recovered from
  `79a4c159^:.../HebrewBooksCsvUpdater.cs` — 205 lines, deleted 2026-06-02 when the catalog
  moved CSV → SQLite. Its behaviour, worth keeping:
  - `RunIfDue()` at startup, but on a **~90-day (few-month) interval, not the original 30**.
    The upstream catalog grows slowly and the scrape is a courtesy request against someone
    else's site; monthly is more traffic than the data justifies. A named `const` here —
    **not** a config field: nobody has asked to tune it, and speculative configuration
    is how an options object turns into a bag
  - resumes from `maxId + 1`, walking IDs upward
  - **1000 ms** between requests — it is scraping someone else's site; keep the courtesy delay
  - row = `Id, Title, Author, Place, Year, Pages, Tags`

  **⚠ The "stop after 10 consecutive empty IDs" rule is BROKEN — do not restore it.**
  Measured against the shipped catalog: 59,583 books over an ID range ending at 69,871, and
  the gaps between consecutive IDs reach **2,766** overall and **1,447 within the last ten
  thousand IDs** (454 within the last five thousand). A 10-miss stop halts at the first
  ordinary gap — and because the walk resumes from the highest ID it *holds*, every later run
  halts in the same place. The catalog stops growing permanently and silently.
  Restored as **`MaxConsecutiveMissingIds = 1500`**, clearing every gap observed in this
  catalog. Cost: a tail of at most 1,500 requests (~25 min at the courtesy delay), once a
  quarter.

  **Last-run stamp: `_metadata.last_scrape_date` in the catalog, NOT the registry.**
  The catalog already carries this key (written by the 2026-06-02 CSV→SQLite conversion).
  It is the better home because the stamp belongs to the *file*: replacing the catalog
  replaces its history with it, and a second machine reading the same file does not re-walk
  ground the first already covered. So `AppSettings.HbCsvLastUpdated` is **deleted, not
  reconnected** — which removes the orphan key the map complains about, by retiring it rather
  than reviving it.

  **No pre-write backup.** The CSV version copied the file first because `AppendRow` wrote
  raw text with no transaction and a mangled line was unrecoverable. Appending parameterised
  rows to SQLite has neither problem, and a backup of a 7 MB catalog per run is a copy of a
  shipped database (rule 0b).

  **Writes IN PLACE, one catalog.** On a read-only install the catalog cannot grow; that is
  reported as `CatalogNotWritable` and search keeps working on what shipped. No writable
  overlay copy — that would be a second catalog, and the reader would then need to know which
  one wins.

  **`HtmlAgilityPack` is AOT-clean — CONFIRMED, not assumed.** A native-AOT console app
  calling `LoadHtml` + `GetElementbyId` + XPath `SelectNodes` (the whole surface this uses)
  published with no ILC warnings and ran correctly. The reflection in that package is in
  `HtmlWeb` and the object-encapsulator APIs, which nothing here touches and ILC trims.

  Changes needed for Core:
  - **CSV → SQLite**: append into `HebrewBooksCatalog.db` via `HebrewBooksCatalogDbQueries`,
    not `AppendRow`/`CsvEscape` (whose `Replace(",", " -")` mangles any title containing a comma)
  - **No `Debug.WriteLine` swallowing** (rule 4) and **no fire-and-forget `Task.Run`** —
    return a result the orchestrator can surface; a silent scrape that half-fails is worse
    than one that reports
  - **Paths injected** (rule 2) — it hardcoded `AppDomain.CurrentDomain.BaseDirectory`
  - `HtmlAgilityPack` moves from a Lib `<Reference>` to a Core `PackageReference` so both
    legs get it — AOT confirmed above
- Delete the orphan `KitveiHakodeshLib/HebrewBooks.db` (declared in no csproj)
- Unify the file name on **`HebrewBooksCatalog.db`** (Service's) — `HebrewBooks.db` (Lib's) does not say it is a catalog
- WebView2 `DownloadStarting` interception **stays in Lib**

### Slice 4 — SeforimDb
- 27 domain row types → `Core/SeforimDb/SeforimDbModels.cs` (one file, rule 12);
  50 envelopes stay in Service
- Service `Sqlite.Strings.cs` → `Core/SeforimDb/SeforimDbSqlStrings.cs` (**already SQL-only** — the one
  area that already follows rule 9; the class `SeforimSql` becomes `SeforimDbSqlStrings`)
- Service `SeforimDbService` (`Sqlite.Queries.cs`) + Lib `DbAccess` → `Core/SeforimDb/SeforimDbQueries`
  — params + row reads only, no SQL literals
- 40 named queries → typed methods (27 repoint, 13 new)
- `SeforimDbSchemaProbe` (category `order_index`, `HAS_LINK_*`) → Core
- **Decide gotcha 7** (corpus index) and **gotcha 8** (pooling) here
- Retire `__webviewQuery`; delete `queries.sql.ts` (608)

### Slice 4b — FtsLib boundary: stop the library reading the corpus

**REVISED 2026-08-25 — GRADUAL SEAM, NOT A REWRITE (user decision).** Do not refactor FtsLib
totally. Create the separation cleanly INSIDE it, so the engine works with its internal
reader OR through Core — both routes live at once. The apps that use FtsLib as-is keep
working unchanged while Core is built; callers move over one at a time.

**Step 1 — the seam. DONE (both legs build; Service, FtsLibTest, FtsLibTest.Net10 and
KitveiHakodeshLib rebuild with ZERO call-site changes):**
- `IFtsCorpus` (public, `FtsLib/IFtsCorpus.cs` + net48 twin): CountDocuments / CountDocumentsUpTo,
  GetDocumentText, ReadDocuments / ReadDocumentsAfter, FetchDocuments, FetchNeighbourText.
  Every engine entry point takes a **`Func<IFtsCorpus>` FACTORY, never an instance** — a search
  returns a LAZY sequence, so the corpus must outlive the call and the engine's `using` sits
  inside the iterator; and searches run on arbitrary threads while a build runs on another, so
  one connection per operation is correctness, not style
- `ZayitDb : IFtsCorpus`, implemented EXPLICITLY so the generic names stay off its own surface
- The three pipelines take the factory instead of `dbPath`; `SeforimIndex` gains
  `(indexPath, Func<IFtsCorpus>)`, and the old `(indexPath, dbPath)` ctor forwards through the
  built-in reader — behaviour identical

**Step 2 — per caller, in its own slice:** Lib and the Service construct `SeforimIndex` with a
Core-backed corpus (Core implements `IFtsCorpus` over `SeforimDbQueries`). Slice 5 does this.

**Step 3 — cleanup, ONLY when no caller uses the legacy ctor:** everything below this note —
deleting the 7 `SeforimDb/` files, folding `ZayitDb` into `SeforimDbQueries`, the 27-file test
split — is step-3 work and moves to slice 11 territory. Measured while cutting the seam:
`FindByPhrase`, `FindByBookAndPhrase`, `FindBooks`, `GetLineInfo` already have **zero callers**
anywhere (production or test) and can go first.

--- original full-inversion plan below, kept as the step-3 spec ---

**The violation.** `FtsLib/SeforimDb/` holds 7 corpus-specific files inside what is supposed
to be a generic engine — and every one of them is **duplicated** in `FtsLib.Net48/SeforimDb/`:

| File | What it actually is |
|---|---|
| `ZayitDb.cs` | a full seforim DB access layer — `CountLines`, `GetLineContent`, `ReadLines`, `ReadLinesFrom`, `FetchNeighborContext`, `FindByPhrase`, `FindByBookAndPhrase`, `FindBooks`, `GetLineInfo`, returning `BookTitle` / `HeRef` / `Content` |
| `SeforimIndex.cs` | *"Public API for full-text search over the seforim database"* — ctor takes `dbPath`; `BuildIndex` reads lines itself. **A facade over 5 collaborators** (`IndexingPipeline`, `SearchPipeline`, `SnippetPipeline`, `SegmentStore`, `ZayitDb`) mixing indexing, searching, snippets, segment lifecycle and corpus counts — rule 11 splits it by job; no `*FtsIndex` facade survives. `CountLines`/`CountLinesUpTo` (270, 276) are plain corpus queries and fold into `SeforimDbQueries` |
| `IndexingPipeline.cs` | *"Builds the full-text index from the seforim SQLite database"* |
| `SearchPipeline.cs`, `SnippetPipeline.cs` | seforim search/snippet flow — **inspect: how much is generic?** |
| `SearchResult.cs`, `SnippetResult.cs` | carry `LineId` / `BookTitle` — corpus shapes |

So the seforim DB has **three** independent access layers today: `ZayitDb` (FtsLib),
`DbAccess` (Lib), `SeforimDbService` (Service). Slice 4 merges the latter two; this slice
removes the third.

**The inversion.** Core reads the lines and feeds them in; FtsLib never opens `seforim.db`:

- FtsLib exposes generic ingestion — documents as `(docId, text)` — and generic search over them
- Core owns the seforim facade (was `SeforimIndex`), the read loop (was `IndexingPipeline`'s
  DB half), and the result shapes carrying `BookTitle`/`HeRef`
- `ZayitDb`'s query methods **fold into `SeforimDbQueries`** (slice 4) — it is a
  duplicate corpus reader, not a new capability
- FtsLib keeps SQLite for its **own segment storage**; it loses only the corpus dependency

**Payoff:** 7 hand-maintained duplicate files disappear, because Core multi-targets
`net48;net10.0-windows` while FtsLib/FtsLib.Net48 are two copies. FtsLib becomes what its
name claims — a reusable engine with no knowledge of this corpus.

**Also in scope — `FtsIndexState` (532) splits by the same rule.** Most of it is generic
engine state that belongs in **FtsLib**, not Core:

| Goes to FtsLib (generic) | Stays Core (seforim/app) |
|---|---|
| `IsReady`, `IsIndexing` | `GetDbPath` / `SetDatabase` |
| `TryStartBuilding`, `SetIndexingTask`, `TryMarkReady`, `TryMarkIdle`, `MarkReadyDirect`, `StopAll` | `ComputeDbStamp(dbPath)` |
| `TryReadProgressFile` — reads FtsLib's own `build.progress` | `ReadSourceStamp` / `WriteSourceStamp` (`fts.src`) |
| `ValidateFtsIndex`, `DeleteFtsIndex` | `GetInstalledAppVersion`, `Read/WriteVersionStamp` |
| the cross-process build lock — see gotcha 13 | `DeleteAllCaches` (not FTS at all — app reset) |

`DeleteAllCaches` is the clearest misfit: it wipes the Word→PDF cache, HebrewBooks cache and
the **WebView2 webcache**. It is the app-reset feature sitting next to index deletion for
convenience. Split it: corpus/app caches to Core, WebView2 webcache stays in Lib.

**Resume state: delete `build.progress`, don't move it.** The index already holds the answer
— `DocSourceMap` is persisted per segment in that segment's `doc_source` table, mapping
docId ranges to `(corpus, sourceLocalId)` with corpus 0 = `seforim.db`. So the resume point
is *"highest committed docId, resolved through the map"*, exposed as a query on the store.
This beats any checkpoint file: a file is second-copy state that can disagree with the index
(rule 0b), whereas the mapping ships **inside** the segment, so an unflushed segment takes
its `doc_source` rows down with it — there is no window where the checkpoint claims lines the
index does not have.

*Two prerequisites to confirm before deleting the file:*
1. **Cost** — a file read is O(1); "max docId across live segments, resolved through the map"
   is a query over per-segment metadata. Expected cheap (`doc_source` rows, not postings),
   but it runs on every resume — measure it
2. **`totalLines`** — line 3 of `build.progress` caches the corpus count for the progress
   percentage. It is not in the index and is Core's number; confirm a `COUNT(*)` is cheap
   enough to recompute, otherwise it is the only thing keeping a file alive

**The tests split the same way the code does.** 27 files in `FtsLibTest` reach the engine
through `SeforimIndex` — the type this slice moves to Core. Left alone, FtsLib's own test
project would end up depending on Core, which is the inversion rule 0a forbids. So:
- **generic engine tests** (merge, crash/interrupt, write lock, posting iterators, wildcards,
  tokenizer, ids-only search) stay in `FtsLibTest`, rewritten against FtsLib's own API
- **corpus-dependent tests** (`DocSourceTest`, `KetivQueryTest`, snippet/embellish, anything
  asserting on `BookTitle` or Hebrew queries) move to a Core test project

That is 27 test files on top of the 7 source files — it does not change whether 4b is right,
it changes how long it takes.

**Call-site churn is accepted.** `SeforimIndex` is the API surface both Lib and Service call,
so this changes them — that is the maintenance work this migration exists to do. Ordering is
forced: **after slice 4** (so `SeforimDbQueries` exists to absorb `ZayitDb`), **before
slice 5** (whose files orchestrate `SeforimIndex`).
Also resolve `SnippetResult.cs` existing in **both** `Snippets/` and `SeforimDb/`.

### Slice 4c — Merge FtsLib + FtsLib.Net48 into ONE multi-targeted project

**Why the split stops being justified.** `FtsLib` (net10) and `FtsLib.Net48` (v4.8) are two
hand-maintained copies of the same engine. After 4b removes `SeforimDb/`, each holds **48
files** — and the two reasons they were ever separate are both gone or cheap by then:

| Reason for the split | Files | Status |
|---|---|---|
| SQLite provider (`System.Data.SQLite` vs `Microsoft.Data.Sqlite`) | 10 | **dissolved by slice 0** — net48 moves to `Microsoft.Data.Sqlite` |
| net10-only APIs (Intrinsics, `Span`, `Parallel`, `stackalloc`) | 6 | `#if NET10_0_OR_GREATER` — the `DocConvertLib` / `DocumentLocator` pattern |
| `SeforimDb/` corpus code | 7 | **removed by slice 4b** |

Measured: of the 48, only 10 touch SQLite and only 6 use net10-only APIs. A sample of six
non-SQLite files found **five byte-identical** (`QueryParser`, `RoaringBitmap`, `Levenshtein`,
`Tokenizer`, `DeleteSet`); only `SnippetBuilder` differed. So **~38 files are pure duplicates
maintained twice.**

Target: one `<TargetFrameworks>net48;net10.0</TargetFrameworks>` project, same csproj pattern
as Core (section 1). The net10 perf work is preserved behind `#if`, not lost. Lib/VSTO
consume the net48 leg; the Service consumes net10 and keeps `IsAotCompatible`.

**The real cost is NOT the csproj — it is adjudicating divergence.** The legs have already
drifted in *behaviour*: `SegmentWriter.cs` carries different bug-fix comments on each side —
net10 *"resumed build re-emits an already-written segment id"* vs net48 *"a leftover
unregistered file occupies the target path"*. Two independent fix histories for one file.
Merging means deciding, per divergence, which behaviour is right or whether both fixes are
needed — across 48 files, in index-corruption territory. Not mechanical.

That drift is also the argument for doing it: every month the split persists the two diverge
further and this reconciliation gets harder.

**Ordering is forced:** after slice 0 (provider unified) and after 4b (`SeforimDb/` gone), so
the merge faces 48 files with no provider difference rather than 55 with one.

### Slice 5 — Full-text search
Slice 4b already moved the generic engine state into FtsLib and the seforim facade into Core.
**This slice is much smaller than first scoped.** Verified against the real call sites: these
three Lib files are thin orchestration over `SeforimIndex`, so once that is in Core, most of
what remains is transport belonging in Lib.

| File | First claimed | Verified reality |
|---|---|---|
| `FtsIndexState` (532) | app lifecycle -> Core | mostly **generic -> FtsLib** (slice 4b); only stamps + `DeleteAllCaches` are Core's |
| `FtsIndexBuilder` (244) | "runs the build + background merge" | it only **calls** `index.BuildIndex(...)` (line 111) and `index.ForceMerge()` (line 197). Nothing generic left to move — FtsLib already owns it. Trigger + resume -> Core, `PushProgress` -> Lib |
| `FtsSearchExecutor` (302) | generic execution + enrichment | execution is already FtsLib: `index.Search` (156), `GenerateSnippet` (164), `FetchNeighborContext` (287), `GenerateSnippetWithNeighbors` (293). Only `EmbellishShortSnippets` (273-298) and the batching policy are Core's; the rest is `PostSearch` -> `_bridge.PushEvent` |

- **CORRECTION to an earlier claim:** I checked only for WebView2/WinForms (comments only)
  and missed `WebBridge` — `FtsSearchExecutor` takes one in its constructor and
  `FtsIndexBuilder` has 4 references. The `searchBatch` / `searchComplete` /
  `searchCancelled` / `searchError` envelopes are transport and **stay in Lib**; the Service
  keeps its own streaming
- Core gets little from this slice: the short-snippet pass folds into
  `SeforimDbFtsSnippetRenderer` and the thresholds into `SeforimDbFtsBatchingPolicy`,
  both already created in 4b. The expander is already clean (0 bridge refs)
- Service `FullTextSearchService` decomposes into Lib's `FtsIndexState` /
  build trigger + FtsLib's `IndexBuildState`; keep the Service's **streaming** surface,
  which stays Service-side exactly as Lib's envelopes stay Lib-side
- Service `SearchExpansionService` + Lib `SearchExpansion` →
  `SeforimDbFullTextSearch/RelatedFormExpander`; dedup the pair, including the duplicated
  `SEARCH_EXPANSION_DB` read → `AppFileLocator.FindFile`
- Its 2 one-line queries stay as `const`s at the top of `SeforimDbFtsRelatedFormExpander` (rule 9
  threshold) — dedup them, but do not give them a file
- Reconcile `ResetFtsIndex` + `DeleteFtsIndex` → `ftsResetIndex` (**semantic** merge, not a rename)
- Fix the culture-sensitive `StartsWith("%")` / `EndsWith("%")` to the char overload

### Slice 6 — Catalog (largest, most corpus-dense)
- **Pin the Lucene version first** (gotcha 12)
- Service `Catalog/*` (2586 lines) → `Core/SeforimDbCatalog/`, files prefixed
  `SeforimDbCatalog*` — **not** `*Toc*`, which understates it: the index covers book titles,
  authors, categories and alt-TOC structures as well as TOC paths.
  `<TrimmerRootAssembly Include="Lucene.Net" />` **stays in the Service csproj**

- **Collapse the 4-stage abbreviation pipeline into one hand-edited C# file.**
  Today the same 275 abbreviations exist in three representations with two generators:

  | Delete | Why |
  |---|---|
  | `catalog_abbreviations.csv` (154 lines) | **already dead** — last touched 2026-07-22 while the json/`.g.cs` moved together on 2026-08-13 |
  | `catalog_abbreviations.json` (1821 lines) | no runtime reader, no human reader — and it is the *least* readable of the three |
  | `scripts/csv_to_json.py` | feeds the dead CSV |
  | `scripts/gen_catalog_abbreviations.py` | a 1:1 emitter (see below) |
  | `CheckCatalogAbbreviationsFresh` MSBuild target | exists **only** because the data lives twice — the same shape as the "keep both DBs identical" rule 0b removes |

  **Verified safe:** the generator expands nothing — `flavours(key)` returns `[key]` and its
  docstring says *"emits each key VERBATIM: no flavour expansion."* JSON has one top-level
  key (`abbreviations`, 275 entries); the `.g.cs` has 275 entries. It is a faithful whole
  representation, not a projection.

  **⚠ Preserve the two invariants the generator enforced** — they are the only part of it
  that was not formatting, and they guard hand-audited data:
  1. **No key may contain a quote glyph** (`" ' ״ ׳ “ ” ‘ ’`). Keys are pre-stripped because
     the lookup strips glyphs before probing, so a key holding one can never match. The
     generator treated this as **fatal**
  2. **No two keys may collide** with different meanings

  Both become **unit tests over the compiled map** (`SeforimDbCatalogAbbreviations.Map`),
  which is stricter than the old check: it runs on every build, not only when someone
  remembers to regenerate.

  Result: `SeforimDbCatalogAbbreviations.cs` — hand-edited, compiled, one copy, `[MapName]`
  keys sorted as today. Drop the `.g.` infix: nothing generates it any more, and generated
  files get skipped in review.
- **Move `CatalogTocIndex`'s 10 SQL statements into `SeforimDbSqlStrings` and their reads into
  `SeforimDbQueries`** (rule 9 — SQL belongs to the database). They query seforim.db:
  `line`, `tocEntry`, `tocText`, `category`, `book`, `book_author`, `author`,
  `alt_toc_structure`. The catalog builder then reads through `SeforimDbQueries`, the
  same way the FTS builder does — one reader for one DB
- **Expect duplicates, not just moves.** `CatalogTocIndex.cs:866-867` is a near-copy of
  `GetAllCategories` *and* carries a **third** copy of the optional-`orderIndex` check
  (the others: `SeforimDbQueries.ColumnExists`, the frontend's `ensureCategorySchema`).
  Reconcile rather than transplant
- This is the one extraction that requires **editing** a corpus-dense file rather than moving
  it — do it as a surgical cut of the SQL literals only
- **Plug Lucene into Lib**: build trigger (no `BackgroundService`), injected index dir
  (not `CATALOG_TOC_INDEX_PATH`, not `AppContext.BaseDirectory` — VSTO-hostile), clean
  shutdown, participation in the app-reset wipe
- Delete ~692 lines of frontend heuristics + the IDB LRU cache (**two** consumers)
- Repoint ~1489 lines of tests; keep `ManualCatalogPipeline.cs` as a regression oracle
- **Working constraint:** 1484 Hebrew occurrences across 6 files. Pure file moves plus
  surgical namespace-line edits only — never read-and-rewrite

### ✅ Slice 5 UNBLOCKED — the FtsLib corpus seam is in (2026-08-25)

The ordering constraint that stood here is resolved by slice 4b step 1 (see its revision
note): FtsLib now takes its documents through `IFtsCorpus`, and `SeforimIndex` gained a
second constructor `(indexPath, Func<IFtsCorpus>)`. Core's `SeforimDbFtsIndexer` /
`SeforimDbFtsSearcher` get written against that seam — implement `IFtsCorpus` over
`SeforimDbQueries`, hand the factory in, and the engine reads nothing. Every existing
caller still uses `(indexPath, dbPath)` unchanged.

### ⛔ Slice 6 NEEDS A DECISION BEFORE IT CAN BE WRITTEN

Rule 11 says split `CatalogTocIndex.cs` (1644 lines) into `SeforimDbCatalogIndexer` and
`SeforimDbCatalogSearcher`. Tracing the code first: **those two share more than a file.**

- ONE `_lock`, ONE `FSDirectory` handle, ONE `DirectoryReader`. `BuildInPlace` takes the lock,
  disposes the reader, publishes `_writer`, and opens a near-real-time reader off it
  (`CatalogTocIndex.cs:433-440`).
- The build then calls `RefreshNrtLocked()` on **every progress tick** (line 446), which exists
  so the catalog stays SEARCHABLE while it is being rebuilt. That is the feature, not a detail.
- `BuildInPlace` deliberately reuses the reader's directory handle (`_dir ??= FSDirectory.Open`,
  line 421) because two `FSDirectory` instances on one folder contend for Lucene's write lock.

So two classes means the indexer holds the searcher (or a third shared index-handle object) and
reaches into its lock and reader on every tick. That is a REDESIGN of the NRT-during-build path,
and lock-ordering mistakes there produce a Lucene write-lock deadlock or a disposed-reader crash
— neither of which a build failure would catch.

**Two options, pick one:**

1. **One class, files named by job** — `SeforimDbCatalogIndexer.cs` + `SeforimDbCatalogSearcher.cs`
   as `partial` halves of one `SeforimDbCatalogIndex`. The precedent is already in Core:
   `SeforimDbQueries` is split across `SeforimDbConnection.cs` (finding/opening/probing) and
   `SeforimDbQueries.cs` (the reads). Zero behaviour change; the shared lock stays private to one
   type. Rule 11 is satisfied at the FILE level, not the type level.
2. **Two classes plus a shared index handle** — a third type owning the lock, the directory and
   the reader, which both depend on. Cleaner boundary, real redesign, needs the NRT-during-build
   path re-verified by hand.

Everything else in slice 6 is unaffected and mechanical: `CatalogTocTextRules` ->
`SeforimDbCatalogTextNormalizer` (whole file), `CatalogAbbreviations.g.cs` ->
`SeforimDbCatalogAbbreviations` (whole file, csv+json+generators collapsed in),
`PipelineAnalyzer`/`PipelineTokenizer` -> `SeforimDbCatalogAnalyzer` (two nested classes lifted
out), `CatalogTocHit` -> a models file, and the SQL to `SeforimDbSqlStrings`.

### Slice 7 — Reusable toolkit (`Common/`)
- `UpdateCheckerLib` ~893 portable lines → `Core/Common/` (**remove the MessageBox at
  `DownloadManager.cs:270`** → throw). ~382 UI lines stay net48. `ServicePointManager` (2 uses)
  needs a net10 conditional. DemoApp references `UpdateCheckerLib` directly — repoint it
- `FileLogger`, `DbFileFingerprint` / `DbChangeWatcher`,
  `TextEncodingDetector`
- **Split** Lib `EnvironmentDiagnostics`: probes → `Core/Common/` (one file per question asked),
  report composition stays in Lib. The Word/Office probes benefit both orchestrators
  (dev has none today)
  - Probes return TYPED data, not `Dictionary<string,string>` entries. The dictionary is the
    REPORT's shape, and it also swallowed every failure into an `"error: …"` string, so a
    caller could not tell "not installed" from "could not read the hive"
  - **`CollectSqliteInterop` / `CollectLoadedSqliteModules` / `CollectAssemblyPaths` are NOT
    ported.** All three diagnose `System.Data.SQLite` + `SQLite.Interop.dll` — the provider
    Core drops entirely for `Microsoft.Data.Sqlite`. ~180 of the file's 403 lines exist to
    debug a dependency that will not be there. They stay in Lib until slice 11 removes them
    with the provider
- Office (net48 leg): `WordThesaurusProvider` → `WordThesaurus`, **autonomous via
  `RunningWordFinder`** — no injected instance, empty result when Word is absent
  (`Marshal.GetActiveObject` on net48; `AotWordConverter.cs:36` shows the net10 P/Invoke).
  `WordExporter` keeps its "`HostApplication` first, else `GetActiveObject`" chain
- `HebrewFontsProvider` + Lib `FontsProvider` → `HebrewFonts`, ONE shared DirectWrite
  implementation (the files are already identical TWINs since 2026-08-20 — see section 5's
  Fonts row; only the factory import needs `#if NET10_0_OR_GREATER` LibraryImport / `#else`
  DllImport)
- `ShellRegistration`: parameterize its 4 app references

### Slice 8 — Naming reconciliation (bridge action names)
Distinct from section 6 — this is the **wire/action** names, not C# type names.
One pass over the 12 drifted pairs, on Service's names:
`fileSystemSearch`→`locateDocuments`, `setDbPath`/`pickDbPath`/`clearDbPath`→`set|pick|clearSeforimDbPath`,
`FtsSearchStart`→`ftsSearch`, `GetFtsIndexingProgress`→`ftsIndexingStatus`,
`openInDefaultApp`→`openFileInDefaultApp`, `pickFile`→`pickLocalFile`,
`restoreLocalFile`→`openLocalFile`, `ResetDocumentLocatorIndex` casing.
**`getWordSynonyms` is NOT `dictSynonyms`** — Word COM thesaurus vs dictionary DB, two
different features. Nav labels key `SINGLETON_ROUTES`; renames need lockstep updates.

### Slice 9 — DocumentLocator: absorb the library, delete the pipe architecture

The "two different architectures" problem disappears rather than being reconciled — **there
stops being a pipe architecture at all.** Lib's named-pipe path goes; everything calls the
index in-process through Core.

**1. Stop being a submodule.** `CSharpBackend/DocumentLocator` is a gitlink today
(mode `160000` → `github.com/KleiKodesh/DocumentLocator.git`, branch `main`). Its source
becomes ordinary tracked files at the **same path**. It stays a separate **C# project**, just
not a separate **git repo**. *History is deliberately not imported* — decided, not overlooked.

**2. Keep the library, drop everything else.**

| Project | Files | Fate |
|---|---|---|
| `DocumentLocator` | 10 | **comes over** |
| `DocumentLocator.Client` | 3 | **dropped** — it exists only to talk to the service over a pipe |
| `DocumentLocator.Service` | 4 | **dropped** — KitveiHakodeshService takes its place |
| `DocumentLocator.Tests` | 8 | **dropped** (they cannot run under `dotnet test` anyway) |
| `DocumentLocator.Demo` | — | **dropped** |

**3. Three files inside the library die with the pipe.** `PipeProtocol.cs` (435),
`PipeClient.cs` (209), `ServiceSetup.cs` (108) — **752 lines** — exist to serve the
client/service split. The csproj already excludes all three from the net10 leg
(`<Compile Remove>`) because *"KitveiHakodeshService has no use for them"*, which is the
same conclusion reached earlier by a different route.

What remains is the real index logic, ~2757 lines:
`PathIndex` (1206, the Lucene index) · `MftCrawler` (596, NTFS MFT scan) ·
`UsnJournal` (484, change journal) · `ExcludedFoldersPersistence` (222) ·
`FallbackCrawler` (154, non-NTFS) · `IdleGuard` (68).

**4. Core owns the surface.** Core references the DocumentLocator project; the Service and
Lib reach file search **through Core**, never the project directly (the existing Lib
`DocumentLocatorAdapter` becomes that seam). Carry over the csproj lessons: keep explicit
`OutputPath` and `PlatformTarget=AnyCPU`, or expect CS1566 and `BadImageFormatException`.

**5. Consumers to repoint:** `KitveiHakodeshLib.csproj` and `KitveiHakodeshDemoApp.csproj`
both reference `DocumentLocator.Client` today; `KitveiHakodesh.slnx` lists Client, Service
and Tests. All three change in this slice.

**⏰ FOLLOW-ON MIGRATION (separate, later — not this slice):** DemoApp uses
**KitveiHakodeshService when it is installed**, and falls back to the DocumentLocator logic
**in-process** when it is not. It never talks to a DocumentLocator service, because none
exists after this.

---

## 11. Open decisions

| # | Decision | Notes |
|---|---|---|
| 1 | **Lucene version** | 00018 (Service) vs 00016 (only one in `packages/`, proven on net48 via DocumentLocator). NuGet already unifies to 00018 in the Service process. **Blocks slice 6** |
| 2 | **`idx_link_type_target_line`** | Keep creating it (dev starts writing to the corpus) or drop it (hosted may regress)? **Blocks slice 4** |
| 3 | **Connection model** | Adopt Lib's explicit 8-connection pool + pragmas, or fresh-per-call relying on `Microsoft.Data.Sqlite` pooling? **Blocks slice 4** |
| ~~4~~ | ~~Authoritative `Dictionary.db`~~ | **VOID** — there was no drift (gotcha 10 retracted) and the frontend copy has no reader (gotcha 14). Nothing to choose: keep Core's, delete the other. No longer blocks slice 2 |
| 5 | **`ShellRegistration`** | Borderline — reusable mechanism, but it mutates machine state outside the app's own files |
| 6 | **`WordExporter`** | Sets `app.Visible = true` (lines 58, 99). User-visible output, though not UI code. In Core or not? |
| 7 | **Resource delivery** | Core declares `Content` and relies on transitive flow to the legacy consumer, or each consumer links by relative path (the proven `expansion-routed.db` pattern)? |
| ~~8~~ | ~~`DbChangeStamp` rename~~ | **RESOLVED by rule 10** → `DbFileFingerprint`. Costs churn: the tests reference `Common.DbChangeStamp` in 5 places — update them in slice 7 |
| 9 | **`SearchParallel` — keep or delete?** | Zero production callers; appears only in `FetchBenchTest` (serial vs parallel) and one `DocSourceTest` assertion. Looks like a perf experiment that never shipped — check it against the verified-rejected list in the FtsLib perf audit before migrating it. **Blocks nothing; decide during 4b** |

---

## 12. Constraints to remember

- **Encoding:** UTF-8 **without BOM** (`.kiro/steering/file-encoding.md`). Never use
  PowerShell `Get-Content`/`Set-Content` on source files. Note the three Service updater
  placeholders and `FtsLib.Net48/SegmentWriter.cs` currently carry a BOM.
- **Corpus text:** several files are dense with Hebrew (Catalog: 1484 occurrences).
  Screen with `Grep` before reading; prefer file moves + surgical edits.
- **Tests:** `KitveiHakodeshService.Tests` (2594 lines, `net10.0-windows`) couples only to
  `Catalog` (6 usings) and `Common.DbChangeStamp` (5). Slices 1–5 do not break it; slice 6 does.
- **Consumers of the net48 leg:** the Word VSTO **and** `KitveiHakodeshDemoApp` (which also
  references `UpdateCheckerLib` directly).
- **Native `e_sqlite3`:** managed DLLs generally flow across a legacy → SDK ProjectReference;
  the native asset under `runtimes\win-x64\native\` does not do so reliably. This lands on the
  same VSTO native-probe problem the interop preload hit (the `SqliteNativeLoader` attempt was
  removed 2026-08-19 — it caused more problems than it solved; only the
  `PreLoadSQLite_BaseDirectory` env var in `ThisAddIn_Startup` remains), and is the one item
  only a real build under Word can settle.

### Slice 10 — ⏰ JSON → MessagePack sweep (POST-MIGRATION — REMIND ME)

**This is the reminder.** Rule 0e is decided now and Core is built MessagePack-native from
slice 0, but the app-wide sweep happens **after** the Core migration lands. Do not attempt it
mid-migration — it touches every layer at once and would make every other slice unreviewable.

What still speaks JSON today, and must be converted:

| Where | What |
|---|---|
| `KitveiHakodeshLib/Bridge/JsBridge.cs` | posts/receives JSON over the WebView2 bridge |
| `KitveiHakodeshService/Ipc/Rpc.cs` | `RpcJsonContext` registers **107** types for `System.Text.Json` |
| `KitveiHakodeshService/Http/*` | HTTP responses |
| `UserSettingsService` | `JsonElement[]` parameters, `QueryRowsJson` returns a JSON **string** |
| `vue-frontend` | `serviceClient` already decodes MessagePack; the bridge path does not |

**Order matters — convert a whole path end-to-end before starting the next.** Converting a
layer at a time is precisely what produces the decode-then-re-encode bug rule 0e warns about.

**Verification that actually catches it:** for each path, assert the payload is MessagePack
bytes at *every* hop. A passing feature test proves nothing — a double conversion is correct,
just wasteful and slow.

Keep JSON where rule 0e says it belongs: human-editable files, logs, diagnostics, the GitHub
releases API.

### Slice 11 — Cleanup (POST-MIGRATION, rule 0f)

Deferred here because none of it blocks a slice, and folding it in would bloat diffs that
have to be reviewed for correctness. Each item is verified dead, not merely suspected.

| Item | Evidence |
|---|---|
| `KitveiHakodeshLib/HebrewBooks.db` | **tracked** but declared in no csproj; byte-identical (`4e417017…`) to the two live copies |
| `vue-frontend/public/dictionary/KitveiHakodesh_dictionary.db` (9.5 MB) | **no reader anywhere** — both transports go through C# (`dictionaryDb.ts` says so). Copied into `dist/` on every build |
| `catalog_abbreviations.csv` + `csv_to_json.py` | CSV last touched 2026-07-22 while the json/`.g.cs` moved together 2026-08-13 — out of the flow for weeks |
| `catalog_abbreviations.json` + `gen_catalog_abbreviations.py` + `CheckCatalogAbbreviationsFresh` | verified 1:1 with the generated C# (275 = 275). **Port the two invariants to unit tests first** — no quote glyph in a key, no colliding keys |
| `wikidictionary.db` | referenced in exactly ONE line; the file exists nowhere in the repo, so `_wikiDb` is always null and `QueryWiki` always no-ops. **Decide: finish it or delete it** |
| Lib's `Dapper` / `Dapper.SqlBuilder` references | unused once Core is raw ADO (rule 0d). Note `Dapper.SqlBuilder`'s HintPath points three levels up, unlike every sibling — it would break on a fresh clone |
| `NET10_SPLIT_AND_OPTIMIZATION.md` | 7 references to the now-deleted `SearchParallel` |

**Not here — these belong to their slices**, because leaving them would mean the same logic
exists twice: the old `UserSettingsDbAccess` / `DbAccess` / `HebrewBooksDb` / `DictionaryService`
copies, `FtsLib/SeforimDb/*`, the `__webviewQuery` / `__webviewDictQuery` bridge actions, and
the Mutex-based build lock.

