# dictionary/

Dictionary page. Singleton route `/dictionary`. Queries two databases: `public/dictionary/kezayit_dictionary.db` for the dictionary, and the main seforim DB for מצודת ציון, מלבי"ם באור המילות, and מחברת מנחם.

## Files

`DictionaryPage.vue` — page shell. Search input via `BottomSearchBar`, debounces the query, calls `combinedLookup`, passes `WordPageData` to `DictionaryWordPage`. Handles spelling suggestions (see lookup scenarios below) when no exact match is found. Manages zoom state via `useZoomHandler` — zoom is stored globally in `settingsStore.dictionaryZoom` (persisted to localStorage) and passed as `fontPx` to `DictionaryWordPage`.

`DictionaryWordPage.vue` — renders a looked-up word. Three sections stacked vertically:
- מחברת מנחם results (scrollable, capped height) — shown directly under the title
- Definitions — all sources merged into one grouped list, grouped by word form (nikud if present, else plain headword)
- קשורים — synonyms, related links, spelling variants

Source labels for מצודת ציון and מלבי"ם are Ctrl+clickable — opens the source book in a new tab at the matched line. מחברת מנחם entries are also Ctrl+clickable for the same reason.

## Data flow

`DictionaryPage.vue` calls `combinedLookup(term)` which runs all three tiered sources in parallel through a shared progression: exact → prefix → contains. All three exact queries fire together; if any source returns results at a tier, the lower tiers are skipped entirely. The result carries `dictRows`, `metzudatRows`, `malbimRows`, and `isExact`.

מחברת מנחם runs independently in parallel via `menchemLookup` — it is contains-only (no tier progression) and does not participate in the tier gating.

Dictionary senses with `source_id = 7` (רד"ק) are split out into `radak` and grouped with the seforim sources in the display, not with the dictionary senses.

Related words (synonyms, links, variants) are only fetched when `isExact` is true.

## DB Schema (dictionary DB)

`source_kind(id, name)` — 7 sources including רד"ק (id=7).

`link_kind(id, name, explanation)` — נרדף | כתיב | ראו_גם | ניגוד | נגזרת.

`word(id, headword UNIQUE)` — one row per distinct headword.

`sense(id, word_id → word, nikud, text, source_id → source_kind)` — all definitions and abbreviations; multiple rows per word, one per source.

`link(word_id → word, target_id → word, kind_id → link_kind)` — composite PK on all three columns.

## Seforim DB queries (מצודת ציון / מלבי"ם / מחברת מנחם)

All three sources are queried from the main seforim DB. Book IDs are looked up at runtime by title pattern and cached — never hardcoded.

**מצודת ציון / מלבי"ם**: Lines are matched against the bold header tag only. The SQL pattern `<b>TERM...</b>%` ensures the term ends inside `<b>...</b>` and is never matched against the definition body. Each `MetzudatRow` carries `bookId`, `lineId`, and `lineIndex` for Ctrl+click navigation.

**מחברת מנחם**: The book has two sections. The dictionary section uses `<strong><big>HEADWORD</big></strong>` lines — the term is matched inside the `<big>` tag (with and without trailing space), and the next line is returned as the definition. The synonym section uses lines where multiple `<b>WORD</b>` entries appear — the term is matched as the end of a bold word (`%<b>%TERM</b>%`) to prevent false positives from plain text between tags. The nearest preceding pure-bold line is returned as the section title. Both sections use contains matching inside the tag patterns.

## Lookup scenarios

**Exact match** — `combinedLookup` finds the term at tier 1 (exact). `isExact: true` is returned. Related words (synonyms, links, כתיב variants) are fetched in a second parallel round. No suggestions shown.

**Prefix / contains match** — `combinedLookup` falls through to tier 2 or tier 3. `isExact: false`. Results are shown as-is (e.g. typing a root prefix shows all words that start with it). No suggestions shown because there are results.

**No results at all** — `combinedLookup` returns empty across all tiers and all sources. The no-results bar appears and suggestions are computed in two stages:

1. **כתיב חסר expansion** (`expandKetivHaser` in `src/utils/hebrewKetivExpander.ts`) — strips all ו/י from the query to get the bare consonant skeleton, then enumerates every combination of reinserting ו, י, or nothing at each inter-consonant gap (up to 40 variants). All variants are checked in a single `WHERE headword IN (...)` query against the `word` table via `dictKetivVariants`. This catches the common case where the user typed a חסר spelling (e.g. ארוסין) but the DB headword is the מלא form (e.g. אירוסין).

2. **Levenshtein fallback** — only runs if כתיב חסר expansion returned zero hits. Fetches candidate headwords that share the first 2–3 characters with the query (`dictSpellCandidates`), then ranks them by edit distance. Catches genuine typos and unrelated spelling differences.

The two stages are sequential — כתיב חסר is tried first, Levenshtein only fires on a miss. Up to 8 suggestions are shown; clicking one sets it as the search query.

## Query layer

Three files handle all query logic:

`src/webview-host/dictionaryDb.sql.ts` — all SQL strings for the dictionary DB. No inline SQL anywhere else in the dictionary layer.

`src/webview-host/dictionaryDb.ts` — dictionary DB query functions (`dictLinks`, `dictSynonyms`, `dictVariants`, `dictSpellCandidates`, `abbrevLookup`) and the main entry point `combinedLookup`. Routes through `__webviewDictQuery` (C# host) or the `/query-dict` Vite dev middleware.

`src/webview-host/dictionarySeforimDb.ts` — seforim DB queries for מצודת ציון, מלבי"ם, and מחברת מנחם. Exports `boldExact`, `boldPrefix`, `boldContains`, `getMetzudatBookIds`, `getMalbimBookIds`, and `menchemLookup`. Uses the same seforim DB transport as the rest of the app.

## Rebuild

`Misc/scripts/dictionary/import_radak_definitions.py` — imports clean definitions from ספר השרשים לרד"ק. Re-runnable.
`Misc/scripts/dictionary/import_radak_roots.py` — imports 2,048 roots into a standalone `root_form` table (not used by the app directly).
