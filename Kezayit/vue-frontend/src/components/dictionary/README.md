# dictionary/

Dictionary page. Singleton route `/dictionary`. Queries the local Kezayit dictionary DB (`public/dicts/kezayit_dictionary.db`) only.

## Files

`DictionaryPage.vue` — the entire page. Search input at the bottom via `BottomSearchBar`, flat list of results above. No tabs, no sub-components.

`useKezayitDictionary.ts` — composable that owns search state. Calls `SEARCH_DICT_SENSES` and returns a flat `DictSense[]` array: `{ headword, definition, sourceLabel }`. One row per sense.

## Data source

`public/dicts/kezayit_dictionary.db` — Aramaic/Hebrew dictionary. Queried via `queryDict()` from `src/host/dictionaryDb.ts`. SQL constants live in `src/host/queries.sql.ts` under the `SEARCH_DICT_SENSES` and `DICT_SUGGEST` keys.

## SQL queries used

- `SEARCH_DICT_SENSES` — exact + prefix match on headword, returns headword + first definition + source label, limit 100
- `DICT_SUGGEST` — prefix match for autosuggest (not currently used in the UI but kept for future use)
