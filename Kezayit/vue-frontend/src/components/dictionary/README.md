# dictionary/

Dictionary page. Singleton route `/dictionary`. Queries `public/dictionary/kezayit_dictionary.db` — a normalized Aramaic/Hebrew dictionary with a graph schema.

## Files

`DictionaryPage.vue` — the entire page. Search input at the bottom via `BottomSearchBar`, flat list of results above.

`DictionaryRow.vue` — single result row. Displays headword (vocalized if one nikud variant exists), separator, definition (vocalized if nikud available), and source pill.

`useKezayitDictionary.ts` — composable owning search state. Calls `dictLookup()` and `dictFuzzyCandidates()` from `src/host/dictionaryDb.ts`. Assembles the structured result into a flat `DictSenseDisplay[]` array for rendering.

## Data source

`public/dictionary/kezayit_dictionary.db` — normalized graph schema:

- `entry(id, term)` — plain unvocalized terms (headwords + single-word definitions)
- `meaning(id, text)` — multi-word definition strings, kept as-is with any nikud
- `synonym_link(from_id, to_id, source_id)` — bidirectional term→term links (headword ↔ single-word definition)
- `entry_meaning(entry_id, meaning_id, source_id)` — headword → multi-word definition links
- `nikud(id, entry_id, form)` — vocalized variants of entry terms, display metadata only
- `source(id, name)` — source dictionary names

## Query architecture

Dictionary queries go through `src/host/dictionaryDb.ts`, which owns all SQL strings and calls `__webviewDictQuery` (C# host) or `devQueryDict` (Vite dev middleware). No SQL lives in `queries.sql.ts` for the dictionary.

The JS side exposes two functions:

- `dictLookup(term)` — exact + prefix match on `headword`, returns `DictRow[]`
- `dictFuzzyCandidates(pattern)` — contains match for Levenshtein fallback, returns `string[]`

Both directions (forward: search by headword, reverse: search by definition) work via the same `headword` index because reverse rows are inserted at build time.

## Dev fallback sync rule

`devQueryDict` in `src/host/devFallbacks.ts` hits the Vite `/query-dict` middleware with the same SQL that `dictionaryDb.ts` sends to C#. Since the SQL lives in one place (`dictionaryDb.ts`) and both paths use it, there is nothing to keep in sync — the dev and C# paths are identical by construction.

## Rebuild script

`Misc/scripts/dictionary/rebuild_dict_final_schema.py` — rebuilds the DB from the original git data (commit `f88ed51`). Run after any schema change or source data update.
