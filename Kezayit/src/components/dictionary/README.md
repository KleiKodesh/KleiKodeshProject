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

**Live Wiktionary** (`he.wiktionary.org`) — Hebrew words. No caching — fetched fresh on every search.

**Aramaic DB** (`public/dictionary.db`) — 7,754 senses from 6,987 entries across 4 Aramaic dictionaries, imported from `FinalDictionary.txt` in the ToratEmet installation. `***` separators in the source data are split into separate sense rows at import time. `(=expansion)` prefixes are extracted into the `etymology` column. Schema: `source → sense → definition / example / section / section_item / translation`.

## SQL queries (`public/dictionary.db`)

- `DICT_SUGGEST` — one row per `(headword, source)` with all definitions concatenated, for the list tab
- `GET_DICT_SENSES_FOR_WORD` — all senses for a headword with source label joined
- `GET_DICT_ALL_DEFINITIONS/EXAMPLES/SECTIONS/TRANSLATIONS` — bulk fetches for all senses at once (no N+1)

## Re-importing Aramaic data

`node scripts/import-aramaic.cjs` — reads from `FinalDictionary.txt`, rebuilds the DB. Idempotent.
`node scripts/create-dictionary-db.cjs` — drops and recreates the schema from scratch.
