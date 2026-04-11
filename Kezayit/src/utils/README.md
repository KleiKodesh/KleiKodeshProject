# src/utils

Pure utility functions. No Vue, no Pinia, no reactivity. If a utility needs a ref or a store, it belongs elsewhere.

**normalizeText.ts** — `normalize(s)`: lowercases and strips Hebrew/ASCII quote characters. Import this as the base normalization step before any search comparison.

**tocSearchUtils.ts** — TOC-specific search used by the books-fs two-tier search. Use `splitQuery` to split a multi-word query into a book part and a TOC part, `buildTocSearchPaths` to build normalized paths from flat TOC rows (also strips nikud for חסר spelling tolerance), and `matchWords` for ordered subsequence matching.

**idbPersistence.ts** — the only file in the app that touches IndexedDB. All IDB reads and writes go through here. Do not call any IDB API from anywhere else. Stores import from here; components and composables do not.

**commentaryNav.ts** — next/previous section navigation for the commentary panel, TOC-aware.

**hebrewTextProcessing.ts** — diacritics handling and text normalization for Hebrew display.

**censorDivineNames.ts** — replaces ה with ק in divine names when the censoring setting is enabled.

**scrollToIndexWithRetry.ts** — scroll-to-index for `@tanstack/vue-virtual` that retries until the target item has rendered. Use this instead of calling `scrollToIndex` directly when the list may not have rendered the target yet.

**detectFonts.ts** — `detectAvailableFonts()` uses canvas measurement to detect which Hebrew and general fonts are installed on the user's system. Returns an array of font family name strings. Used by `FontSelector.vue` to populate the font picker with only fonts that are actually available.

**resetState.ts** — exports a single `resetting` ref set to `true` just before an app reset. Check this before any interaction that should be blocked during reset.
