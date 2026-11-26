# KitveiHakodesh.Core — rules for working in this project

Read this before writing a line of code here. The full migration plan is
`../MIGRATION-PLAN.md`; this file is the short version of what binds *this* project.

---

## ⚠ 1. The Service ships as NATIVE AOT

`KitveiHakodeshService` sets `PublishAot=true` and references this project, so **all of
Core is compiled by ILC**. No runtime IL generation, no reflection-driven behaviour.

- **NO DAPPER.** It emits materializers as IL at runtime — impossible under native AOT.
  Use raw ADO: `CreateCommand` / `ExecuteReader`.
- **No** `Activator.CreateInstance`, `Type.GetType`, `Expression.Compile`, `MakeGenericType`,
  `Assembly.Load`, `dynamic`, or `Emit`.
- Serialization must be **source-generated** — MessagePack attributes on the type;
  `JsonSerializerContext` if JSON is ever unavoidable.
- Anything that genuinely needs the above does not belong in Core: host-only code (Office
  Interop, the Word COM finder) lives in `KitveiHakodeshLib/Common` (net48), since e1e30b36.
- Keep `IsAotCompatible` on so warnings surface here, not at publish time.

## ⚠ 2. MessagePack is the only wire format

Core is **MessagePack-native**. Models carry `[MessagePackObject(keyAsPropertyName: true)]`
and are serialized directly by both transports — there is no mapping layer.

**Never decode MessagePack and re-encode the same payload as JSON**, or the reverse. It is
invisible in a diff and costs a full serialize+parse per hop. If a payload changes format
between two hops, that is a bug.

JSON is allowed only where a human reads it: config, hand-edited data files, logs,
diagnostics. Wire = MessagePack.

## 3. Core knows nothing about its callers

No `using KitveiHakodeshService.*`, no `KitveiHakodeshLib` types, no transport types, no
`WebBridge`, no HTTP, no WebView2.

- **Core finds its own files** via `Common/AppFileLocator` — there is no options object and
  nothing is injected. It probes candidate roots in order and takes the first that exists,
  falling back to `%LocalAppData%\KleiKodesh` last. Core reads **no environment variables**.
- **No app UI.** No `MessageBox`, no forms, no windows of our own. Return data. The one
  exception is native OS dialogs (file/folder pickers in `LocalFiles/Native*Picker.cs`). They
  take an owner HWND from the caller, run per-monitor-v2 DPI aware via `NativeDialogDpiScope`,
  and return the chosen path or null. They live here so every host shares one implementation.
- **Return data or throw** — never swallow an error into `Debug.WriteLine` (a no-op in
  Release). The orchestrator decides what the user sees.
- Core's public surface is set by **production callers, never by tests**.

## 4. The logic lives in FtsLib; Core supplies data and calls it

FtsLib must never open `seforim.db`. Equally, **Core must not re-implement or wrap engine
algorithms** — search, ranking, snippets, merging, index state are FtsLib's.

The recurring trap is *engine algorithm + one corpus query*, which looks like Core logic and
is not. Split at that seam: the query is Core's, the algorithm is FtsLib's.

## 5. Naming

- Would **any** developer know what this file is at a glance, without opening it?
- Name the **full subject**: `SeforimDb`, never `Seforim`.
- Use the **conventional** word — `Models`, `Options`, `Factory`, `Provider`, `Resources`.
  Never invent taxonomy.
- **No `*Service` suffix** — Core is a library, not a service.
- **Split by JOB, not by noun.** *Stamps, version, policy, state, probe* are usually
  bookkeeping owned by a job, not jobs themselves.
- SQL is split by layer, see section 5a. For the small databases (dictionary, HebrewBooks
  catalog) the SQL `const`s may still sit at the top of their queries file; seforim.db follows 5a.

## 5a. Layers: SQL strings / SQL calls / thinking (HARD RULE, user 2026-11-26)

Three kinds of file. **Nothing of one kind may live in a file of another kind.**

1. **SQL strings: ONE file** (`SeforimDb/SeforimDbSqlStrings.cs`). Every SQL text of the
   database, and nothing else. No inline SQL anywhere, no SQL `const` beside its caller.
2. **SQL calls: ONE file** (`SeforimDb/SeforimDbQueries.cs`). Open the connection, bind the
   parameters, run a string from (1), read the rows into plain data (a row, a list, a map).
   No decisions: no rules, grouping, ordering, caching, staleness or schema reasoning.
3. **Thinking files** (everything else: grouping, sections, ordering rules, tree-order
   rebuilds, caches, staleness). They call (2) to get data and never hold SQL text, a
   connection or a reader. A thinking function over plain data is testable without a database.

Corollary: what belongs in (1) or (2) must not be put in a thinking file for convenience, and
thinking must not be pushed into (1) or (2).

**The one flow, each step used only by the next:**
`SQL strings` -> used by `SQL calls (queries)` -> used by `logic (thinking)` -> used by the
`simple CLI-like API` -> used by the `service` (transport) -> sent to the `UI`.
The UI does not think about data. It displays. It may think about UI data (its own state,
selection, layout, presentation) but it never BUILDS queried data: no grouping, ordering,
sectioning, joining or deriving of what the database knows. If the UI builds queried data,
that thinking belongs in Core.

**The goal (user, 2026-11-26): Core is a wrapper for the seforim database, shaped like a CLI
tool, all in ONE dedicated folder** (`SeforimDb/`), knowing nothing of the service or the UI -
so that one day a real command-line tool can be built straight on it. Nothing of the seforim.db
wrapper lives outside that folder (today parts sit in `SeforimDbCatalog/`, `SeforimDbFullTextSearch/`,
`TalmudPdf/`, `UserSpace/` and others - the audit decides what moves in or stays a separate library).

**Core's public surface is a few simple commands**, like endpoints of a CLI tool: plain input
in (ids, a line range), plain data out (`GetCommentaryList`, `GetBookSummaries`, ...). Callers
(the service) see no connection, no SQL and no intermediate data; the service only forwards.
Core does the work behind the command in one call; the page only shows the result.

Known violations to clean up (audit 2026-11-26; clean examples: `SeforimDbCommentaryList.cs`,
`SeforimDbCommentaryBookFacts.cs`, `SeforimDbBookInfo.cs`, `SeforimDbWordLinkPreview.cs`):
- SQL text AND calls mixed with rules, whole file: `SeforimDbRelatedBookTarget.cs`,
  `SeforimDbTocSectionLookup.cs`, `SeforimDbBaseTextLookup.cs`, `SeforimDbRelatedBooks.cs`
  (3 texts + 2 readers), `SeforimDbBookProtectionTables.cs`, `SeforimDbContentFingerprint.cs`.
- A few lines each: `SeforimDbCommentaryFilterGroups.cs` (ReadReverseBooks), `SeforimDbBookTreeOrder.cs`
  (Load readers), `SeforimDbLineAnchorLookup.cs`, `SeforimDbSchema.cs`, `SeforimDbConnection.cs`
  (probes, staleness), `SeforimDbNoteIds.cs`, `SeforimDbLineContentCodec.cs` (zstd_dict read),
  `SeforimDbLineContentSql.cs` (SQL fragments belong in SqlStrings), `TalmudPdf/TalmudBavliPdfLocator.cs`
  (use GetBookSummaries), `UserAnnotations/LibraryIdMap.cs` (reads seforim.db).
- Thinking inside `SeforimDbQueries.cs`: catalog caches + `GetAllBooks` (tree order, protection,
  cache), the per-book commentary-id cache, the Seifim merge in `GetAllTocEntries`, LIKE rules in
  `GetTocTitlesForBooks`.
- Whole-catalog in memory: `SeforimDbBookByName.cs` (folded-title index), `GetAllCategories/GetAllBooks`.
- Outside `SeforimDb/` but part of the wrapper: the FTS corpus reader `SeforimDbFullTextSearch/SeforimDbFtsCorpus.cs`
  and the catalog indexer's readers (the Lucene engines themselves are a separate library).
- Public surface that is not CLI-like: raw intermediate data (`GetAllCategories/Books`, `GetReverseLineData`,
  `GetStaticFilterBooks`, `GetTocPathsForLines` returning two lists, `GetAllConnectionTypes`, flat-triple calls).
- The tree-order tables live in the COMPANION database (a sidecar file in the app's folder, per library,
  attached as schema `companion`), not in seforim.db: DDL in the library file moves its schema version,
  which the search-index stamps read, and would rebuild both indexes. Any future derived data goes there too.

## 6. One target: net10.0-windows

`<TargetFramework>net10.0-windows</TargetFramework>` only (since e1e30b36). The net48 hosts
(Word VSTO add-in, `KitveiHakodeshDesktopApp`) no longer reference Core: they reach it through
the service (`KitveiHakodesh.ServiceClient`), and `KitveiHakodeshLib` holds no Core, SQLite,
Lucene, FtsLib or MessagePack DLL. Don't add a net48 leg back; host-only needs go in Lib.

`InvariantGlobalization=true` is set on the Service, so **always specify
`Ordinal`/`Invariant` explicitly** — never rely on culture-sensitive defaults
(`StartsWith(string)`, `EndsWith(string)`, `IndexOf(string)`, `string.Compare`).

## 7. One copy of everything

One `Dictionary.db` (`Dictionary/`), one `HebrewBooksCatalog.db` (`HebrewBooks/`), declared as Content in Core's csproj only. No second copy anywhere, no
"keep them identical" rule. If a consumer seems to need its own copy, that is a delivery
problem to solve, not a copy to create.

## 8. Encoding

UTF-8 **without BOM**. Never use PowerShell `Get-Content`/`Set-Content` on source files —
it silently corrupts Hebrew. Several files here are dense with Hebrew source text; check
before bulk-editing.
