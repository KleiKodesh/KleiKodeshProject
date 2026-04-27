# Remaining Refactor Tasks

Based on the comprehensive analysis of the non-book-view codebase.

---

## Task 1: Delete Unused/Unnecessary Files

### 1.1 Delete `useOnlineStatus.ts` ✅ Done
- **File:** `vue-frontend/src/utils/useOnlineStatus.ts`
- **Reason:** One-line wrapper around `useOnline()` with no added value, nobody calls it
- **Status:** Deleted

### 1.2 Delete `useBooksFsViewItems.ts` ✅ Done
- **File:** `vue-frontend/src/components/books-fs/useBooksFsViewItems.ts`
- **Reason:** Three pure functions with no state, no reactivity, no lifecycle — not a composable
- **Action:** Inlined `activateIndex` and `getTitle` directly into `BooksFsListView.vue` and `BooksFsTileView.vue`
- **Status:** Done

---

## Task 2: Fix Type Import Violations ✅ Done

### 2.1 Move `WordPageData` out of `DictionaryPage.vue` ✅ Done
- **Files:** `vue-frontend/src/components/dictionary/DictionaryPage.vue`, `dictCache.ts`
- **Problem:** `dictCache.ts` imports `WordPageData` from a `.vue` file
- **Fix:** Created `dictionaryTypes.ts`, moved `WordPageData` there, updated both imports
- **Status:** Done

### 2.2 Move `TreeNodeItem` out of `TreeNode.vue` ✅ Done
- **Files:** `vue-frontend/src/components/common/TreeNode.vue`, `useToc.ts`, etc.
- **Problem:** `.ts` files importing `TreeNodeItem` from a `.vue` file
- **Fix:** Created `treeTypes.ts`, moved `TreeNodeItem` there, updated all imports
- **Status:** Done

### 2.3 Move `SearchMode` out of `BookViewSearchBar.vue` ✅ Done
- **Files:** `vue-frontend/src/components/book-view/BookViewSearchBar.vue`, `useBookView.ts`
- **Problem:** `useBookView.ts` importing `SearchMode` from a `.vue` file
- **Fix:** Created `bookViewTypes.ts`, moved `SearchMode` and `SidePanelMode` there
- **Status:** Done

---

## Task 3: Deduplicate Bridge Action Calls ✅ Done

### 3.1 Extract `callAction` to shared utility ✅ Done
- **Files:** `useBloomSearch.ts`, `useIndexingStatus.ts`
- **Problem:** Identical `callAction()` function copy-pasted in both files
- **Fix:** Added `callBridgeAction()` to `bridge.ts`, replaced both usages
- **Status:** Done

---

## Task 4: Fix Bridge Call Locations ✅ Done

### 4.1 Move reset actions from `useSettingsPage.ts` to `bridge.ts` ✅ Done
- **File:** `vue-frontend/src/components/settings/useSettingsPage.ts`
- **Problem:** Direct `window.__webviewAction` calls in a composable
- **Fix:** Added `resetHostApp()` and `resetSearchIndex()` to `bridge.ts`, updated composable to call them
- **Status:** Done

---

## Task 5: Fix Module-Level State Issues

### 5.1 Fix `useHomeDateInfo.ts` module-level ref
- **File:** `vue-frontend/src/components/home/useHomeDateInfo.ts`
- **Problem:** `dateInfo` is a module-level ref shared across all instances
- **Options:**
  - Convert to proper composable that returns the ref (per-instance)
  - Or keep module-level if it's intentionally shared (date is the same for all tabs)
- **Decision needed:** Is the date info meant to be shared app-wide or per-page?
- **Status:** Pending decision

---

## Task 6: Remove Unnecessary Re-exports ✅ Done

### 6.1 Remove type re-exports from `useBooksFs.ts` ✅ Done
- **File:** `vue-frontend/src/components/books-fs/useBooksFs.ts`
- **Problem:** Re-exports `BookFsItem`, `TocFsItem`, `SearchFsItem` from `useBooksFsSearch.ts`
- **Fix:** Removed the re-export line
- **Status:** Done — nothing imports those types through `useBooksFs`

---

## Task 7: Deduplicate Query Normalization ✅ Done

`toWords` in `dafYomiNavigation.ts` was a private function — inlined directly at the call site (one line). No shared utility needed.

---

## Task 8: Deduplicate Book Filtering Logic ✅ Done

`filterBooksByWords` moved from `useBooksFsSearch.ts` to `src/utils/booksCategoryTree.ts` where it belongs (pure catalog logic). Both `useBooksFsSearch.ts` and `dafYomiNavigation.ts` now import it from there. `useSearchFilters.ts` has a different variant (`matchBookIds` — filters a subset, not the full catalog) so it stays separate.

---

## Task 9: Fix Naming Violations ✅ Done

- `useDafYomiNavigation.ts` → `dafYomiNavigation.ts` (plain utility, not a composable)
- `useHomeDateInfo.ts` → `homeDateInfo.ts` (module with lazy-loaded shared state, not a composable)
- `HomePage.vue` updated to import from new filenames

---

## Task 11: Scan Rest of App

### 11.1 — `src/components/layout/`
Files: `AppTitleBar.vue`, `AppPageView.vue`, `AppTitleBarNavDropdown.vue`, `AppTitleBarTabDropdown.vue`, `DesktopMainPanel.vue`, `DesktopShell.vue`
- Check: are `DesktopMainPanel` and `DesktopShell` real components or thin wrappers?
- Check: does `AppTitleBar` have logic that belongs in a composable?
- Check: do the two dropdown components have duplicated open/close logic?

### 11.2 — `src/components/common/`
Files: `BottomSearchBar.vue`, `ConfirmDialog.vue`, `ContextMenu.vue`, `IconTreeRtl.vue`, `LoadingAnimation.vue`, `SplitPane.vue`, `TabStrip.vue`, `TreeNode.vue`, `TreeView.vue`
- Check: `IconTreeRtl.vue` — is it just `<IconTextBulletListTree style="transform:scaleX(-1)"/>` with no logic? If so, inline it everywhere it's used.
- Check: `TabStrip.vue` — is it used? Is it a real component or a thin wrapper?
- Check: `BottomSearchBar.vue` — is it shared or only used in one place?

### 11.3 — `src/components/settings/`
Files: `SettingRow.vue`, `HintIcon.vue`, `SliderSetting.vue`, `ToggleGroup.vue`, `FontDisplaySettings.vue`, `FontSelector.vue`, `ThemePicker.vue`, `SetupWizard.vue`, `SettingsPage.vue`, three pane components, `useSettingsPage.ts`
- Check: `HintIcon.vue` — is it just a tooltip icon with no logic? Could be inline.
- Check: `SettingRow.vue` — is it a real reusable component or just a layout wrapper?
- Check: `useSettingsPage.ts` — after the bridge fix, does it still have the right responsibilities?

### 11.4 — `src/components/hebrew-calendar/`
Files: `HebrewCalendarPage.vue`, `MonthlyView.vue`, `WeeklyView.vue`, `DayRow.vue`, `CalendarHeader.vue`, `useMonthlyView.ts`, `useWeeklyView.ts`, `useZmanim.ts`, `calendarTypes.ts`
- Check: `CalendarHeader.vue` — is it a real component or just a few HTML elements?
- Check: `DayRow.vue` — same question.
- Check: `useMonthlyView.ts` and `useWeeklyView.ts` — do they have the right scope or is logic leaking between them?

### 11.5 — `src/components/hebrew-books/`
Files: `HebrewBooksPage.vue`, `HebrewBooksListItem.vue`, `useHebrewBooks.ts`, `hebrewBooksCatalog.ts`
- Check: `HebrewBooksListItem.vue` — is it complex enough to justify a component or just a list row?
- Check: `hebrewBooksCatalog.ts` — is it pure data or does it have logic that belongs elsewhere?

### 11.6 — `src/components/dictionary/`
Files: `DictionaryPage.vue`, `DictionaryWordPage.vue`, `dictCache.ts`, `dictionaryTypes.ts`
- Check: `DictionaryPage.vue` — already flagged as doing too much data fetching inline. Assess whether it's worth extracting.
- Check: `DictionaryWordPage.vue` — is it a real component or a display-only wrapper?

### 11.7 — `src/components/conversions/` and `src/components/midot/`
Files: `MidotPage.vue`, `midot.ts`, `units/`
- Check: is `midot.ts` pure logic with no Vue dependencies? (Should be in `src/utils/` if so.)
- Check: `midot/` folder — what's in it vs `conversions/`? Are they duplicates?

### 11.8 — `src/components/pdf/`
Files: `PdfViewPage.vue`, `PdfToolbar.vue`
- Check: `PdfToolbar.vue` — is it a real toolbar or just a few buttons that could be inline?
- Check: `pdf-viewer/` folder is empty — delete it.

### 11.9 — `src/components/workspace/`
Files: `WorkspaceManagerPage.vue` only
- Check: is all logic inline in the page or is there a composable that should exist?

### 11.10 — `src/theme/`
Files: `themeStore.ts`, `themes.ts`, `themeColorUtils.ts`, `themeTypes.ts`, `ThemeToggle.vue`
- Check: `themeColorUtils.ts` — is it used? Is it pure utils?
- Check: `themes.ts` — does it have responsibilities that belong in the store?
- Check: `ThemeToggle.vue` — is it a real component or just a button?

### 11.11 — `src/host/`
Files: `seforimDb.ts`, `dictionaryDb.ts`, `dictionaryDb.sql.ts`, `dictionarySeforimDb.ts`, `queries.sql.ts`, `bridge.ts`, `devFallbacks.ts`
- Check: `dictionarySeforimDb.ts` — does it follow the same pattern as `dictionaryDb.ts` or is there inconsistency?
- Check: `queries.sql.ts` — are all SQL strings here or are some still inline?
- Check: `seforimDb.ts` — is the schema detection logic (`ensureCategorySchema`) in the right place?

### 11.12 — `src/main.ts`
- Check: initialization order is correct per architecture.md
- Check: any logic that doesn't belong at the top level

---

## Task 12: Over-Abstraction Audit

After Task 11 findings are collected, review each flagged item against these criteria:

### 12.1 — Unnecessary composables
A composable is only justified if it has: reactive state OR side effects OR logic complex enough to warrant a name. Flag any `use*.ts` file that is just a thin wrapper, re-export, or contains fewer than ~10 meaningful lines with no real encapsulation.

### 12.2 — Over-split components (markup that should be inline)
A component is only justified if it has: its own logic/state OR is reused in 2+ places OR its markup is complex enough that inlining hurts readability. Flag any `.vue` file that is just a few HTML elements with no props variation, no events, no state, used in exactly one place.

### 12.3 — Performance-affecting abstractions
Each Vue component has runtime cost (vnode, instance, lifecycle hooks). Flag components that:
- Have no reactive state
- Have no events
- Are used in exactly one place
- Could be replaced with a `<template>` fragment or plain HTML

### 12.4 — Files that exist only to re-export
Any `.ts` file whose entire purpose is `export { x } from './y'` — these add an indirection layer with no value. The one exception is a deliberate public API barrel file, which must be documented as such.

---

## Priority Order for Tasks 11 + 12

| Subtask | What to look for | Expected effort |
|---|---|---|
| 11.8 | Delete empty `pdf-viewer/` folder | Trivial |
| 11.2 | `IconTreeRtl.vue`, `TabStrip.vue` usage | Low |
| 11.7 | `midot/` vs `conversions/` overlap, `midot.ts` location | Low |
| 11.3 | `HintIcon.vue`, `SettingRow.vue` justification | Low |
| 11.4 | `CalendarHeader.vue`, `DayRow.vue` justification | Low |
| 11.5 | `HebrewBooksListItem.vue` justification | Low |
| 11.1 | `DesktopMainPanel`, `DesktopShell` justification | Medium |
| 11.6 | `DictionaryPage.vue` data fetching | Medium |
| 11.9 | `WorkspaceManagerPage.vue` logic | Low |
| 11.10 | Theme system responsibilities | Low |
| 11.11 | Host layer consistency | Low |
| 11.12 | `main.ts` | Low |
| 12.* | Over-abstraction audit across all findings | After 11 |

---

## Current Work In Progress ✅ Complete

All interrupted work finished. Build passes. Ready for Tasks 11 and 12.

---

## Priority Order

| Task | Impact | Effort | Status |
|---|---|---|---|
| 1.1 Delete `useOnlineStatus.ts` | Noise | Trivial | ✅ Done |
| 1.2 Delete `useBooksFsViewItems.ts` | Noise | Trivial | Pending |
| 2.* Fix type import violations | Architecture | Low | ✅ Done |
| 3.1 Deduplicate `callAction` | Maintainability | Low | ✅ Done |
| 4.1 Move bridge calls from composable | Architecture | Low | ✅ Done |
| 6.1 Remove type re-exports | Noise | Trivial | ✅ Done |
| 5.1 Fix `useHomeDateInfo` module ref | Correctness | Low | Pending decision |
| 7.1 Deduplicate query normalization | Maintainability | Low | Pending |
| 8.1 Deduplicate book filtering | Maintainability | Medium | Pending |
| 9.1 Rename `useDafYomiNavigation` | Naming | Low | Pending decision |
| 10.* Update documentation | Polish | Low | Partial |

---

## What NOT to do (agent was wrong)

- **Don't move `scrollToIndexWithRetry.ts`** — it's a pure function with no Vue lifecycle, belongs in utils
- **Don't delete `resetState.ts`** — it's a shared signal across unrelated components, module-level ref is correct
- **Don't extract data fetching from `DictionaryPage.vue`** — tightly coupled to UI, would add indirection for no gain
- **Don't add cache cleanup on tab close** — caches are LRU-capped and persist across sessions by design
- **Don't add bounds checking to calendar** — infinite navigation is intentional
