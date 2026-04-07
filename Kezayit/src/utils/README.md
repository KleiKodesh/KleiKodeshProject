# src/utils

Pure utility functions. No Vue, no Pinia, no reactivity. If a utility needs a ref or a store, it belongs elsewhere.

**normalizeText.ts** — `normalize(s)`: lowercases a string. Import this as the base normalization step before any search comparison. Do not add quote-stripping back here — that responsibility moved to `fuzzyMatch.ts`.

**fuzzyMatch.ts** — word-based fuzzy matching for book and HebrewBooks search. Use `scoreMatch` to get a numeric score (0 = all exact, higher = more fuzzy, Infinity = no match) and sort results by it. Use `fuzzyMatchWords` when you only need a boolean. Key design rule: words containing quote characters are treated as Hebrew acronyms and never fuzzy-matched — only exact or quote-stripped — because acronyms like `רשב"א`, `רשב"ם`, and `ריב"א` are all edit-distance 1 from each other and fuzzy-matching them produces false positives.

**tocSearchUtils.ts** — TOC-specific search used by the books-fs two-tier search. Use `splitQuery` to split a multi-word query into a book part and a TOC part, `buildTocSearchPaths` to build normalized paths from flat TOC rows, and `matchWords` for ordered subsequence matching. Exact matching only — do not introduce fuzzy logic here.

**idbPersistence.ts** — the only file in the app that touches IndexedDB. All IDB reads and writes go through here. Do not call any IDB API from anywhere else. Stores import from here; components and composables do not.

**commentaryNav.ts** — next/previous section navigation for the commentary panel, TOC-aware.

**hebrewTextProcessing.ts** — diacritics handling and text normalization for Hebrew display.

**censorDivineNames.ts** — replaces ה with ק in divine names when the censoring setting is enabled.

**scrollToIndexWithRetry.ts** — scroll-to-index for `@tanstack/vue-virtual` that retries until the target item has rendered. Use this instead of calling `scrollToIndex` directly when the list may not have rendered the target yet.

**resetState.ts** — exports a single `resetting` ref set to `true` just before an app reset. Check this before any interaction that should be blocked during reset.
