# dictionary/

Dictionary page. Singleton route `/dictionary`. Queries `public/dictionary/kezayit_dictionary.db`.

## Files

`DictionaryPage.vue` — page shell. Search input via `BottomSearchBar`, fetches data on query change, passes `WordPageData` to `DictionaryWordPage`.

`DictionaryWordPage.vue` — renders a looked-up word: definitions (משמעויות) and related words (קשורים). No grammar/nikud section.

## DB Schema

```
source_kind(id, name)
link_kind(id, name, explanation)                              ← נרדף | כתיב | ראו_גם | ניגוד | נגזרת
word(id, headword UNIQUE)                                     ← one row per distinct headword
sense(id, word_id → word, nikud, text, source_id → source_kind)
link(word_id → word, target_id → word, kind_id → link_kind)  PK: (word_id, target_id, kind_id)
```

`word` is the identity table — one row per distinct headword. `sense` holds all content (definitions, abbreviations, Aramaic entries) — multiple rows per word, one per source. `link` uses integer FKs into `word`.

## Query layer

All SQL lives in `src/host/dictionaryDb.ts`.

Exported functions:

- `dictLookup(term)` — exact → prefix → contains on `sense.headword`; returns `SenseRow[]` + `isExact`
- `dictLinks(term)` — related words from `link` (excludes כתיב variants)
- `dictSynonyms(term)` — נרדף links only
- `dictVariants(term)` — כתיב links only
- `dictSpellCandidates(term)` — headword prefix scan for Levenshtein fallback
- `abbrevLookup(term)` — delegates to `dictLookup` (abbreviations are in the same table)

Routes through `__webviewDictQuery` (C# host) or `/query-dict` Vite middleware in dev.

## Rebuild

`Misc/scripts/dictionary/import_radak_definitions.py` — imports clean definitions from ספר השרשים לרד"ק. Re-runnable.
`Misc/scripts/dictionary/import_radak_roots.py` — imports 2,048 roots into a standalone `root_form` table (not used by the app directly).
