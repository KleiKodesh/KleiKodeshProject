# src/utils

Pure utility functions. No Vue, no Pinia, no reactivity. If a utility needs a ref or a store, it belongs elsewhere.

**normalizeText.ts** — `normalize(s)`: lowercases and strips Hebrew/ASCII quote characters. Import this as the base normalization step before any search comparison.

**bookCatalogTree.ts** — pure data logic for the book catalog tree. Exports `buildTree`, `assignFullPaths`, `findCategoryMeta`, `ensureBookSearchMetadata`, and the `BookRow`, `CategoryRow`, `CategoryNode` types. No Vue or Pinia dependencies — import from here in stores, composables, and components alike. Do not put this back under `src/components/`.

**bookCatalogSearchNormalizer.ts** — normalizes book search paths and query strings for matching. `normalizeBookPath(text)` applies Hebrew-specific substitutions: שו"ע / שוע expand to שלחן ערוך; שולחן normalizes to שלחן. Also exports `areHebrewSpellingVariants(wordA, wordB)` and `decomposeHebrewWord(word)` for חסר/מלא spelling variant detection — two words are variants when they share the same consonantal skeleton and their vowel letter sets (yod/vav between consonants) are in a subset relationship. Must be applied symmetrically to both indexed titles and user queries. Add new book-search normalization rules here — never in `normalizeText.ts`.

**bookCatalogSearchMatcher.ts** — scores a normalized query word against a book's token list. `scoreWordAgainstTokens(word, tokens)` returns SCORE_EXACT (3) for exact or spelling-variant matches, SCORE_PREFIX (2) for prefix matches, SCORE_CONTAINS (1) for substring matches, SCORE_NONE (0) for no match. Used by `filterBooksByWords` in `bookCatalogTree.ts`.

**tocSearchUtils.ts** — TOC-specific search used by the file-system two-tier search. Use `splitQuery` to split a multi-word query into a book part and a TOC part, `buildTocSearchPaths` to build normalized paths from flat TOC rows (also strips nikud for חסר spelling tolerance), `matchWords` for ordered subsequence matching, and `stripTocTitleRoots` to remove root TOC entries that merely repeat the book title (pass `singleRootOnly: true` for strict single-root mode; omit for multi-book batch stripping). Nodes with a `level` field have it decremented automatically when their parent root is removed.

**persistence.ts** — the only file in the app that touches IndexedDB and localStorage. All IDB reads and writes go through here. Do not call any IDB API or `localStorage` directly from anywhere else. Stores import from here; components and composables do not.

**commentaryNav.ts** — next/previous section navigation for the commentary panel, TOC-aware.

**hebrewTextProcessing.ts** — diacritics handling and text normalization for Hebrew display.

**censorDivineNames.ts** — replaces ה with ק in divine names when the censoring setting is enabled.

**scrollToIndexWithRetry.ts** — scroll-to-index for `@tanstack/vue-virtual` that retries until the target item has rendered. Use this instead of calling `scrollToIndex` directly when the list may not have rendered the target yet.

**detectFonts.ts** — `detectAvailableFonts()` uses canvas measurement to detect which Hebrew and general fonts are installed on the user's system. Returns an array of font family name strings. Used by `FontSelector.vue` to populate the font picker with only fonts that are actually available.

**resetState.ts** — exports a single `resetting` ref set to `true` just before an app reset. Check this before any interaction that should be blocked during reset.

**hebrewCalendarLearning.ts** — `getDailyLearning(hd)` returns today's schedule for all daily learning cycles: Daf Yomi, Mishna Yomi, Nach Yomi, Yerushalmi, Rambam (1 and 3 chapters), Kitzur Shulchan Aruch, Chofetz Chaim, Psalms, Perek Yomi, Arukh HaShulchan, and Dirshu Amud Yomi. Used by both the home page date bar and the calendar weekly view. All Hebrew learning formatting lives here.
