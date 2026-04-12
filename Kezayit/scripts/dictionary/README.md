# Dictionary DB — Build Spec

Everything needed to rebuild `public/dictionary.db` from scratch.

## Run

Stop the dev server first (it holds a read lock), then:

```
node scripts/dictionary/build-dictionary-db.cjs
```

Output: `public/dictionary.db` (~6.7 MB, ~22,598 entries). The script writes to `public/dictionary.db.tmp` first then renames atomically.

## Files in this folder

`build-dictionary-db.cjs` — the only script you need to run.

`torat-emet-dictionary.txt` — pre-exported UTF-8 copy of the ToratEmet FinalDictionary.txt. The build script reads this directly, so no access to the original ToratEmet installation is needed. If this file is present it takes priority; the original Win-1255 file is only used as a fallback.

`README.md` — this file.

## What the DB is

A pre-built SQLite dictionary shipped with the app. The Vue dictionary page (`/dictionary`) queries it via `queryDict()` in `src/host/db.ts`, which hits `window.__webviewDictQuery` in C# or `/query-dict` in the Vite dev middleware. The C# handler is `DictionaryHandler.cs`. The background indexer `DictionaryIndexer.cs` runs on first launch to add entries from the user's main DB.

## Schema

```sql
CREATE TABLE entry (
  id         INTEGER PRIMARY KEY AUTOINCREMENT,
  headword   TEXT    NOT NULL,   -- consonants only, stripped of nikud, indexed
  nikud      TEXT,               -- vocalized form shown in UI
  definition TEXT    NOT NULL,   -- see Definition Format below
  type       TEXT    NOT NULL,   -- 'aramaic' | 'abbrev' | 'book'
  source     INTEGER NOT NULL,   -- 0-3 = txt files, 10-13 = main DB books
  bookId     INTEGER,            -- main DB book id (source >= 10 only)
  lineIndex  INTEGER             -- line in main DB (source >= 10 only)
);

CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT);
-- meta keys: version='1', txt_indexed, db_indexed, db_book_{bookId}
```

Indexes: `headword`, `type`, `source`.

## Source 1 — FinalDictionary.txt (ToratEmet)

**Path:** `C:\Users\Admin\Documents\ToratEmetInstall\Dictionaries\FinalDictionary.txt`  
**Encoding:** Windows-1255. Use `iconv-lite` to decode.  
**Entries:** ~6,983

### Line format

```
[0-3] headword={nikud} definition *** {nikud2} alt_def
```

- Numeric prefix 0–3 = source file (stored as `source` column)
- `{nikud}` at the start of the definition = vocalized form of the headword → stored in `nikud` column; rest is `definition`
- `***` separates multiple forms/meanings of the same word — kept as-is in `definition`, parsed at display time
- `(=...)` in definition = abbreviation expansion — kept as-is, rendered specially for `type='abbrev'`
- Headword may already contain nikud (strip it for the `headword` column, keep original as `nikud` if no `{nikud}` block)

### Source files merged into FinalDictionary.txt

| Prefix | Original file   | Content                 | Nikud          | Entries |
| ------ | --------------- | ----------------------- | -------------- | ------- |
| 0      | dictionary1.txt | Basic Aramaic, no nikud | None           | ~2,788  |
| 1      | dictionary2.txt | Aramaic + ראשי תיבות    | Partial (~394) | ~1,775  |
| 2      | dictionary3.txt | Single etymology entry  | None           | 1       |
| 3      | dictionary4.txt | Targum-style Aramaic    | Every entry    | ~2,516  |

The same headword often appears in multiple source files. The build script deduplicates by headword, keeping the richest version: source 3 > source 1 > source 0. This reduces ~6,983 raw lines to ~6,266 unique entries.

### Type detection

- `abbrev` if headword contains `"` `״` `׳` `'` (abbreviation markers)
- `aramaic` otherwise

## Source 2 — Main seforim DB (4 dictionary books)

**Path:** `C:\Users\Admin\AppData\Roaming\io.github.kdroidfilter.seforimapp\databases\seforim.db`  
**Entries:** ~16,332

| Book             | bookId | source | Entry line detection                                     | Headword extraction |
| ---------------- | ------ | ------ | -------------------------------------------------------- | ------------------- |
| ספר הערוך        | 473    | 10     | `content LIKE '%<big>%' AND content NOT LIKE '<h%'`      | `<big>WORD</big>`   |
| הפלאה שבערכין    | 471    | 11     | `content LIKE '%<b>%' AND content NOT LIKE '<h%'`        | first `<b>WORD</b>` |
| ספר השרשים לרד"ק | 6105   | 12     | `content LIKE '<h3>%' AND content NOT LIKE '<h3>הקדמה%'` | `<h3>ROOT</h3>`     |
| אוצר לעזי רש"י   | 472    | 13     | `lineIndex >= 4`                                         | first `<b>WORD</b>` |

All have `type='book'`. Definition = HTML stripped to plain text, truncated at 500 chars. Full HTML is fetched live from the main DB when the user opens an entry.

ספר השרשים entries span 2 lines (`<h3>` + content) — the app fetches both when displaying.

## Definition Format (display-time parsing)

The Vue component `DictionaryEntryView.vue` parses `definition` at render time:

1. Split on `***` → each segment is a separate "sense"
2. Each sense may start with `{nikud}` → extract as the sense's vocalized form
3. `(=...)` at start of sense → abbreviation expansion, shown in italic
4. HTML tags in `book` entries → rendered with `v-html`

## Dependencies

`better-sqlite3` and `iconv-lite` — both already in `devDependencies`.
