# Naming for Navigability

These rules exist so that any developer — or AI agent — can find the right file without searching, and can predict a file's path from its description alone. A codebase that follows these rules turns navigation from "search and hope" into "know and go".

---

## The Guess-the-Path Test

A name passes if someone who knows the app's features but has never seen the file tree can correctly guess the full path from a plain description. "The composable that syncs the active TOC entry as the user scrolls in the book reader" → `src/components/book-view/useBookViewScrollSync.ts`. If the path is not guessable, the name or location is wrong.

---

## Names Encode Both Feature and Role

A filename read in isolation must answer two questions: *what feature does this belong to?* and *what is its technical role?* Both must be present.

- `useSearch.ts` — answers only the role. Wrong.
- `useBloomSearch.ts` — answers both. Correct.
- `ScrollSync` — answers neither. Wrong.
- `useBookViewScrollSync` — answers both. Correct.

---

## The Folder Name Is the Feature Name

The folder name is the canonical name for the feature. Everything inside inherits it as a prefix. `book-view/` → `BookViewPage.vue`, `useBookView.ts`, `useBookViewScrollSync.ts`, `bookViewTypes.ts`. If a file in `book-view/` does not start with `BookView` or `useBookView`, it either belongs to a sub-feature with its own prefix (`Commentary*`, `useToc*`) or it is in the wrong folder.

---

## Siblings Share a Prefix

All files in a feature folder that belong to the same component family share the parent component's name as a prefix. Sub-components of `CommentaryView.vue` are named `CommentaryHeader.vue`, `CommentaryHeaderNav.vue`, `CommentaryFilterPanel.vue` — never `Header.vue` or `FilterPanel.vue`. This keeps related files alphabetically adjacent in the editor and makes the relationship visible without opening any file.

---

## One Concept, One Name — Everywhere

If the app calls something "commentary", every file, variable, prop, event, and CSS class that touches it uses the word "commentary" — never "comments", "notes", or "annotations". If the app calls something "workspace", it is never "project" or "environment" anywhere in the code. Vocabulary drift means every synonym is a search term that returns zero results. Pick the domain word once and use it everywhere.

---

## A Name Must Match Its Contents Exactly

If a file contains code that its name does not imply, that code either belongs in a different file or the name is wrong. A file named `useCommentary.ts` owns commentary logic only — not TOC logic, not scroll logic. A file named `persistence.ts` owns persistence only — not feature-specific business logic. If you cannot describe a file's entire contents using only its name, the mismatched code belongs elsewhere.

---

## No Junk Drawer Names

Folders and files named `utils`, `helpers`, `common`, `shared`, `misc`, `stuff`, or `other` are junk drawers. They accumulate unrelated code and become unsearchable. Every file must have a name specific enough that you know exactly what it contains without opening it. If you cannot name a utility file specifically, the utility probably belongs in an existing file or the feature folder that owns it.

---

## Symmetric Pairs

Operations that are inverses of each other must have symmetric names. If you have `openSearch`, you must have `closeSearch` — not `hideSearch` or `dismissSearch`. If you have `startHbDownload`, you must have `cancelHbDownload` — not `stopDownload`. Asymmetric names for symmetric operations are a navigation trap: you find one half of the pair and cannot predict the other.

---

## Collections and Maps Encode Their Structure

- Arrays and sets are plural: `tocEntries`, `selectedLineIds`, `hiddenCommentaryBookIds`
- Maps include key and value meaning: `allBooksMap`, `pathMap` — never just `map` or `data`
- Grouped data uses "by": `byBook`, `byType`, `orderById`

This makes misuse visible at a glance and makes refactors safer.

---

## Props Are a Public API

Prop names are the public API of a component. They must be unambiguous even when read outside the component's context. `isLoading` not `loading`. `selectedLineId` not `line`. `hiddenCommentaryBookIds` not `hidden`. A reader should understand a prop's meaning from its name alone, without looking at the component.

---

## Events Use Domain Language, Not UI Language

Emitted events describe what happened in the domain, not what the user did with the mouse. `@navigate-section` not `@button-clicked`. `@line-selected` not `@click`. `@toggle-filter-panel` not `@icon-pressed`. The exception is generic UI events on generic components: `@select`, `@close`, `@toggle` are fine on `TreeView`, `ConfirmDialog`, `SplitPane`.

---

## No Generic Names

Names like `data`, `state`, `manager`, `processor`, `handler`, `service`, `item`, `thing` say nothing about what is being managed, processed, or handled. They are placeholders, not names. Every name must be specific enough that it could not apply to any other file in the codebase.
