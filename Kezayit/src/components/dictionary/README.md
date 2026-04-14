# dictionary/

Dictionary page. Singleton route `/dictionary`. Queries two sources simultaneously: the live Hebrew Wiktionary API and the local Aramaic dictionary DB (`public/dictionary.db`).

## Files

`DictionaryPage.vue` — orchestrator only. Wires `useWiktionary`, `useAramaicSearch`, and `useDictSuggestions` together. Owns the tab state, search bar, and routes actions between the two panes.

`DictionaryListPane.vue` — the רשימה tab. Shows one row per source entry — the same headword appears multiple times if it exists in multiple Aramaic dictionaries, each with its own definition. This is intentional: each row represents a distinct meaning from a distinct source.

`DictionaryDetailsPane.vue` — the פרטים tab. Shows the full entry for the current search term. At the top, a "הצעות" chip strip shows nearby headwords (within 2 chars of the query length) — deduplicated by headword so each word appears once regardless of how many sources it has. Chips are shown even when no results were found for the current word.

`useDictSuggestions.ts` — composable that owns the merged autosuggest list. Takes the wiki suggestions ref, the aramaic getter, and the debounced query as inputs (shares the same composable instances from the parent — no duplicate fetches). Filters to Hebrew-only headwords. Returns `suggestions` (one entry per source, for the list tab) and `clearSuggestions`.

`useWiktionary.ts` — live Wiktionary lookup. Fetches wikitext from `he.wiktionary.org/w/api.php`, parses it into `WiktionarySense[]`. Exposes `senses`, `title`, `suggestions` (from OpenSearch), and search actions.

`useAramaicSearch.ts` — Aramaic DB lookup via `queryDict()`. `search(term)` loads full senses in 5 queries (bulk, no N+1). `getSuggestions(prefix)` returns one row per `(headword, source)` combination with all definitions for that source concatenated.

`WiktionaryEntry.vue` — renders a single `WiktionarySense`: headword, nikud, pos badge, binyan, etymology, shoresh, definitions with examples, named sections, and translations.

`DictionaryEntryView.vue` — renders entries from the old book-based dictionary (HTML content).

`DictionarySearchResults.vue` — virtual-scrolled results list for the old book-based dictionary search.

`DictionaryBookShelf.vue` — book shelf shown when no search is active.

`useDictionarySearch.ts` — search logic for the old book-based dictionary.

`useDictionary.ts` — loads reference books for the shelf.

## List tab vs Details tab — key difference

The list tab (רשימה) shows one row per `(headword, source)` pair. דילמא appears 3 times because it exists in 3 different Aramaic dictionaries with 3 different definitions. Clicking a row fills the search input and switches to the details tab.

The details tab (פרטים) shows the full entry for the searched word. The הצעות chip strip at the top is deduplicated by headword — דילמא appears once as a chip even though it has 3 source entries. Clicking a chip searches for that word.

## Data sources

**Offline Wiktionary** (`public/wikidictionary.db`) — Hebrew words. Queried via `queryWikiDict()`. Definitions with inappropriate layer tags are filtered out before display — see `BLOCKED_LAYERS` in `useWiktionary.ts`. No network required.

**Aramaic DB** (`public/dictionary.db`) — 7,754 senses from 6,987 entries across 4 Aramaic dictionaries + abbreviations, imported from `FinalDictionary.txt` in the ToratEmet installation. `***` separators in the source data are split into separate sense rows at import time. `(=expansion)` prefixes are extracted into the `etymology` column. Schema: `source → sense → definition / example / section / section_item / translation`.

## SQL queries (`public/dictionary.db`)

- `DICT_SUGGEST` — one row per `(headword, source)` with all definitions concatenated, for the list tab
- `GET_DICT_SENSES_FOR_WORD` — all senses for a headword with source label joined
- `GET_DICT_ALL_DEFINITIONS/EXAMPLES/SECTIONS/TRANSLATIONS` — bulk fetches for all senses at once (no N+1)

## SQL queries (`public/wikidictionary.db`)

- `WIKIDICT_SUGGEST` — one row per headword with first definition, for autosuggest
- `GET_WIKIDICT_SENSES_FOR_WORD` — all senses for a headword with source label joined
- `GET_WIKIDICT_ALL_DEFINITIONS` — bulk fetch; includes `filter_tag` column for content filtering
- `GET_WIKIDICT_ALL_EXAMPLES/SECTIONS/TRANSLATIONS` — bulk fetches (no N+1)

## Re-importing Aramaic data

`node scripts/dictionary/import-aramaic.cjs` — reads from `FinalDictionary.txt`, rebuilds the DB. Idempotent.
`node scripts/dictionary/create-dictionary-db.cjs` — drops and recreates the schema from scratch.

## Wiktionary offline DB (`public/wikidictionary.db`)

A second local SQLite DB built from the kaikki.org Hebrew Wiktionary JSONL dump. Same normalized schema as `dictionary.db` with one addition: `definition.filter_tag` stores the raw layer tag string from the wikitext (e.g. `גס`, `סלנג`, `ספרות`, `עברית מקראית`). NULL means untagged — the vast majority of entries. The tag is stored at import time so filtering rules can be changed without re-importing.

Queried via `queryWikiDict()` from `src/host/db.ts`. SQL constants live in `src/host/queries.sql.ts` under the `WIKIDICT_*` prefix.

### Building the wiki dict DB

```
npm run wikidict:import
```

This downloads the kaikki.org dump to `data/kaikki-hewiktionary.jsonl` (one-time, ~50MB), recreates the schema, and imports all Hebrew entries. Idempotent — safe to re-run.

To use a local dump file: `DUMP_PATH=/path/to/file.jsonl npm run wikidict:import`

### Deploying to C#

Copy `public/wikidictionary.db` to `CSharpBackend/KezayitLib/bin/{Config}/kezayit/wikidictionary.db` alongside `dictionary.db`. The C# host opens it read-only at startup via `AppViewer._wikiDictDb` and handles `wikidict-sql` actions from JS.

## Content filtering (Wiktionary offline)

`useWiktionary.ts` filters out definitions that carry inappropriate layer tags before returning results. The `BLOCKED_LAYERS` set lists the tags that are silently dropped: `גס` (vulgar), `סלנג` (slang), `מדובר`/`דיבורי` (colloquial), `ארגו`/`ז'רגון` (jargon), `פוגעני`/`גנאי` (offensive). These are stored in `definition.filter_tag` in `wikidictionary.db`. Untagged definitions — which covers the vast majority of biblical, Talmudic, and rabbinic Hebrew — pass through unchanged. If all definitions in a sense are blocked, the entire sense is dropped automatically. To add or remove blocked tags, edit `BLOCKED_LAYERS` in `useWiktionary.ts`.

## Planned features

### Custom dictionary quotes tab

A new tab in the dictionary page for displaying quotes from the main app DB books. When a word is searched, this tab would show relevant passages from books like מלבים באור המילות (source 23) and מצודת ציון (source 20) that contain or define the word. These books already exist in the main app DB and are accessible via `useDictionarySearch.ts` (sources 20 and 23 are already mapped there). The tab would use the existing `DictionarySearchResults` / `DictionaryEntryView` infrastructure to render the HTML book content.

### מקורות tab

A dedicated tab showing results from classical Jewish lexicographic sources available in the main app DB. Planned sources:

- מלבים באור המילות (source 23) — Malbim's lexicon of biblical Hebrew
- מצודת ציון (source 20) — Metzudat Zion, biblical word definitions
- Additional sources from categories 75 and 1220 (מילונים וספרי יעץ, ספרות עזר) as they become available

This tab would reuse `useDictionarySearch` with a filter limiting results to these specific source IDs, and render via the existing `DictionarySearchResults` component. The "open in viewer" button on each result would navigate to the book at the relevant line.
