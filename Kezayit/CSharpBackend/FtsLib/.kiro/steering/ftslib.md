# FtsLib Project Guidelines

## What this project is
A full-text search library for a Hebrew/Aramaic seforim database (SQLite, ~5.4M lines). The search engine answers one question: **which lines contain all the search terms?** (AND semantics). Nothing more.

---

## Project structure

```
FtsLib/
  Core/
    IndexPaths.cs               ← base class: IndexPath + SegmentsDir
    IndexWriter.cs              ← public write API: Add(), Dispose()
    IndexReader.cs              ← public read API: Search(), SearchOr(); queries all live segments
    RamIndex.cs                 ← in-memory term→PostingStream map; searchable directly
    RamIndexEntry.cs            ← per-term PostingStream + skip list
    PostingStream.cs            ← delta+varint compressed byte buffer
    PostingIterator.cs          ← forward iterator with SkipTo; skip-list accelerated
    PostingMatcher.cs           ← Intersect / Union merge algorithms
    UnionIterator.cs            ← PostingIterator wrapper for OR groups
    SegmentStore.cs             ← orchestrates flush, merge cascade, live-state tracking
    SegmentMerger.cs            ← LSM merge logic: MergeLevel, ForceMergeAll
    SegmentReader.cs            ← forward-only .dat file reader
    SegmentWal.cs               ← write-ahead log for crash recovery
    Tokenizer.cs                ← HTML-aware, nikud-stripping tokenizer
  Misc/
    ZayitDb.cs                  ← opens source SQLite DB, streams rows, fetches line content

FtsLibTest/                     ← test + benchmark console app
  IndexTest.cs                  ← multi-tier build+search+validate test
  CostumeTest.cs                ← ad-hoc manual test
  Program.cs                    ← entry point
```

---

## How the index works (current implementation)

### Write path
1. `IndexManager.Add(term, lineId)` — called once per term per line, IDs must be **strictly ascending** (enforced by `PostingStream`)
2. `RamIndex` maps each term to an `Entry` object containing:
   - A `PostingStream` — a raw `byte[]` holding delta+varint compressed line IDs
   - A skip list — `int[]` of `(docId, byteOffset, prevEncodedValue)` triples, one entry every **128 doc IDs**
3. `PostingStream.Add(lineId)` encodes `(lineId - prevLineId)` as a varint and appends to the buffer
4. The skip list entry is written **before** each 128th entry, storing:
   - `Skip[n]`   = the docId of the skip target
   - `Skip[n+1]` = byte offset in the buffer where that entry's varint starts
   - `Skip[n+2]` = the **previous** entry's encoded value (needed to resume delta decoding after a jump)

### Read / Search path
1. `IndexManager.Search(terms)` sorts terms by frequency ascending (rarest first)
2. `MergeIntersect` creates one `PostingIterator` per term
3. The rarest list drives the outer loop; for each candidate ID, `SkipTo(target)` is called on all other iterators
4. `SkipTo` binary-searches the skip table to find the largest skip entry with `docId < target`, jumps the byte cursor there, then linear-scans at most 127 entries
5. Results are yielded lazily — **zero heap allocation** during search

### Codec invariant — CRITICAL
The delta codec requires doc IDs to be added in **strictly ascending order**. The SQL query in `FullDbTest.BuildIndex` uses `ORDER BY id` for this reason. **Never remove `ORDER BY id`** — without it, IDs come back in arbitrary order, deltas become negative (wrap as huge unsigned values), and the iterator reads garbage, producing millions of false results.

### Skip list invariant — CRITICAL
The skip entry stores `prevEncodedValue` = `Stream.LastEncoded` at the time the skip is written (i.e., the encoded value of the entry **before** the skip target). In `SkipTo`, after jumping to `bestOffset`:
```
_encoded = bestPrevEncoded;   // restore state to "just before" the skip entry
_encoded += ReadVarInt();     // read the delta → lands exactly on the skip entry's docId
```
If you change the skip write logic, you must update `SkipTo` to match, and vice versa. The `SkipListTest` suite must pass before any full DB run.

---

## Tokenizer behaviour

- **Hebrew letters**: U+05D0–U+05EA (alef–tav), kept as-is including final forms (ך ם ן ף ץ)
- **Nikud** (U+05B0–U+05C7): stripped silently — `שָׁלוֹם` → `שלום`
- **English**: lowercased, a–z only
- **HTML tags**: block-level tags (`<p>`, `<div>`, `<br>`, `<h1>`–`<h6>`, `<li>`, `<tr>`, `<td>`, etc.) act as word separators; inline tags (`<b>`, `<i>`, `<span>`, etc.) are invisible
- **HTML entities**: whitespace entities (`&nbsp;`, `&ensp;`, `&emsp;`) act as word separators; all others (`&amp;`, `&shy;`, etc.) are invisible
- The tokenizer **reuses internal buffers** (`_terms`, `_buffer`, `_tagName`) — it is not thread-safe and must not be shared across threads

---

## Stop words
`StopWords.cs` contains common Hebrew/Aramaic words filtered at index time. Currently **not applied** — `IndexManager.Add` does not call `StopWords.IsStopWord`. The class exists for future use. Do not silently re-enable stop word filtering without updating search to handle the case where a query term is a stop word (it won't be in the index).

---

## File length rule

- **No source file should exceed 200 lines.** If a file grows beyond that, split it by responsibility before adding more code.
- Each file should have one clear job: orchestration, I/O, merge logic, codec, etc. Mixed concerns are a sign the file needs splitting.

---

## Core design rules

- **Keep the codec**. `PostingCodec` + `PostingStream` are intentional — delta+varint compression keeps the index small. Never replace with `HashSet<int>` or `List<int>`.
- **Keep it simple**. AND-intersection only. No ranking, scoring, phrase search, or prefix matching.
- **Single-threaded indexing**. The codec requires ascending IDs. Parallel indexing requires partitioning by ID range and merging in order — do not attempt without explicit agreement.
- **No over-engineering**. No arenas, memory-mapped structures, or custom allocators unless asked.
- **`LiveDbTest.cs` is read-only**. Never modify it.
- **Tests live in `TokenizerTests.cs` and `SkipListTest.cs`**. Do not add tests to `Program.cs`.
- **Run `SkipListTest` before any full DB run** — it catches skip list bugs in seconds rather than 17 minutes.

## Search results must never be truncated — CRITICAL

Every layer of the search pipeline must return **all** matching results. Never introduce a `LIMIT`, `.Take(N)`, `MaxResults`, or any other cap at any layer:

- `IndexReader.Search` — lazy `IEnumerable<int>`, yields every matching doc ID
- `SearchService.RunSearch` — materializes the full ID list: `new List<int>(reader.Search(terms))`
- `ZayitDb.FetchSearchResults` — fetches every ID passed in, no `LIMIT` clause
- `ResultsHtmlService.Render` — renders every item in the list
- The ViewModel — passes the full result list to the HTML service, no slicing

If a query matches 50,000 lines, the user sees 50,000 results. Do not add pagination, virtual scrolling limits, or result caps without explicit agreement. The WebView2 renderer handles large HTML documents without issue.

---

## DB schema (key tables)
- `line(id, bookId, lineIndex, content, heRef)` — one row per line of text; `id` is the doc ID used in the index
- `book(id, title)` — book metadata
- Search results are fetched back from DB by matched line IDs after the index search

## Running tests
```
QuickTest.Run()        // 500k lines, ~1 min — use for development
FullDbTest.Run()       // all 5.4M lines, ~17 min
FullDbTest.Run(N)      // first N lines
```

---

## UI / MVVM coding style (FtsLibDemo)

Follow **Brian Lagunas** MVVM conventions for all WPF/XAML code:

- **ViewModelBase** — implement `INotifyPropertyChanged` with `[CallerMemberName]` and a `SetField<T>` equality-check helper.
- **Commands** — expose `ICommand` properties (never the concrete type). Use `AsyncRelayCommand` for async handlers and `RelayCommand` for sync ones. Hook `CanExecuteChanged` into `CommandManager.RequerySuggested`.
- **No `async void`** — command handlers must be `async Task`. The only acceptable `async void` is the `Execute` method inside `AsyncRelayCommand` itself.
- **No `ContinueWith`** — always use `async/await` instead of `Task.ContinueWith` + manual `TaskScheduler`.
- **Constructor injection** — ViewModels receive services through the constructor. Never `new` a service inside a ViewModel.
- **Composition root** — wire up all services and ViewModels in `App.xaml.cs OnStartup`. Nothing else should know the concrete types.
- **Clean code-behind** — `MainWindow.xaml.cs` must only call `InitializeComponent()`. All logic belongs in the ViewModel.
- **Thin ViewModel** — the ViewModel orchestrates; it does not do I/O, DB access, or file operations. Extract those to services with interfaces.
- **Services have interfaces** — every service the ViewModel depends on must have a matching `IXxxService` interface in the `Services/` folder.
- **`SearchResultItem` and similar display models** — immutable, constructor-set, get-only properties.
