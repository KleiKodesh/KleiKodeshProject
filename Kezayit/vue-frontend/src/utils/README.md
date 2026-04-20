# src/utils

Pure utility functions. No Vue, no Pinia, no reactivity. If a utility needs a ref or a store, it belongs elsewhere.

**normalizeText.ts** — `normalize(s)`: lowercases and strips Hebrew/ASCII quote characters. Import this as the base normalization step before any search comparison.

**bookQueryNormalizer.ts** — `normalizeBookQuery(text)`: applies Hebrew-specific transformations for book catalog search. Each entry in `TITLE_VARIANTS` is an independent rule: שו"ע / שוע expand to שלחן ערוך; שולחן normalizes to שלחן as a standalone word (not tied to שלחן ערוך). Must be applied symmetrically to both indexed titles (in `booksCategoryTree.ts`) and user queries (in `useBooksFsSearch.ts`). Add new book-search normalization rules here — never in `normalizeText.ts`.

**tocSearchUtils.ts** — TOC-specific search used by the books-fs two-tier search. Use `splitQuery` to split a multi-word query into a book part and a TOC part, `buildTocSearchPaths` to build normalized paths from flat TOC rows (also strips nikud for חסר spelling tolerance), `matchWords` for ordered subsequence matching, and `stripTocTitleRoots` to remove root TOC entries that merely repeat the book title (pass `singleRootOnly: true` for strict single-root mode; omit for multi-book batch stripping). Nodes with a `level` field have it decremented automatically when their parent root is removed.

**persistence.ts** — the only file in the app that touches IndexedDB and localStorage. All IDB reads and writes go through here. Do not call any IDB API or `localStorage` directly from anywhere else. Stores import from here; components and composables do not.

**commentaryNav.ts** — next/previous section navigation for the commentary panel, TOC-aware.

**hebrewTextProcessing.ts** — diacritics handling and text normalization for Hebrew display.

**censorDivineNames.ts** — replaces ה with ק in divine names when the censoring setting is enabled.

**scrollToIndexWithRetry.ts** — scroll-to-index for `@tanstack/vue-virtual` that retries until the target item has rendered. Use this instead of calling `scrollToIndex` directly when the list may not have rendered the target yet.

**detectFonts.ts** — `detectAvailableFonts()` uses canvas measurement to detect which Hebrew and general fonts are installed on the user's system. Returns an array of font family name strings. Used by `FontSelector.vue` to populate the font picker with only fonts that are actually available.

**resetState.ts** — exports a single `resetting` ref set to `true` just before an app reset. Check this before any interaction that should be blocked during reset.

**hebrewLearning.ts** — `getDailyLearning(hd)` returns today's schedule for all daily learning cycles: Daf Yomi, Mishna Yomi, Nach Yomi, Yerushalmi, Rambam (1 and 3 chapters), Kitzur Shulchan Aruch, Chofetz Chaim, Psalms, Perek Yomi, Arukh HaShulchan, and Dirshu Amud Yomi. Used by both the home page date bar and the calendar weekly view. All Hebrew learning formatting lives here.

**useOnlineStatus.ts** — thin wrapper around VueUse `useOnline`. Returns a reactive boolean for network connectivity. Import this instead of calling `useOnline` directly so the dependency is in one place.
