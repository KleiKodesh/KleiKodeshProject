# src/utils

Pure utility functions. No Vue, no Pinia, no reactivity. If a utility needs a ref or a store, it belongs elsewhere.

**normalizeText.ts** — `normalize(s)`: lowercases and strips Hebrew/ASCII quote characters. Import this as the base normalization step before any search comparison.

**segmentSearchTree.ts** — generic segment-aware search tree used by any hierarchical node list. `SegmentSearchTree` indexes nodes by their full ancestor chain (each ancestor's text is one segment) and matches query words as an ordered subsequence across segments. Three-pass algorithm: score all nodes (prefix on last word, exact-last-word preferred), bond detection (consecutive words that landed in the same segment in the best result must stay together), ancestry deduplication (matched parent suppresses its descendants). `displayPaths` map gives the "root / parent / node" string for rendering. Used by `TreeView.vue`, `CommentaryTreePanel.vue`, `bookCatalogSearchTocHeuristics.ts`, and `dafYomiNavigation.ts`.

**persistence.ts** — the only file in the app that touches IndexedDB and localStorage. All IDB reads and writes go through here. Do not call any IDB API or `localStorage` directly from anywhere else. Stores import from here; components and composables do not.

**hebrewTextProcessing.ts** — diacritics handling and text normalization for Hebrew display.

**hebrewTextCleaning.ts** — text cleaning utilities for Hebrew: strips leading non-Hebrew characters, normalizes whitespace, and removes diacritics from HTML. Used before any text is stored or compared.

**hebrewKetivExpander.ts** — generates plausible כתיב מלא spelling variants of a query by stripping ו/י and reinserting them at every consonant-boundary gap. Used as the first fallback when a dictionary lookup returns no results.

**censorDivineNames.ts** — censors divine names according to the `divineNameMode` setting. The tetragrammaton rendering is mode-selected (`yudDaled` ידוד, `yudKuf` יקוק, `doubleYud` יי, `heApostrophe` ה' — nikkud dropped, cantillation kept — or `none`); the other names have two independent settings of their own: `elokimMode` for the אלהים family (`hyphen` א‑להים / `kuf` אלקים / `daled` אלדים — substitution keeps the points and te'amim in place) and `otherNamesMode` for the names with no ה to swap, אדני/אל/שדי (`hyphen` / `none`). יה always hyphenates. A `mode` of `none` is the master off switch. The separator is U+2011 non-breaking hyphen so a censored name can never wrap across two lines. Exports `*_MODE_OPTIONS` lists for the settings UI and `normalize*` helpers for reading persisted values (including migrating the legacy boolean).

**scrollToIndexWithRetry.ts** — scroll-to-index for `@tanstack/vue-virtual` that retries until the target item has rendered. Use this instead of calling `scrollToIndex` directly when the list may not have rendered the target yet.

**detectFonts.ts** — `detectAvailableFonts()` uses canvas measurement to detect which Hebrew and general fonts are installed on the user's system. Returns an array of font family name strings. Used by `FontSelector.vue` to populate the font picker with only fonts that are actually available.
