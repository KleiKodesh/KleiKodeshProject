# Naming Conventions

Every name in this codebase — file, folder, variable, function, component, store, composable — must be immediately clear to someone who has never seen the file before. If a name requires a comment to explain it, the name is wrong.

---

## Core Rules

**No abbreviations, ever.** Write the full word. No exceptions except universally understood domain terms where the short form IS the name: `id`, `url`, `html`, `css`, `sql`, `api`, `db` (only as a suffix on a database connection variable). Everything else is spelled out: `configuration` not `config`, `navigation` not `nav`, `parameter` not `param`, `button` not `btn`, `message` not `msg`, `error` not `err`, `reference` not `ref` (unless it is a Vue template ref), `source` not `src` (unless it is an HTML attribute), `index` not `idx`, `previous` not `prev`, `abbreviation` not `abbrev`, `definition` not `def`, `destination` not `dest`.

**A name must match its contents exactly.** If a file contains code that its name does not imply, that code either belongs in a different file or the name is wrong. A file named `useCommentary.ts` owns commentary logic only — not TOC logic, not scroll logic. A file named `persistence.ts` owns persistence only — not feature-specific business logic.

**Names must indicate context and placement.** A name read in isolation must tell you what feature it belongs to, what layer it lives in, and what it does. `useSearch.ts` is too vague — `useFileSystemSearch.ts` or `useBloomSearch.ts` is correct. `types.ts` is too vague — `bookViewTypes.ts` or `calendarTypes.ts` is correct.

**Siblings share a prefix.** All files in a feature folder that belong to the same component family share the parent component's name as a prefix. Sub-components of `CommentaryView.vue` are named `CommentaryHeader.vue`, `CommentaryHeaderNav.vue`, `CommentaryFilterPanel.vue` — never `Header.vue` or `FilterPanel.vue`. All composables for the book view are prefixed `useBookView*`.

---

## File Types and Their Naming Rules

### Vue Components (`.vue`)
- PascalCase always: `BookViewPage.vue`, `CommentaryHeader.vue`
- Always multi-word — never a single word: `SearchPage.vue` not `Search.vue`
- Pages are always suffixed `*Page.vue`: `SearchPage.vue`, `SettingsPage.vue`
- Sub-components are prefixed with their parent: `BookViewToolbar.vue` is a sub-component of `BookViewPage.vue`
- Word order: highest-level (most general) word first, modifying words last. `BookViewSearchBar` not `SearchBarBookView`. This keeps related files alphabetically adjacent in the editor.
- A component name must describe what it renders, not what it does: `CommentaryFilterPanel.vue` not `useCommentaryFilter.vue`

### Composables (`.ts` with `use` prefix)
- `use` prefix is reserved exclusively for functions that use Vue reactivity (refs, computed, watch, lifecycle hooks) and are called inside `setup()`
- The name after `use` describes the behavior owned, not the data type: `useBookViewScrollSync` not `useScrollData`
- Never use `use` prefix for plain utility functions, even if they are async or complex: `dafYomiNavigation.ts` not `useDafYomiNavigation.ts`
- Never use `use` prefix for modules with lazy-loaded shared state: `homeDateInfo.ts` not `useHomeDateInfo.ts`

### Utility Files (`.ts`, no `use` prefix)
- camelCase for the filename: `normalizeText.ts`, `booksCategoryTree.ts`, `commentaryNav.ts`
- The name describes what the file provides, not what it is: `booksCategoryTree.ts` (provides tree-building logic for the book catalog) not `treeUtils.ts`
- Never use generic names: no `utils.ts`, `helpers.ts`, `common.ts`, `shared.ts`. Every utility file must have a name specific enough that you know exactly what it contains without opening it.

### Types Files (`*Types.ts`)
- PascalCase with `Types` suffix: `bookViewTypes.ts`, `dictionaryTypes.ts`, `calendarTypes.ts`
- A types file exists only when a type is shared between a composable and a component (or between multiple composables). A type used in only one file stays in that file.
- Never define types in `.vue` files that are needed by `.ts` files — move them to a types file first.

### Stores (`*Store.ts`)
- camelCase with `Store` suffix in the filename: `tabStore.ts`, `settingsStore.ts`, `booksDataStore.ts`
- The Pinia store id matches the filename without the suffix: `defineStore('tabs', ...)`, `defineStore('settings', ...)`

### SQL Files (`*.sql.ts`)
- Named after the database they serve: `queries.sql.ts` for the main seforim DB, `dictionaryDb.sql.ts` for the dictionary DB
- SQL constant names are SCREAMING_SNAKE_CASE: `SQL.GET_ALL_TOC_ENTRIES`, `SQL.GET_BOOK_BY_ID`

### Host Layer Files (`src/host/`)
- Named after what they connect to: `seforimDb.ts`, `dictionaryDb.ts`, `bridge.ts`, `devFallbacks.ts`
- No `use` prefix — these are not composables

---

## Folder Naming

- All lowercase, kebab-case: `book-view/`, `book-catalog/`, `hebrew-calendar/`, `full-text-search/`
- Folder name describes the feature, not the technology: `book-view/` not `reader/`, `full-text-search/` not `bloom/`
- No generic folder names: no `utils/` inside a feature folder (put utilities in `src/utils/`), no `components/` inside a feature folder (all components live flat in the feature folder)

## Vue Official Style Guide Alignment

These rules come from the [Vue official style guide](https://vuejs.org/style-guide) and are enforced here.

**Multi-word component names (Priority A — Essential).** Every component name must be at least two words. This prevents conflicts with HTML elements which are always single words. `Search.vue` is forbidden — `SearchPage.vue` or `SearchBar.vue` is correct.

**Component names start with the highest-level word (Priority B — Strongly Recommended).** The most general/contextual word comes first, modifiers come last. This keeps related files alphabetically adjacent. `SearchButtonClear.vue` and `SearchButtonRun.vue` not `ClearSearchButton.vue` and `RunSearchButton.vue`. In this codebase: `BookViewToolbar.vue`, `BookViewSearchBar.vue`, `BookViewSplitPane.vue` — not `ToolbarBookView.vue`.

**Tightly coupled child components share the parent prefix (Priority B).** Already covered in our Core Rules above — this is the official Vue recommendation too.

**PascalCase for component filenames (Priority B).** All `.vue` files use PascalCase. This codebase uses PascalCase exclusively — never kebab-case filenames.

**Full words, no abbreviations (Priority B).** The official guide explicitly calls this out: `StudentDashboardSettings.vue` not `SdSettings.vue`, `UserProfileOptions.vue` not `UProfOpts.vue`. Matches our Core Rules.

**Props declared in camelCase (Priority B).** `defineProps<{ bookId: number }>()` not `defineProps<{ 'book-id': number }>()`. In templates, Vue automatically handles the kebab-case binding: `:book-id="..."` binds to `bookId`.

**Scoped styles on all non-layout components (Priority A).** Every component except `App.vue` and layout components uses `<style scoped>`. Never use unscoped styles in a component file.

**No `v-if` and `v-for` on the same element (Priority A).** Use a computed property to filter the list, or wrap with `<template v-for>` and put `v-if` on the inner element.

**Always use `:key` with `v-for` (Priority A).** Every `v-for` must have a `:key` binding using a stable unique identifier — never the loop index unless the list is static and never reordered.

**Directive shorthands consistently (Priority B).** Always use shorthands: `:` for `v-bind`, `@` for `v-on`, `#` for `v-slot`. Never mix shorthand and longhand in the same template.

---



**Event handlers** are prefixed `on`: `onLineSelected`, `onTocSelect`, `onNavigateSection`. Never `handleLineSelected` or `lineSelectedHandler`.

**Booleans** use `is` or `has` prefix: `isSearching`, `hasCommentaries`, `isHosted`. Never `searching` (ambiguous — could be a noun or adjective), never `commentariesExist`.

**Async functions** that load data are prefixed `load` or `fetch` when they are side-effectful: `loadDateInfo`, `fetchThesaurus`. Pure async transformations use a descriptive verb: `buildCommentaryGroups`, `resolveCategory`.

**Computed values** that derive from state use a noun or adjective, never a verb: `activeMatchCount` not `countActiveMatches`, `filteredResults` not `filterResults`.

**Refs that hold component instances** use the component name in camelCase with `Ref` suffix: `commentaryViewRef`, `linesContentRef`, `searchBarRef`.

**Cache variables** are prefixed with underscore to signal they are internal implementation details: `_staticFilterGroupsCache`, `_lastReadCount`.

---

## What a Name Must NOT Do

- Must not require reading the file to understand: if the name is clear, the file's purpose is clear without opening it
- Must not be so broad it could apply to many things: `data.ts`, `state.ts`, `manager.ts` are all forbidden
- Must not use technical jargon as a substitute for a descriptive name: `processor.ts`, `handler.ts`, `service.ts` say nothing about what is being processed, handled, or served
- Must not be a verb when a noun is more precise: `search.ts` is ambiguous (is it the search UI? the search logic? the search results?); `useBloomSearch.ts` is precise
- Must not omit the feature context: `useSearch.ts` could be any search in the app; `useFileSystemSearch.ts` is unambiguous

---

## Navigability Rules

These rules exist so that any developer — or AI agent — can find the right file without searching, and can predict a file's path from its description alone.

### The "Guess the Path" Test
A name passes if someone who knows the app's features but has never seen the file tree can correctly guess the full path from a description. "The composable that syncs the active TOC entry as the user scrolls in the book reader" → `src/components/book-view/useBookViewScrollSync.ts`. If the path is not guessable, the name or location is wrong.

### Names Encode Layer and Feature Together
A filename read in isolation must answer two questions: *what feature does this belong to?* and *what is its technical role?* Both must be present. `useSearch.ts` answers only the second. `useBloomSearch.ts` answers both. `BookViewScrollSync` answers both. `ScrollSync` answers neither.

### Collections and Maps Encode Their Structure
- Arrays and sets are plural: `tocEntries`, `selectedLineIds`, `hiddenCommentaryBookIds`
- Maps include key and value meaning: `allBooksMap`, `pathMap`, `lineData` — never just `map` or `data`
- Grouped data uses "by": `byBook`, `byType`, `orderById`

### Symmetric Pairs
Operations that are inverses of each other must have symmetric names. If you have `openSearch`, you must have `closeSearch` — not `hideSearch` or `dismissSearch`. If you have `startHbDownload`, you must have `cancelHbDownload` — not `stopDownload`. Asymmetric names for symmetric operations are a navigation trap.

### Props as a Public API
Prop names are the public API of a component. They must be as descriptive as the component name itself. `isLoading` not `loading`. `selectedLineId` not `line`. `hiddenCommentaryBookIds` not `hidden`. A prop name must be unambiguous even when read outside the component's context.

### Emitted Events Use Domain Language, Not UI Language
Events describe what happened in the domain, not what the user did with the mouse. `@navigate-section` not `@button-clicked`. `@line-selected` not `@click`. `@toggle-filter-panel` not `@icon-pressed`. The exception is generic UI events on generic components: `@select`, `@close`, `@toggle` are fine on `TreeView`, `ConfirmDialog`, `SplitPane`.

### Constants Are SCREAMING_SNAKE_CASE
Module-level constants that represent fixed configuration values use `SCREAMING_SNAKE_CASE`: `ZOOM_CONFIG`, `STATIC_FILTER_CACHE_MAX`, `CHUNK_SIZE`, `HB_HISTORY_MAX`. This makes them visually distinct from reactive state and computed values at a glance.

### One Concept, One Name — Everywhere
If the app calls something "commentary", every file, variable, prop, event, and CSS class that touches it uses the word "commentary" — never "comments", "notes", "annotations", or "explanations". If the app calls something "workspace", it is never "project", "environment", or "session" anywhere in the code. Vocabulary drift is a navigation tax: every synonym is a search term that returns zero results.

### The Folder Name Is the Feature Name
The folder name is the canonical name for the feature. Everything inside inherits it as a prefix. `book-view/` → `BookViewPage.vue`, `useBookView.ts`, `useBookViewScrollSync.ts`, `bookViewTypes.ts`. If a file in `book-view/` does not start with `BookView` or `useBookView`, it either belongs to a sub-feature with its own prefix (`Commentary*`, `useToc*`) or it is in the wrong folder.

### No Junk Drawer Names
Folders and files named `utils`, `helpers`, `common`, `shared`, `misc`, `stuff`, or `other` are junk drawers. They accumulate unrelated code and become unsearchable. Every file must have a name specific enough that you know exactly what it contains without opening it. If you cannot name a utility file specifically, the utility probably belongs in an existing file or the feature folder that owns it.
