# Architecture Map

This is a functional architecture map of the app — a Hebrew book reader, mobile-first, strictly RTL.

Every folder described here has a `README.md` that goes deeper: which file to edit for a given task, what to import from where, and what constraints apply. When working in any folder, read its README first.

## Feature-First Organization

**This is a feature-based app, and the code must be built that way.** A feature owns everything that exists only for it — its components, composables, plain modules, types, utils, caches, and tests all live in that feature's folder under `src/features/`. The default home for new code is the feature folder, not a shared folder.

`src/utils/` is only for utilities that are **not feature-specific** — genuinely shared code with more than one consuming feature. A utility used by exactly one feature belongs in that feature's folder, even if it is pure, generic-looking, and has no Vue in it. The test: if deleting a feature would leave a dead file in `src/utils/`, that file was in the wrong place.

The reverse is equally wrong: never park shared infrastructure inside a feature folder. Anything imported by more than one feature must move out to `src/utils/`, `src/composables/`, `src/stores/`, or `src/webview-host/` depending on its role. Reaching into `../another-feature/` for a helper is the signal that it is time to move the helper, not the signal to add the import.

## Layering & Dependency Direction

**A module must never know its consumers.** Dependencies point one way, lowest layer first:

```
utils/  ->  webview-host/  ->  stores/  ->  composables/  ->  components/, layout/  ->  features/
```

Never do any of these:

- `utils/` importing a store, a composable, or anything from `features/`
- a shared component in `components/` importing from `features/`
- a data-layer module in `webview-host/` importing a type from `features/`
- a child folder importing its parent's implementation (a subfolder importing shared *types* from its feature root — `../bookViewTypes` — is fine; that is siblings sharing a contract)
- two stores importing each other

**A shared type belongs to the lower layer.** If a type describes a database row or a wire payload, the data layer defines it and features consume it, never the reverse.

Every seforim row type lives in `webview-host/queries.types.ts`, beside the SQL that produces it — the same rule `queries.sql.ts` applies to the SQL strings. That file has **no imports** by design: a row shape must not reference a component prop type, a Vue type, or anything in `features/`. Types that build *on* a row are view models and stay with their feature (`CategoryNode` adds `children`/`books`/`subtreeBookIds`, so it belongs to book-catalog). `features/dictionary/dictionaryTypes.ts` shows the same shape for the dictionary DB.

One trap worth knowing: `TocEntry`'s first five fields are identical to `TreeNodeItem`, `TreeView`'s prop contract in `components/`. There is deliberately **no `extends`** between them — TypeScript is structural, so the row satisfies the prop with no import in either direction, whereas declaring the relationship would make the data layer depend on a component. Reach for structural compatibility instead of inheritance whenever a type would otherwise have to point upward.

**Prefer an invariant a command can check over a rule that needs an argument.** "Don't put app knowledge in `utils/`" has to be re-argued at every review, and lost repeatedly. "`persistence.ts` has zero imports" cannot be quietly eroded:

| Invariant | Check | Status |
| --- | --- | --- |
| the storage driver knows nothing about the app | `grep -c '^import' src/utils/persistence.ts` -> `0` | holds |
| no upward imports out of utils | `grep -rn "@/stores\|@/features\|@/composables" src/utils/` -> empty | holds |
| no feature imports in shared components | `grep -rn "@/features" src/components/` -> empty | holds |

All three hold as of 2026-07-28. The last one was fixed by pointing `components/TreeView.vue` at `utils/segmentSearchTree` directly instead of at `features/book-view/toc/tocSearchUtils`, which merely re-exports that same class under the alias `SearchableTree`. Note the shape of that bug: nothing needed to move, because the generic code was already in the right place — the component was reaching it through a feature-folder alias. Check for that before relocating anything.

**Still open (weaker, same family):** `features/book-catalog/bookCatalogSearchTocHeuristics.ts` imports `stripTocTitleRoots` from `../book-view/toc/tocSearchUtils` — a feature reaching into another feature's subfolder. Per the Feature-First rule above, a helper used by two features must move out. It can be shrunk first: that file also imports `SearchableTree`, which it should take from `utils/segmentSearchTree` like `TreeView` now does, leaving exactly one shared function to place.

**Name a file after its contract, not its category.** A category name invites everything adjacent to accumulate: `persistence.ts` reached six jobs and 452 lines before anyone noticed, and the reset workflow inside it forced `utils/` to import two Pinia stores. "Storage driver" is a contract; "persistence" is a topic. The same applies to identifiers — do not name a function `idb*` if it also writes localStorage, and do not prefix a constant `SETTINGS_` if it is not a setting.

**When refactoring a file that has drifted:** name its single job before deciding where anything goes ("where should this live?" is unanswerable while a file has several jobs), park everything undecided in one staging file with a manifest of candidate homes, land the direction fix separately from the ownership decisions, then delete the staging file. Treat an existing workaround comment as evidence of where the design is broken — but verify the claim, since the one reading "imported lazily to avoid a circular dependency" turned out to be guarding a type-only cycle that could not occur at runtime.

## App Shell

The shell is two levels: a split-view container and one or two pane shells inside it.

`App.vue` is the split-view container. It owns nothing about page rendering — its job is to decide how many panes exist and how wide they are:

- Renders `AppShell.vue` for pane 1 always, and a second `AppShell.vue` for pane 2 only when `bookViewStore.splitViewEnabled` is true
- Switches from flex column to CSS grid when split is active, with the divider width driven by `bookViewStore.splitViewFraction`
- Owns the horizontal resize divider (pointer events, 0.15–0.85 fraction clamp). In RTL the fraction controls pane 2, which is the physical LEFT pane, so dragging right grows it
- Split view requires `appWidth >= 768` (`SPLIT_VIEW_MIN_WIDTH`) and is unavailable in the VSTO environment; it auto-disables when the window shrinks below that
- Also renders the app-wide overlays: `ClockWidget`, `SetupWizard` (async, only when `!setupDone`), `GlobalContextMenu`, `ToastBanner`, and the reset overlay

`AppShell.vue` is one pane. It takes a `paneId` prop (1 or 2) and is a **row** of:

- `AppNavSidebar.vue` — icon rail owning the pane's edge for its full height, only while `settingsStore.navSidebarVisible`
- `.app-shell-main` — the column beside it, which is where everything document-scoped begins:
  - `AppTitleBar.vue` — fixed 40px header, spans `.app-shell-main` and never the rail, receives `paneId`
  - `AppPageView.vue` — fills the remaining height and width, renders the active page, receives `paneId`

Each pane is an Edge-style inset content panel: `--content-inset`, `--content-border-width`, and `--content-border-radius` are set by `settingsStore.applyCSSVariables`, so turning the "content border" setting off zeroes them and the content sits flush. The chrome surface (`--bg-secondary`) flows continuously from the title bar around the panel — there is no separator line between them.

`.app-shell-content` declares `container: app-shell / inline-size`. Page content must size its padding against this container query, not the viewport, so a narrow pane lays out correctly in split view.

### Pane context — how children know which pane they are in

`AppShell` provides two things to its entire subtree. Components must use these rather than reaching for `tabStore` directly:

- `provide('paneId', paneId)` — inject this to know which pane the component lives in
- `provide(PANE_NAVIGATION_KEY, ...)` — pane-scoped tab operations (`updateActiveTab`, `openTab`, `openOrUpdateActiveTab`, `navigateToSingleton`, `switchTab`, plus `activeTabId` / `activeTab` / `tabs` getters), backed by `useAppShellPane(paneId)`

Any navigation performed from inside a pane must go through the injected `PANE_NAVIGATION_KEY` API, never through `useTabStore()` directly — calling the store directly always targets pane 1 and will navigate the wrong pane in split view.

Pane 2 invariants are enforced synchronously at setup time, before first render: `AppShell` calls `ensurePane2HasTab()` when `paneId === 2`, and `App.vue` calls `reclaimPane1ActiveForSplit()` + `ensurePane2HasTab()` both on setup and whenever split view is re-enabled. This prevents the same tab rendering in both panes.

`bookViewStore.setFocusedPane` is called from a capturing `pointerdown` on the pane root, so the last-touched pane is the focused one.

### AppPageView routing

`AppPageView` maps the active tab's route to a page component via a `<component :is>` map, reading `tabStore.pane2ActiveTabId` when `paneId === 2` and `tabStore.activeTabId` otherwise.

Keying (the `pageKey` computed) decides what remounts on tab switch:

- `/book-view` — keyed by `` `${activeTabId}:${bookId}` `` so it remounts on both tab and book change
- `/search` and `/txt-view` — keyed by `activeTabId`
- All other routes share a single instance

## Setup Wizard

`SetupWizard.vue` is a full-screen onboarding overlay rendered in `App.vue` when `settingsStore.setupDone` is `false`. It covers the entire app until the user completes or skips it.

The step list is computed, so it varies by environment. Each step past `welcome` is its own `SetupWizardStep*.vue` component, mapped via a `stepComponents` record:

1. `welcome` — always shown; app logo, intro text
2. `db` — shown only when a database still needs to be chosen (in dev, when the service reports none); lets the user download Zayit/Otzaria or pick an existing `.db` file
3. `theme` — theme picker + app zoom slider
4. `general` — divine name censoring, resume-last-read, new-tab destination
5. `book-display` — header/text fonts, font size, line padding; optional separate commentary settings
6. `shortcuts` — keyboard shortcuts introduction

Navigation: forward/back buttons with a slide transition; a "skip" button calls `settings.completeSetup()` immediately. Progress is shown as a top-edge accent bar.

Completion: `settings.completeSetup()` sets `setupDone = true` and persists it to localStorage at key `app.setupDone` (via `KEYS.SETTINGS_SETUP_DONE` in `settingsStore.ts`). Once set, the wizard never shows again. `clearPersistedSettings` preserves this key explicitly, so a settings reset never re-shows the wizard.

## Title Bar

`AppTitleBar.vue` is the persistent chrome of a single pane — it takes a `paneId` and there is one instance per pane in split view. It is not app-global.

- Physical left side: hamburger nav menu (`AppTitleBarNavDropdown`, not rendered while `settingsStore.navSidebarVisible` — and `Ctrl+M` is inert then too, because `AppNavSidebar` is that same menu already on screen), split-view toggle (also handed over to the rail while it is up), theme toggle, toolbar toggle (book view only), PDF filter toggle (PDF tabs only)
- Center: `AddressBar.vue` when in search mode, otherwise the active tab title + `AppTitleBarTocBreadcrumb`
- Physical right side: home button, new tab button, close tab button
- The address bar's dropdown doubles as the tab list — an empty field shows all tabs. There is no separate tab-dropdown component.
- Visibility is managed by `useUiChromeVisibility`; `Ctrl+H` hides/shows the bar

### Keyboard shortcuts

All shortcuts live in `layout/useAppTitleBarShortcuts.ts`, not in the component — that composable also forwards `Ctrl`+key events out of child iframes back into the top-level pipeline. All matching uses `e.code`, never `e.key`. `ShortcutsReferenceList.vue` is the user-facing reference — keep it in sync when changing any binding.

Pane-scoped — these fire only when `isThisPaneFocused` (i.e. always when split view is off):

| Shortcut | Action |
| --- | --- |
| `Ctrl+W` / `Ctrl+X` | close tab / close all tabs |
| `Ctrl+Tab` / `Ctrl+Shift+Tab` | next / previous tab (guarded on `e.repeat` — one hop per physical press, because each hop cold-remounts a page) |
| `Ctrl+B` | toggle book-view toolbar, or the PDF viewer title bar on `/pdf-view` |
| `Ctrl+J` / `Ctrl+K` | toggle bottom (commentary) panel / TOC panel — book view only |
| `Ctrl+F` | open search in book view or txt view; suppressed when focus is inside `[data-ctrlf-enabled]` |
| `Ctrl+T` | toggle the address bar |
| `Ctrl+N` / `Ctrl+G` | new tab / go home |
| `Ctrl+H` | toggle title bar visibility |
| `Ctrl+L` / `Ctrl+M` | toggle dark mode / open nav dropdown |
| `F1` | settings in a new tab |
| `Alt+ArrowRight` / `Alt+ArrowLeft` | back / forward through the active tab's own history (RTL: back is the RIGHT arrow, matching the title-bar button icons; `Ctrl`+arrows belong to the book view's section navigation) |
| `Ctrl+1`…`Ctrl+9` | open a nav destination in a new tab (ספרים, חיפוש, היברו-בוקס, פתח קובץ, חיפוש קבצים, מילון, לוח שנה, מידות ושיעורים, סביבות עבודה) |

App-wide — handled by pane 1 only, so they do not fire twice in split view:

| Shortcut | Action |
| --- | --- |
| `Ctrl+\` | toggle split view (only when `isSplitViewAvailable`) |
| `Ctrl+Shift+F` / `F11` | toggle fullscreen |

When adding a shortcut, decide deliberately whether it is pane-scoped or app-wide. An app-wide shortcut placed in the pane-scoped block fires once per open pane.

## Tab System

Tabs are managed entirely by `tabStore`. Each tab is a `Tab` object with a route, title, and optional route-specific state (bookId, PDF info, search query, TOC path, etc.).

Navigation rules:

- `tabStore.updateActiveTab({ route, title, ...data })` — navigate in-place (most common)
- `tabStore.openTab(...)` — explicitly open a new tab (e.g. "new tab" button)
- `tabStore.navigateToSingleton(route)` — for singleton routes; switches to existing tab if one exists, otherwise replaces current tab

Singleton routes enforce one-tab-per-route and are never persisted across sessions. The authoritative list is `SINGLETON_ROUTES` in `tabStore.ts` (with Hebrew titles in `SINGLETON_TITLES` beside it): `/settings`, `/books`, `/hebrewbooks`, `/workspaces`, `/hebrew-calendar`, `/dictionary`, `/midot`, `/file-search`.

Multi-instance routes (`/book-view`, `/search`, `/pdf-view`, `/html-view`, `/txt-view`) can have multiple tabs open simultaneously.

## Pages & Routes

| Route              | Component                  | Kind                                     |
| ------------------ | -------------------------- | ---------------------------------------- |
| `/`                | `HomePage.vue`             | shared instance                          |
| `/books`           | `BookCatalogPage.vue`      | singleton                                |
| `/book-view`       | `BookViewPage.vue`         | multi-instance (keyed by tabId + bookId) |
| `/search`          | `FullTextSearchPage.vue`   | multi-instance (keyed by tabId)          |
| `/file-search`     | `LocalFileSearchPage.vue`  | singleton                                |
| `/settings`        | `SettingsPage.vue`         | singleton                                |
| `/hebrewbooks`     | `HebrewBooksPage.vue`      | singleton                                |
| `/pdf-view`        | `PdfViewPage.vue`          | multi-instance                           |
| `/html-view`       | `HtmlViewPage.vue`         | multi-instance                           |
| `/txt-view`        | `TxtViewPage.vue`          | multi-instance (keyed by tabId)          |
| `/workspaces`      | `WorkspaceManagerPage.vue` | singleton                                |
| `/hebrew-calendar` | `HebrewCalendarPage.vue`   | singleton                                |
| `/dictionary`      | `DictionaryPage.vue`       | singleton                                |
| `/midot`           | `HalachicUnitsPage.vue`    | singleton                                |

The PDF route is `/pdf-view`, not `/pdf-viewer` — the feature *folder* is `pdf-viewer/` but the route string is `/pdf-view`. Do not conflate them.

## Feature Folders (`src/features/`)

### home/

Home page navigation tiles. The tile list in `HomePage.vue` and the menu list in `layout/appNavItems.ts` (rendered by both `AppTitleBarNavDropdown.vue` and `AppNavSidebar.vue`) are the two entry points to the same set of destinations — they must always be kept in sync. When adding, removing, or renaming a navigation destination, update both files. The home page uses `navigate()` (navigates in the active tab); the nav dropdown uses `navigateInNewTab()` (always opens a new tab). Neither list is derived from the other — they are maintained in parallel.

- `HomePage.vue` — page shell: layout, search input markup, tile keyboard traversal, cold-start focus. No search/tile/date logic.
- `HomePageTile.vue` — one tile; emits `tap` (with the Ctrl/⌘ flag), `togglePin`, `remove`
- `useHomeTiles.ts` — static tile list, recently-opened list + icon map, how many recent tiles fit, pin/remove actions
- `useHomeSearchBar.ts` — dropdown open state, fixed-position anchor (computed once on open, never reactive), animated placeholder, input keyboard handling
- `useHomeSearchNavigation.ts` — all four `onSelect*` handlers plus `openRecentEntry` / `openFullTextSearch`; the only file here that touches `tabStore` / `localFileStore` / the bridge
- `HomePageDateBar.vue` — the bottom bar (clock, zman button + popup, Hebrew date, daf yomi), self-contained
- `useHomeDateBarFit.ts` — keeps the date bar to one line by dropping items by priority instead of wrapping
- `homeDateInfo.ts`, `dafYomiNavigation.ts` — no `use` prefix: these are plain modules with lazy-loaded shared state, not reactive composables (see the composable naming rule in `naming.md`)
- `useNextZman.ts` + `NextZmanPopup.vue` — next-zman countdown
- Recently opened entries are rendered inline in `HomePage.vue`'s tile grid (after the static tiles), loaded async on mount by `useHomeTiles`. No separate component.
- `useHomeSearch.ts` — unified quick-search composable; fans the query out across book catalog (instant, title-only), HebrewBooks catalog (debounced, hosted-only), and Document Locator file search (debounced, hosted-only). Each source writes to its own result ref.
- `HomeSearchDropdown.vue` — results dropdown for the hero search bar; grouped by source (ספרים / היברו-בוקס / קבצים) with per-section loading spinners.

### book-catalog/

Book catalog browser. Supports list, tiles, and full tree views with search.

- `BookCatalogPage.vue` — main page; owns view switching via `<component :is>` map
- `BookCatalogView.List.vue`, `BookCatalogView.Tiles.vue`, `BookCatalogView.Tree.vue` — the three view modes
- `BookCatalogSearch.vue` — search results
- `BookCatalogBreadcrumb.vue`, `BookCatalogBreadcrumbChevronDropdown.vue` — navigation breadcrumb
- `BookCatalogTitleBar.vue` — catalog-specific title bar row
- `useBookCatalog.ts` — navigation and folder traversal
- `useBookCatalogSearch.ts` — two-phase search orchestration
- `bookCatalogTree.ts` — tree building and category metadata
- Search stack: `bookCatalogSearch.ts`, `bookCatalogSearchNormalizer.ts`, `bookCatalogSearchMatcher.ts`, `bookCatalogSearchTocHeuristics.ts`, `bookCatalogTocKeywords.ts`, `bookCatalogTocSearchCache.ts`
- `SEARCH.md` — authoritative design doc for the search stack; read before changing search behaviour

### book-view/

The main book reader. Orchestrates a split pane (text above, commentary below), a TOC side panel, a floating search bar, and a toolbar. Organized into three subfolders: `lines/`, `toc/`, and `commentary/`.

`SCROLL_AND_COMMENTARY_POSITIONING.md` in this folder documents the scroll/positioning contract — read it before touching any scroll code here.

**Main components:**
- `BookViewPage.vue` — orchestrator (see the note in `preferences.md`: it is over the length limit and pending refactor into a true shell)
- `BookViewToolbar.vue` — zoom, search, TOC, bottom panel toggles
- `BookViewSidePanel.vue` — side panel container for TOC
- `BookViewSearchBar.vue` — floating search (query, mode, match navigation)
- `BookViewRelatedBooksDropdown.vue` — dropdown for related books
- `bookViewTypes.ts` — shared types: `SearchMode`, `SidePanelMode`

**Main composables:**
- `useBookView.ts` — central composable; owns data loading, state, event handlers, and watchers
- `useBookViewSearch.ts` / `useBookViewSearchPanel.ts` — content search (line-based) and its panel state
- `useBookViewScrollSync.ts` — syncs active TOC entry and auto-selects commentary on scroll
- `useBookViewSessionRestore.ts` — restores per-book view state on mount
- `useBookViewPinnedCommentary.ts` — tracks the pinned commentary book with default-commentator fallback
- `useBookViewCommentaryPanel.ts` / `useBookViewCommentaryAnnotations.ts` — bottom panel state; commentary annotations
- `useBookViewLineSelection.ts` — line selection state
- `useBookViewSidePanel.ts` / `useBookViewTocNavigation.ts` — side panel state; TOC navigation
- `useBookViewKeyboardShortcuts.ts` — book-view-scoped shortcuts
- `copyFlagExclusivity.ts` — mutual exclusivity rules for copy options (has unit tests)

**lines/ subfolder** — main text rendering with virtual scroller and line selection:
- `BookViewLinesContent.vue` — main text (virtual scroller, line selection)
- `useBookViewLinesTable.ts` — paginated line fetching in chunks of 200; pre-allocates placeholder slots so the virtualizer has the correct total height immediately, then fills content as chunks arrive; exposes `prioritise(lineIndex)` to move a chunk to the front of the fetch queue
- `useBookViewLineRenderer.ts` — line rendering logic
- `useBookViewLineCopyMenu.ts` — context menu for line copying
- `useBookViewLineLink.ts` — "copy link to this section" menu action; copies the deep link built by `buildLineLink` in `utils/appDeepLink.ts` — the single definition of this app's link format, `kitveihakodeshapp://book/<bookId>?index=<lineIndex>`, shaped to match the `otzaria://` links `HostLink.cs` parses (the app opens such a link when it is launched with one — `Program.GetOpenRequestArgument` / `MainForm.OpenRequest` route it through the same single-instance pipe as a file path, so it lands as a new tab; registering the scheme with Windows is an installer step, specified in `Build/Installer/README.md`)
- `useBookViewLinesScroll.ts` / `useBookViewLinesNavigation.ts` — scroll and navigation within the lines view
- `useBookViewHighlights.ts`, `useBookViewNotes.ts`, `useBookViewAnnotations.ts` — user annotations; `bookViewAnnotationColors.ts`, `BookViewAnnotationMenuRow.vue`, `BookViewNoteBubble.vue`
- `useBookViewAbbrevTooltip.ts` + `BookViewAbbrevTooltip.vue` — abbreviation expansion tooltip
- `wordLinkAnchors.ts`, `useWordLinkAnchors.ts`, `useWordLinkTooltip.ts`, `WordLinkTooltip.vue` — per-word link anchors (with tests and fixtures). Requires a schema-v2 database; dormant without one.

**toc/ subfolder** — table of contents side panel:
- `BookViewTocTree.vue` — TOC side panel (main + alt structures)
- `BookViewTocTreeSection.vue` — TOC section header
- `useBookViewToc.ts` — loads TOC entries and alt TOC structures; builds a path map (entry id → full breadcrumb string); exposes `getActiveTocEntry` and `getTocPath`
- `useBookViewTocScrollTracking.ts` — tracks programmatic TOC scrolls to suppress active-entry updates during animation
- `tocSearchUtils.ts` — TOC search utilities

**commentary/ subfolder** — commentary display and navigation:
- `CommentaryView.vue` — commentary display grouped by book
- `CommentaryHeader.vue` / `CommentaryHeaderNav.vue` — book header (type selector) and prev/next section nav
- `CommentaryTreePanel.vue` / `CommentaryTreeSectionNode.vue` — tree panel for commentary filtering
- `commentaryTreeTypes.ts` — types for commentary tree
- `useCommentary.ts` — reactive shell; owns Vue state (`groups`, `staticFilterGroups`, `loading`) and watchers; delegates fetching to `commentaryGroupBuilder.ts`
- `commentaryConnectionTypes.ts` — connection type constants, DB→canonical mapping, Hebrew labels, lazy-loaded type ID table
- `commentaryGroupBuilder.ts` — all data fetching and group building, no Vue reactivity
- `commentaryNavigation.ts` — pure navigation helpers (no `use` prefix: no reactivity)
- `uncheckedCommentaryBooks.ts` — persisted set of hidden commentary books
- `useCommentarySearch.ts`, `useCommentaryNavigation.ts`, `useCommentaryRender.ts`, `useCommentaryScroll.ts`, `useCommentaryTocPaths.ts`, `useCommentaryTreeSearch.ts`, `useCommentaryCopy.ts`, `useCommentaryHighlights.ts`, `useCommentaryNotes.ts`
- `DEBUG_NOTES.md` — scroll/positioning debugging notes

### full-text-search/

Full-text search backed by FtsLib with a custom LSM-style segment index. Supports category/book filters and caches results in IDB.

- `FullTextSearchPage.vue` — main page
- `FullTextSearchBar.vue` — search input + filter toggle
- `FullTextSearchAdvancedPanel.vue` — advanced query options (wildcard/grammar wrap, distance, ordering, ketiv)
- `FullTextSearchResultsList.vue` — results (virtual scroller)
- `FullTextSearchResultPreview.vue` + `useFullTextSearchPreview.ts` — inline result preview
- `FullTextSearchFilterPanel.vue`, `FullTextSearchFilterNode.vue`, `FullTextSearchFilterBookList.vue` — category/book filter tree
- `fullTextSearchFilterExpansion.ts` — filter tree expansion state
- `FullTextSearchIndexingOverlay.vue` — indexing progress overlay; search is enabled as soon as the first segment is flushed (partial index)
- `useFullTextSearch.ts` — search execution and IDB caching; streams results from the host in batches; enriches each batch with TOC paths; resumes interrupted searches from the cache skip offset
- `useFullTextSearchIndexingStatus.ts` — subscribes to `ftsIndexProgress`; handles `ftsIndexInvalidated` (automatic rebuild when the DB changes or the index is corrupt)
- `useFullTextSearchFilters.ts` — filter state (checked books/categories), result filtering, result click handler
- `ftsChronology.ts` — chronological ordering/period metadata for results
- `scrollRestore.ts` — scroll position restore (has unit tests). Scroll saves fire only on `visibilitychange` / `beforeunload` / unmount — never on `scroll`; see the rule in `preferences.md`.
- `fullTextSearchTypes.ts` — TypeScript types

Results must never be capped or limited. The term-expansion cap inside FtsLib is a separate, sanctioned mechanism — do not confuse the two.

#### Query syntax (FtsLib)

- Multiple words are AND-ed by default. `word*` is a prefix wildcard, `*word` is a suffix wildcard, `*word*` is an infix wildcard.
- Fuzzy matching: `word~1` / `word~2` for edit distance 1–2 (Levenshtein distance).
- OR within a slot: `a | b` matches lines with either a or b.
- Grammar expansion: `%word` for prefix expansion (all grammatical prefixes prepended), `word%` for suffix expansion (all grammatical suffixes appended), `%word%` for both. Handled natively in FtsLib by `GrammarExpander` — candidates are verified against the index so only forms that actually exist are matched.
- Spelling variants (ketiv/chaseir): `~word` to match Hebrew spelling variants.
- Wrapping modes (frontend-side options):
  - `searchWildcardWrap` — auto-wrap each term with `*term*` for infix search
  - `searchGrammarWrap` — auto-wrap each term with `%term%` for grammar expansion
- Parameters passed to the search engine:
  - `searchMaxWordDistance` — maximum token distance between matched terms in a line (default 10)
  - `searchRequireOrdered` — whether terms must appear in query order
  - `searchExpandKetiv` — whether to expand ketiv/chaseir spelling variants

### settings/

App settings across three tabs: general, reading, and advanced. Also contains the setup wizard.

- `SettingsPage.vue` — page shell: layout (side nav + scroll body), sticky search bar, narrow-screen nav dropdown, and section scroll navigation; no business logic
- `SettingsPageSideNav.vue` — section side navigation
- `SettingsPageThemeAndApplicationSection.vue` — theme, dark mode, zoom, toolbar position, new-tab destination, chrome options
- `SettingsPageReadingAndBookDisplaySection.vue` — reading preferences, book display fonts/sizes, commentary overrides
- `SettingsPageCalendarSection.vue` — calendar city picker, clock toggle
- `SettingsPageAdvancedSection.vue` — database/index paths, excluded folders, diagnostics
- `SettingsPageResetSection.vue` — all reset actions
- `SettingsPageKeyboardShortcutsSection.vue` + `ShortcutsReferenceList.vue` — shortcuts reference; keep in sync with `layout/useAppTitleBarShortcuts.ts`
- `SettingsPagePathField.vue`, `SettingRow.vue`, `SliderSetting.vue`, `ToggleGroup.vue`
- `ThemePicker.vue`, `FontDisplaySettings.vue`, `FontSelector.vue`
- `useSettingsPage.ts`, `useSettingsSearch.ts` — page state and settings search
- `appResetState.ts` — the app-reset module: the `resetting` flag `App.vue` reads to show the reset overlay, plus `resetEverything()`, the full-reset sequence. Lives here rather than in a store because it spans every domain (all seven databases + localStorage). See the App reset section of `app.md`.
- `SetupWizard.vue` + `SetupWizardStep*.vue` — first-launch onboarding wizard

### hebrewbooks/

HebrewBooks catalog browser with download history. Note the folder is `hebrewbooks/` (one word, no hyphen) — an exception to the kebab-case folder convention, matching the site's own name.

- `HebrewBooksPage.vue`, `HebrewBooksListItem.vue`
- `useHebrewBooks.ts`, `hebrewBooksCatalog.ts`

### halachic-units/

Halachic unit converter. Singleton route `/midot`. Converts between biblical, Talmudic, and modern units across six systems (length, area, volume, weight, coins, time) with support for multiple halachic opinions.

- `HalachicUnitsPage.vue` — full converter UI with opinion selector and conversion explanation
- `halachicUnits.ts` — all conversion logic (`convert`, `toMetric`, `explainConversion`)
- `units/` — unit definitions per measurement system; `types.ts` for shared types

### pdf-viewer/

PDF viewer with OCR support. Embeds a PDF.js iframe and provides OCR text extraction.

Route is `/pdf-view`; folder is `pdf-viewer/`.

- `PdfViewPage.vue` — main PDF viewer page
- `PdfOcrResultPopup.vue` — OCR result display popup
- `usePdfOcrSelection.ts` — OCR selection and text extraction
- `pdfOcrInjectedScript.ts` — script injected into PDF.js iframe
- `usePdfContextMenu.ts` — right-click menu inside the viewer
- `usePdfViewPageTracking.ts` — tracks the current page for the breadcrumb and session restore
- `pdfViewerTypes.ts` — TypeScript types

## PDF.js Viewer

The PDF.js viewer files live at `vue-frontend/public/pdfjs/`. This is a static folder served as-is; it is not processed by Vite.

Key files:

- `web/viewer.html` — the iframe document loaded by `PdfViewPage.vue`
- `web/viewer.mjs` — the main PDF.js application bundle; all viewer behaviour patches go here
- `web/viewer.css` — PDF.js default styles
- `web/viewer-custom.css` — custom theme variable hooks and page filter support (added file, not in vanilla PDF.js)
- `web/pixel-ratio-override.js` — forces minimum `devicePixelRatio` of 1.5 for sharp rendering (added file)
- `build/pdf.worker.mjs` — the PDF.js Web Worker bundle; polyfills for old WebView2 go here
- `web/locale/he/viewer.ftl` — Hebrew translation overrides
- `CUSTOMIZATIONS.md` — the authoritative record of every patch applied to this PDF.js build; read this before touching any file in `public/pdfjs/`

When modifying PDF.js behaviour — adding a feature, fixing a bug, adjusting presentation mode — always edit the files in `vue-frontend/public/pdfjs/web/` or `vue-frontend/public/pdfjs/build/` directly. After making any patch, document it in `CUSTOMIZATIONS.md` following the same format as the existing entries.

### html-view/

HTML file viewer for local HTML documents.

- `HtmlViewPage.vue` — main HTML viewer page
- `useOtzariaAddinBridge.ts` — bridge to the Otzaria add-in

### txt-view/

Native Vue viewer for local `.txt` files. Renders content directly in a `<div>` — no iframe, no virtual host.

- `TxtViewPage.vue` — loads raw text via bridge (`readTxtFileContent` action in C#), parses custom line markup (`@#$` → h2, `!` → strip prefix), renders with RTL layout. Scroll position persisted to `TabState.htmlViewScrollTop`.
- `useTxtViewSearch.ts` — in-document search
- `useTxtViewCopyMenu.ts` — copy context menu

### local-file-search/

File system search over the DocumentLocator service. Singleton route `/file-search`.

- `LocalFileSearchPage.vue` — main page
- `LocalFileSearchResultsList.vue` — results list
- `useLocalFileSearch.ts` — query execution against the service

This folder has no `README.md` yet — add one when next working here.

### dictionary/

Hebrew dictionary lookup. Singleton route `/dictionary`.

- `DictionaryPage.vue` — main dictionary page
- `DictionaryWordPage.vue` — individual word page
- `dictionaryCache.ts` — LRU cache for dictionary lookups
- `dictionaryTypes.ts` — TypeScript types

### workspace/

Workspace CRUD UI.

- `WorkspaceManagerPage.vue`

### hebrew-calendar/

Hebrew calendar page. Monthly grid and weekly detail views with zmanim. Singleton route.

- `HebrewCalendarPage.vue` — orchestrator, view-mode toggle, city picker
- `MonthlyView.vue` — monthly grid with Hebrew dates, holidays, parasha
- `WeeklyView.vue` — week view with events, candle lighting, zmanim, daily learning
- `DayRow.vue`, `CalendarHeader.vue`, `calendarTypes.ts`
- `useMonthlyView.ts` — month navigation, keeps Gregorian and Hebrew month in sync
- `useWeeklyView.ts` — week data via `@hebcal/core`, daily learning via `hebrewCalendarLearning.ts`
- `useZmanim.ts` — city selection, geolocation, zmanim calculation

## App Shell (`src/layout/`)

- `AppShell.vue` — one pane: nav sidebar beside the title-bar-plus-page-view column, provides `paneId` and `PANE_NAVIGATION_KEY`
- `AppTitleBar.vue`, `AppPageView.vue`, `AppTitleBarNavDropdown.vue`
- `appNavItems.ts` — the one list of nav destinations (`APP_NAV_ITEMS`, `APP_NAV_SETTINGS_ITEM`), read by both the nav dropdown and the nav sidebar
- `AppNavSidebar.vue` — the nav menu as an always-on icon rail at the pane's edge, one per pane, owning the pane's edge for its full height with the title bar starting beside it, never under it — the rail is a sibling of the shell that holds the tab-header strip, not of the strip. It splits with the pane (a pane is a whole shell, not an editor group, so each gets one). Surface is the title bar's own `--bg-secondary`, inherited from `.app-shell` — it is frame, not a panel. No border on any side and no shadow (only floating panels cast one): the column of items is what marks the rail out, not an edge around it. 44px wide with 32×32 items (24px glyph plus a 4px inset — sized from the hover band, not the icon pitch), 6px gutters, 4px radius; destinations at the top and a bottom `.nav-group-end` group (split view, pop-out, settings, collapse — settings always the floor) separated by space rather than a rule. Icons only, label as the tooltip, and a `ChevronDoubleRight` button at the bottom that folds it back into the edge. Shown while `settingsStore.navSidebarVisible` (persisted as `app.navSidebar`), opened from the nav dropdown's `ChevronDoubleLeft` row — which is the only way in, since the hamburger is gone while it is up
- `AddressBar.vue` — Explorer-style address bar in the title bar; hosts the tab dropdown and reuses the home search. There is no `AppTitleBarTabDropdown.vue`.
- `AppTitleBarTocBreadcrumb.vue` — interactive breadcrumb rendered in the title bar center for `/book-view` and `/pdf-view` tabs. Each segment has a chevron before it listing siblings; the active segment gets a trailing chevron if it has children. Emits `navigateToTocEntry` and `navigateToPdfEntry`.
- `AppTitleBarBreadcrumbChevronDropdown.vue` — teleported chevron dropdown listing `BreadcrumbItem[]` entries. Used by `AppTitleBarTocBreadcrumb` for both TOC and PDF siblings. Scrolls to the active item on open.
- `AppTitleBarHistoryButton.vue` — one Back or Forward button (`direction` prop); click steps the active tab's history, press-and-hold opens a teleported dropdown of all frames in that direction for a direct jump (`useAppShellPane.goToHistoryIndex`).
- `useSplitViewAvailable.ts` — `SPLIT_VIEW_MIN_WIDTH` (768) plus the availability computed, shared by the two surfaces that offer the split toggle (`AppTitleBar`, `AppNavSidebar`); `App.vue` imports the threshold but measures the app element, since it owns the auto-disable
- `useAppTitleBarShortcuts.ts` — every keyboard shortcut the title bar owns, for one pane; splits them into pane-scoped and pane-1-only app-wide, and forwards iframe `Ctrl`+key events into the top-level pipeline
- `useAppTitleBarTocBreadcrumb.ts` — parses `tab.tocPath` into `BreadcrumbSegment[]` for both `/book-view` (splits on ` / `, reads `TocBridge`) and `/pdf-view` (splits on ` · `, reads `PdfBridge`). Each segment includes `siblings` and `children` for the chevron dropdowns.

`AppTitleBarNavDropdown` is the hamburger nav menu. Its destination list is `appNavItems.ts`, which `AppNavSidebar` renders too, and it mirrors the tiles in `HomePage.vue` — see the `home/` section for the sync rule.

## Shared Components (`src/components/`)

Reusable UI primitives used across multiple features. No feature-specific logic lives here.

- `TreeView.vue`, `TreeNode.vue` — generic tree
- `treeTypes.ts` — `TreeNodeItem` interface; import from here, never from `TreeNode.vue`
- `SplitPane.vue` — resizable split pane (the *bottom-panel* splitter inside book view; the left/right pane splitter is in `App.vue`)
- `TopSearchBar.vue` (every page search bar), `ContextMenu.vue`, `GlobalContextMenu.vue`, `ConfirmDialog.vue`, `AlertDialog.vue`, `LoadingAnimation.vue`, `ToastBanner.vue`, `ClockWidget.vue`
- `HintIcon.vue` — tooltip hint icon
- `common/FloatingSearchBar.vue`, `common/AutofillDropdown.vue` — shared search chrome
- RTL/custom icon wrappers: `IconTreeRtl.vue`, `IconBookRtl20.vue`, `IconBookRtl24.vue`, `IconEverythingSearch.vue`

Dialogs are `ConfirmDialog.vue` (confirmation) and `AlertDialog.vue` / `ToastBanner.vue` (messages) — see the "No Browser Dialogs" rule in `preferences.md`. `useToast.ts` is the programmatic entry point for transient messages.

## Pinia Stores (`src/stores/`)

**tabStore** — tab lifecycle and navigation. The central store — most features read from it. It re-exports the per-tab/per-book persistence API (see below), so callers keep reaching it through the store. It is **pane-aware**: alongside `tabs` / `activeTabId` it owns the pane-2 equivalents (`pane2ActiveTabId`, `openPane2Tab`, `updatePane2ActiveTab`) and the split invariant helpers (`ensurePane2HasTab`, `reclaimPane1ActiveForSplit`). Prefer the pane-scoped API from `useAppShellPane` / `PANE_NAVIGATION_KEY` over calling these directly.

**bookViewStore** — book viewer UI state and split-view state. Toolbar visibility, floating search bar position, per-tab+book zoom map, and a reactive `zoom` computed for the active tab+book. Panel toggles take a `paneId` (`toggleToolbar`, `toggleBottomPanel`, `toggleTocPanel`, `openSearch`). Also owns split view: `splitViewEnabled`, `splitViewFraction`, `setSplitViewFraction`, `toggleSplitView`, `disableSplitView`, and the focused-pane tracking (`setFocusedPane`). Also holds the per-tab `TocBridge` registration map (`registerTocBridge` / `unregisterTocBridge` / `getTocBridge`) used by the title bar breadcrumb for book-view TOC navigation, and the per-tab `PdfBridge` registration map (`registerPdfBridge` / `unregisterPdfBridge` / `getPdfBridge`) for PDF outline navigation. Both bridges are in-memory only, never persisted.

**settingsStore** — all app-wide settings (fonts, sizes, padding, zoom, diacritics, censoring, etc.). Each setting has its own localStorage key and is watched individually: add a namespaced key to the local `KEYS` object, a `DEFAULTS` entry and a ref, then a `loadSetting` call in `init()` and a `persistSetting` call, and export the ref. Assigning the ref is all a consumer needs to do — the watcher persists it.

This store also **owns** the disk names for those settings (there is no central key registry) and `clearPersistedSettings`, which implements the settings-vs-structural split: it wipes the `kitvei-hakodesh.` namespace except the `tabs:` and `workspaces.` prefixes and `app.setupDone`. That distinction is a product decision, which is why it lives here and not in the driver — the driver has no way to know that `tabs:*` is structure while `text.fontSize` is a preference.

Per-feature *display* preferences belong here too, not in the feature or in `tabStore` — `booksView` (book catalog layout) and `fileSearchSortOrder` are the precedents. A preference belongs in a feature folder only when it is genuinely per-tab.

**booksDataStore** — lazy-loaded book catalog. Fetches all categories and books on first access, builds the category tree, assigns period metadata.

**workspaceStore** — workspace management. All tab/book IDB keys are workspace-scoped; switching workspaces changes `activeId` and reloads tabs.

**localFileStore** — Local file and Word handling state. Manages conversion state, HebrewBooks download state, and session restore for PDF/HTML tabs. Listens to C# push events (`conversionStarted`, `hbPdfReady`, `hbPdfCancelled`).

**searchCacheStore** — LRU cache for FTS search results (capped at 100 entries), stored in `app-search-cache` IDB.

**hebrewBooksHistoryStore** — HebrewBooks download history, LRU-capped at 25 entries. This store owns the `app-hb-history` IDB database **entirely** — type, schema, open, read, write — rather than going through `persistence.ts`, which is why that database does not appear in the `handles` map there. It is the documented exception to the "all IDB access goes through persistence.ts" rule.

**recentlyOpenedStore** — recently opened documents, stored in `app-recently-opened` IDB, LRU-capped at 16 entries. Covers /book-view, /pdf-view, /html-view, and /txt-view. Loaded lazily on first access. Recording is triggered automatically by `tabStore.updateActiveTab`, `openTab`, and `updateTab` for all trackable routes.

Two plain modules live in `src/stores/` alongside the Pinia stores. They hold no reactive state, and both `tabStore` (for teardown) and feature composables (for read/write) use them, so they can live in neither a feature folder nor `utils`. Split on the database boundary, which is also the scoping boundary:

- **tabStatePersistence.ts** — the `app-tabs` slice: everything keyed by workspace + tab. `TabState` (search filters, scroll restore, per-tab zoom), `BookState` (reading position, commentary layout, per tab *and* book), the book-state cache, and `deleteAllStateForTab` — the single teardown call every close path uses.
- **bookLastRead.ts** — the `app-lastread` slice: the global per-book last-read position. Deliberately not tab-scoped and it outlives tab close, which is what separates it from `BookState` (same shape, one tab's view). Always write via `setLastReadPos` so the 1000-entry on-disk cap is enforced.

**hostSearchStore** — receives "navigate from the VSTO host" pushes and routes them to a page. The Word ribbon's context menu calls into the C# AppViewer, which pushes `hostSearch` (`target: 'fts' | 'catalog'` plus cleaned selection text) or `hostOpenBook` (an `otzaria://` / `kitveihakodeshapp://` / `zayit://` deep link). Seeds `tab.searchQuery` or `tab.catalogQuery`, which the target page reads on mount.

**pdfOcrStore** — PDF OCR state and results caching.

## Composables (`src/composables/`)

**useAppNavigation.ts** — central navigation handler. Routes singletons via `navigateToSingleton()`, handles file picker, external links, and search navigation.

**useVirtualScrollerKeys.ts** — keyboard nav for virtual scrollers (`Ctrl+Home`, `Ctrl+End`). Required on every component that uses `@tanstack/vue-virtual`. The scroll container must have `tabindex="0"`.

**useZoom.ts** — zoom handler for keyboard (`Ctrl+±/0`), wheel (`Ctrl+scroll`), and pinch (2-finger touch). Config: MIN=50, MAX=200, DEFAULT=100, STEP=10.

**useListKeyNav.ts** — arrow-key + Home/End navigation for plain DOM lists that own keyboard focus themselves (roving-focus model); tracks `focusedIndex` and scrolls the focused item into view. For standalone lists only — input-paired lists use `useInputListNavigation.ts`.

**useTextSelectionKeys.ts** — `Ctrl+A` (select all text in a container) and `Ctrl+F` (open search) scoped to a specific element.

**useTileGridKeys.ts** — 2D arrow-key navigation for tile grids; computes column count from container width to handle Up/Down correctly.

**useInputListNavigation.ts** — combobox keyboard model (W3C APG): DOM focus stays in a text input while its keydown events move a highlight through the paired list. Handles arrows, PageUp/PageDown, `Ctrl+Home`/`Ctrl+End`, Enter (Ctrl+Enter = new tab); plain Home/End and Left/Right stay caret keys. Supports plain containers (`scrollIntoView` over `[data-nav-item]`), virtual lists (`getVirtualizer` → `scrollToIndex`), and tile grids (`getColumnsPerRow`). Used by every input-paired list: home search dropdown, address bar, book catalog search, HebrewBooks, local file search, full-text-search filter panel.

**useLineCopy.ts** — intercepts the browser `copy` event on a scroller element; when the user has selected all, writes each line as a `<div>` in `text/html` and strips HTML tags for `text/plain`, so copied text has no inline line breaks.

**useDropdownClose.ts** — drop-in replacement for `onClickOutside` that also closes on window blur and handles the toggle-button race condition. Use on every dropdown instead of `onClickOutside` directly.

**useAppShellPane.ts** — pane-scoped tab operations for a given `paneId`. Backs the `PANE_NAVIGATION_KEY` injection provided by `AppShell.vue`.

**usePaneNavigation.ts** — defines `PANE_NAVIGATION_KEY` and the injection helper. Import the key from here.

**useCrossPaneTabActions.ts** — moving/duplicating tabs between panes.

**useOpenInNewTab.ts** — shared "open in new tab" behaviour (modifier-click, middle-click).

**useTabSwipeNavigation.ts** — touch swipe between tabs. One swipe = one tab, with a 2× ratio re-strike rule.

**useFloatingPanel.ts** — draggable/positioned floating panel behaviour.

**useUiChromeVisibility.ts** — all UI-chrome visibility in one file: per-pane title-bar visibility (session-only, Ctrl+H); the app-wide hidden-scrollbars setting (bars fully invisible except while scrolling, revealed per scrolled element only; persisted in settingsStore, applied via root classes and a capture scroll listener, CSS in main.css); `useIframeScrollbarsHidden`, which propagates that setting into an iframe (same-origin style injection plus scroll-activity listener, or postMessage to the C#-injected IframeScrollScript for cross-origin frames — used by the PDF viewer and html-view pages); and the F9 reading-mode check-all over title bars, toolbars, and hidden scrollbars. Rule: never add `::-webkit-scrollbar-*` rules anywhere in the app — the standard properties make them dead code and they would defeat the hidden-scrollbars tinting for their element.

**useSelectAllInContainer.ts** — `Ctrl+A` scoped to a container.

**useContextMenuLongPress.ts** — long-press to open a context menu on touch.

**useAutofill.ts** — autofill/history suggestions for search inputs; pairs with `common/AutofillDropdown.vue`.

**useToast.ts** — transient message queue rendered by `ToastBanner.vue`. Use this instead of any browser dialog.

## Host Layer (`src/webview-host/`)

### seforimDb.ts

The main seforim database access layer. Exports:

- `isHosted` — true when running inside C# WebView2 host (or in dev mode)
- `dbReady` — reactive ref, true once a DB path is available
- `query<T>(sql, params)` — executes SQL via `window.__webviewQuery` (C# host) or a `/query` POST to the Vite dev middleware
- `onWebviewEvent(fn)` — subscribe to C# push events

### dictionaryDb.ts

Dictionary database access layer. Separate from the main seforim DB.

- `queryDict<T>(sql, params)` — executes SQL against the dictionary database

### dictionarySeforimDb.ts

Seforim-specific dictionary queries.

### seforimApi.ts

Typed request layer above `seforimDb.ts` — prefer this over hand-writing `query()` calls for new data access.

### serviceClient.ts

Client for the native KitveiHakodesh service (MessagePack over a loopback HTTP endpoint). See the service documentation for the wire contract.

### userSettingsDb.ts / userSettingsDb.sql.ts

Settings that must be shared with the native host. Browser-only preferences stay in localStorage via `persistence.ts` — use this only when C# needs to read the value too.

### tabMirror.ts

Mirrors the Vue tab list to the host so native chrome (the Fluent tab strip) can render it. Order is not synced back.

### queries.sql.ts

All raw SQL strings for the seforim database live here. No inline SQL anywhere else in the Vue/TypeScript codebase — every query a composable or store needs must be added to this file and imported from it.

### dictionaryDb.sql.ts

All raw SQL strings for the dictionary database.

### bridge.ts

Host actions. There is no `devFallbacks.ts` — dev behaviour is handled inline or by the native service.

Environment flags: `isVstoEnvironment` / `showPopOutButton` (running inside the Word add-in — split view is disabled here), `hasNativeChromeTabs`.

- `callBridgeAction(name, ...params)` — call any host action with positional params
- `pickLocalFile(openInNewTab)` — native file picker (**not** `pickFile()`)
- `restoreLocalFile(filePath)`, `disposeLocalFileHost(filePath)`, `openFileInDefaultApp(filePath)`, `readTxtFileContent(filePath)`
- `restoreHbPdf(bookId, bookTitle, tabId)` — restore HebrewBooks PDF from cache; `hbSearch(...)`
- `fileSystemSearch(...)`, `fileSystemSearchWarmup()` — DocumentLocator search
- `getExcludedFolders()`, `setExcludedFolders(folders)`, `openExcludedFoldersManager()`, `resetDocumentLocatorIndex()`
- `exportToWord(html, title)`, `pasteIntoWord()`, `copyImageToClipboard(dataUrl)` — Word integration
- `resetHostApp()`, `resetSearchIndex()`, `getDiagnostics()`, `toggleFullscreen()`, `togglePopOut()`, `setTheme(...)`

## Utilities (`src/utils/`)

**persistence.ts** — the **storage driver**, and the only file that touches localStorage or a raw IndexedDB API. Two halves: a promise wrapper over IDB with one cached handle per database, and a namespaced (`kitvei-hakodesh.`), JSON-coded, non-throwing localStorage wrapper.

It has **zero imports**, and that is the invariant to protect — the moment it needs one it has started knowing something about the app. Check it with `grep -c '^import' src/utils/persistence.ts` → `0`.

It deliberately holds **no** schemas, retention policies, key names or reset workflow. Those belong to whoever owns the value: `TabState`/`BookState` and the `app-tabs` key layout → `stores/tabStatePersistence.ts`; `LastReadState` and the 1000-entry disk cap → `stores/bookLastRead.ts`; `Workspace` types, `tabsListKey` and workspace teardown → `stores/workspaceStore.ts`; the settings-vs-structural reset filter and the settings keys → `stores/settingsStore.ts`; the reset workflow → `features/settings/appResetState.ts`. If a change here can only be explained by naming a feature, it belongs in one of those instead. See the Persistence section of `app.md` for who may import it.

**hebrewTextProcessing.ts** — diacritics handling and text normalization for Hebrew.

**hebrewTextCleaning.ts** — text cleanup for copy/export paths (has unit tests).

**htmlText.ts** — HTML ↔ plain text conversion used by the copy and Word-export paths.

**textEncoding.ts** — encoding helpers.

**bookViewPerf.ts**, **commentaryScrollTrace.ts** — instrumentation for book-view performance and commentary scroll debugging.

**censorDivineNames.ts** — divine name censoring (replaces ה with ק).

**normalizeText.ts** — `normalize(s)`: lowercases and strips Hebrew/ASCII quote characters. Import this as the base normalization step before any search comparison.

**segmentSearchTree.ts** — generic segment-aware search tree for any hierarchical node list. `SegmentSearchTree` matches query words as an ordered subsequence across ancestor path segments. Used by `TreeView.vue`, commentary filter panels, and search utilities.

**detectFonts.ts** — `detectAvailableFonts()` uses canvas measurement to detect which Hebrew and general fonts are installed. Used by `FontSelector.vue`.

**scrollToIndexWithRetry.ts** — virtual scroller scroll-to-index with retry for async rendering.

**hebrewKetivExpander.ts** — Hebrew text expansion utilities for ketiv/keri handling.

## Theme System (`src/theme/`)

- `theme.css` — CSS custom properties (colors, fonts, spacing) for all themes
- `themeStore.ts` — active theme preset + reading background color
- `themes.ts` — theme loading, PDF theme observer
- `themeTypes.ts` — TypeScript types
- `themeColorUtils.ts` — color manipulation utilities
- `ThemeToggle.vue` — toggle button in the title bar
- `themes.json` — built-in theme presets

Default theme is `vscode-dark`.

## Initialization Order (`main.ts`)

1. `await checkAndExecPendingReset()` (from `features/settings/appResetState.ts`) — clear all DBs if a reset was scheduled last session
2. Create Pinia
3. Store init, synchronous and in this order — `workspaceStore.init()` must be first because `tabStore` depends on `activeId`, and `tabStore.init()` must be last: `workspaceStore`, `settingsStore`, `bookViewStore`, `themeStore`, `tabStore`
4. `initTabMirror()` — start mirroring the tab list to the host
5. Mount app to `#app`
6. `initPdfThemeObserver()` — sync PDF iframe theme with app theme
7. After one `requestAnimationFrame`, `booksDataStore.ensureLoaded()` — the catalog load IS the connection warm-up. Do **not** add a separate `dbWarmup` call or a delayed second load: a previous version raced two cold passes over the same connection and made startup slower. Exactly one cold pass.
8. Restore persisted local file tabs via `localFileStore.restoreTab()` — after mount, so the UI paints first
9. Signal the host that Vue has mounted and all event listeners are registered

Steps 3 and 5 are ordering-sensitive. Store `init()` methods are synchronous (localStorage-backed) by design — see the IDB performance rule in `app.md` before adding any `await` to this sequence.

## C# Backend

The C# project (`CSharpBackend/`) hosts the Vue app in a WebView2 control.

- Vue builds as a single-file bundle (`vite-plugin-singlefile`) — all JS/CSS inlined into one `index.html`
- `KitveiHakodesh.targets` runs `npm run build` after every C# build and copies `dist/` to `bin/{Config}/KitveiHakodesh/`
- C# injects `window.__webviewQuery`, `window.__webviewAction`, `window.__webviewDbReady` before the app boots
- Push events from C# arrive via `window.__onWebviewEvent`
- Target: .NET 4.8, C# 7.3

Key C# handlers (all under `KitveiHakodeshLib/`, organized into subfolders — the paths below are current):

- `Bridge/JsBridge.cs` — handles `__webviewAction` calls (file picker, PDF restore, virtual host management)
- `Bridge/WebBridge.cs` — WebView2 setup, message routing
- `Db/DbAccess.cs` / `Db/DbHandler.cs` — SQLite access via Dapper
- `Search/SearchHandler.cs` — FtsLib index lifecycle orchestrator
- `HebrewBooks/HebrewBooksHandler.cs` — HebrewBooks download via the WebView2 browser engine
- `Pdf/LocalFileHandler.cs` — local file virtual host management
- `Pdf/WordToPdfConverter.cs` — Word-to-PDF conversion
- `AppViewer*.cs` — the WinForms host shell (focus, navigation, tabs, theme, splash), plus `ChromeTabsMirror.cs`, `HostLink.cs`, `WordExporter.cs`

There is no `ZimHandler.cs` — ZIM/Kiwix support was removed.

### KitveiHakodeshService

`CSharpBackend/KitveiHakodeshService/` is the native .NET 10 data front-door, AOT-published. **Browser dev mode is fully service-dependent** — the Vite dev path talks to this service, not to a JS shim. Subfolders: `SeforimDb`, `Dictionary`, `Catalog`, `HebrewBooks`, `LocalFiles`, `Pdf`, `UserSettings`, `Http`, `Ipc`, `Common`, `KitveiHakodesh`.

Wire format is MessagePack with PascalCase keys; the frontend transforms to camelCase. The HTTP surface is a raw loopback `TcpListener` whose port is private — handed over an ACL'd pipe and discovered via `/khs-endpoint`, never written to a file.

Consequence for frontend code: `isHosted` is **true** in browser dev mode. It is therefore the wrong guard for "am I inside the C# WinForms host" — branch on `window.__webviewAction` for that instead.

### Full-Text Search Pipeline

The full-text search pipeline spans three layers: FtsLib (the custom index engine), KitveiHakodeshLib (the C# orchestration layer), and the Vue frontend.

**FtsLib** (`CSharpBackend/FtsLib-Csharp/`) is a custom LSM-style segment index built specifically for Hebrew/Aramaic seforim. It uses delta+varint compressed posting lists and skip-list accelerated intersection. The search engine handles index building, querying, and result retrieval with support for prefix/suffix/infix wildcards, fuzzy matching (Levenshtein distance), and Hebrew-specific features like spelling variant expansion. Public API entry point is `SeforimIndex` in `FtsLib/SeforimDb/`.

**KitveiHakodeshLib/Search/** contains the orchestration classes:

- `SearchHandler` — thin orchestrator. Manages the lifecycle of the search index (building, querying, resetting). Receives search requests from the frontend via bridge actions, delegates to the search engine, and streams results back to the frontend in batches via `WebBridge.PushEvent` (which calls `PostWebMessageAsString` → `chrome.webview` message events). Batch size starts at 1 and doubles up to 16, then switches to a 150ms timer flush.
- Search execution streams results to the frontend in batches, enriches each batch with TOC paths via a SQL query, and applies filtering inside the snippet loop.

**Vue frontend** (`src/features/full-text-search/`):

- `useFullTextSearch.ts` — calls `FtsSearchStart` via `callBridgeAction`, receives batches via a `chrome.webview` message listener keyed by `searchId`, enriches each batch with TOC paths via `GET_TOC_PATHS_FOR_LINES`, appends to the IDB cache via `searchCacheStore`. On a cache hit with a complete result set, skips the stream entirely. On a partial cache hit, passes `skipCount` to C# so the stream resumes from where it left off.
- `useFullTextSearchIndexingStatus.ts` — subscribes to `ftsIndexProgress` push events via `onWebviewEvent`. Handles `ftsIndexInvalidated` (automatic rebuild when DB changes or index is corrupt).

**Bridge actions** (all handled by `SearchHandler` via `JsBridge.cs`):

| Action | Direction | Description |
| --- | --- | --- |
| `FtsSearchStart` | Vue → C# | Start a search; returns `{ searchId }` |
| `FtsSearchCancel` | Vue → C# | Cancel an in-flight search by `searchId` |
| `GetFtsIndexingProgress` | Vue → C# | Poll current indexing state on mount |
| `ResetFtsIndex` | Vue → C# | Delete index and rebuild from scratch |
| `searchBatch` | C# → Vue | Batch of `FullTextSearchResult` objects |
| `searchComplete` | C# → Vue | Stream finished |
| `searchCancelled` | C# → Vue | Stream cancelled |
| `searchError` | C# → Vue | Stream error |
| `ftsIndexProgress` | C# → Vue | Indexing progress tick |
| `ftsIndexInvalidated` | C# → Vue | Index corrupt or missing; rebuild started automatically |

### DocumentLocator — File System Search Service

`CSharpBackend/DocumentLocator/` is a self-contained Windows Service that indexes the file system via the NTFS MFT and serves search queries over a named pipe. It is a separate product from KitveiHakodesh — any WinForms or desktop application can use it.

#### Dependency rule — read this before touching any of these projects

No host application ever references `DocumentLocator.dll` directly. The only public surface is `DocumentLocator.Client.dll`. The internal library is an implementation detail embedded inside the service exe.

```
Host app  →  DocumentLocator.Client.dll  →  named pipe  →  DocumentLocator.Service.exe
                                                               (embeds DocumentLocator.dll)
```

- `DocumentLocator.Client` — the only project host applications reference. Contains `ServiceBridge` (all pipe communication) and `ExcludedFoldersForm` (the reusable WinForms dialog for managing excluded folders). Adding a UI component that any host needs? It goes here.
- `DocumentLocator` (library) — Lucene index engine, MFT crawler, pipe protocol, `ExcludedFoldersPersistence`. Internal only. Never referenced directly by host apps. Embedded as a resource inside the service exe.
- `DocumentLocator.Service` — the Windows Service exe. Embeds `DocumentLocator.dll` and all its Lucene dependencies as resources via `EmbeddedResource` in its csproj, loaded at runtime by an `AssemblyResolve` handler in `Program.cs`. Ships as a single exe with no side-by-side DLLs.

#### Where things live

- Pipe communication (search, status, reindex, get/set excluded folders) → `ServiceBridge.cs` in `DocumentLocator.Client`
- Reusable UI dialogs for managing service settings → `DocumentLocator.Client` (e.g. `ExcludedFoldersForm.cs`)
- Index persistence, file format, crawling logic → `DocumentLocator` library
- Service lifecycle, pipe server, SCM registration → `DocumentLocator.Service`

#### KitveiHakodeshLib integration

`KitveiHakodeshLib/FileSystemSearch/` contains the KitveiHakodesh-specific glue:

- `DocumentLocatorAdapter.cs` — thin wrapper over `ServiceBridge` that translates results into `FileSystemSearchResult` objects. Belongs here because it is KitveiHakodesh-specific (Hebrew progress strings, result type mapping).
- `FileSystemSearchHandler.cs` — bridge action handler that wires `DocumentLocatorAdapter` and `ExcludedFoldersForm` to the Vue frontend via `WebBridge`.

`KitveiHakodeshLib` references `DocumentLocator.Client` only — never `DocumentLocator` (the library) directly.
