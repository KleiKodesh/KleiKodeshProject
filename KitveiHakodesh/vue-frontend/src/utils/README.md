# src/utils

Pure utility functions. No Vue, no Pinia, no reactivity. If a utility needs a ref or a store, it belongs elsewhere.

**normalizeText.ts** — `normalize(s)`: lowercases and strips Hebrew/ASCII quote characters. Import this as the base normalization step before any search comparison.

**segmentSearchTree.ts** — generic segment-aware search tree used by any hierarchical node list. `SegmentSearchTree` indexes nodes by their full ancestor chain (each ancestor's text is one segment) and matches query words as an ordered subsequence across segments. Three-pass algorithm: score all nodes (prefix on last word, exact-last-word preferred), bond detection (consecutive words that landed in the same segment in the best result must stay together), ancestry deduplication (matched parent suppresses its descendants). `displayPaths` map gives the "root / parent / node" string for rendering. Used by `TreeView.vue`, `CommentaryTreePanel.vue`, `bookCatalogSearchTocHeuristics.ts`, and `dafYomiNavigation.ts`.

**persistence.ts** — the storage driver: a promise wrapper over IndexedDB plus a namespaced, JSON-coded, non-throwing wrapper over localStorage. It has **zero imports**, and that is the invariant worth protecting — the moment it needs one, it has started knowing something about the app.

It holds no schemas, no retention policies, no key names and no reset workflow. If a change here can only be explained by naming a feature, it belongs in the module that owns the value, not in this file. Its docblock lists where each such concern went.

Do not call an IDB API or `localStorage` directly from anywhere else — go through this file. One exception, by design: each IDB database here is a flat key→blob bucket (single `data` store, out-of-line keys, no indexes), so a caller needing in-line keys, secondary indexes or multiple object stores must hold its own handle. `hebrewBooksHistoryStore` legitimately does; see `src/stores/README.md`.

Key names are **not** centralised. Every localStorage key is defined by its owning module and namespaced `area.name` (`text.fontSize`, `search.expandKetiv`), so the one flat namespace this driver writes into cannot be claimed twice. Never add a bare, un-namespaced key.

**hebrewTextProcessing.ts** — diacritics handling and text normalization for Hebrew display.

**hebrewTextCleaning.ts** — text cleaning utilities for Hebrew: strips leading non-Hebrew characters, normalizes whitespace, and removes diacritics from HTML. Used before any text is stored or compared.

**hebrewKetivExpander.ts** — generates plausible כתיב מלא spelling variants of a query by stripping ו/י and reinserting them at every consonant-boundary gap. Used as the first fallback when a dictionary lookup returns no results.

**censorDivineNames.ts** — censors divine names according to the `divineNameMode` setting. The tetragrammaton rendering is mode-selected (`yudDaled` ידוד, `yudKuf` יקוק, `doubleYud` יי, `heApostrophe` ה' — nikkud dropped, cantillation kept — `hyphen` י‑ה‑ו‑ה, or `none`); the other names have two independent settings of their own: `elokimMode` for the אלהים family (`hyphen` א‑להים / `kuf` אלקים / `daled` אלדים / `none` — substitution keeps the points and te'amim in place) and `otherNamesSelected` for the names with no letter to swap — אדני/אל/שדי/יה — a list of `OtherNameKey` (`'adnai' | 'el' | 'shadai' | 'yah'`) so each name is independently selected for hyphen-censoring or left uncensored. A `mode` of `none` is the master off switch. The separator is U+2011 non-breaking hyphen so a censored name can never wrap across two lines. Exports `*_MODE_OPTIONS`/`OTHER_NAME_OPTIONS` lists for the settings UI and `normalize*` helpers for reading persisted values (including migrating the legacy boolean and the legacy `hyphen`/`none` mode string).

**scrollToIndexWithRetry.ts** — scroll-to-index for `@tanstack/vue-virtual` that retries until the target item has rendered. Use this instead of calling `scrollToIndex` directly when the list may not have rendered the target yet.

Font detection is **not** here — it asks the C# host or the service which fonts the OS has, which is host I/O, so it lives in `src/webview-host/fontsApi.ts`.
