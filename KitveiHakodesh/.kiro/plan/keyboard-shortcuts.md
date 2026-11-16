---
inclusion: manual
---

# Keyboard Shortcuts

The catalogue of shortcuts is **code**, not this document:
`vue-frontend/src/shortcuts/shortcutRegistry.ts` lists every shortcut with its id, scope,
default combination(s) and description, and the settings page draws its grid from it. The
user can change any row not marked `fixed` (Settings → Keyboard shortcuts → "שנה" ("Change")).
This file explains the design; the README next to the registry has the file-by-file detail.

## How a key press becomes an action

1. `src/shortcuts/shortcutCapture.ts` — the first keydown listener on `window` (imported first
   in `main.ts`). Idle unless the settings grid is recording a new combination; then it takes
   the press and nothing else sees it.
2. The handlers — `layout/useAppTitleBarShortcuts.ts` (capture phase, per pane: pane-scoped
   ids, and app-wide ids in pane 1 only), `features/book-view/useBookViewKeyboardShortcuts.ts`,
   `features/fast-doc-view/FastDocViewPage.vue` — call `useShortcutsStore().idsFor(e)` and run
   the action for the id(s) returned.
3. The store maps the press (`Ctrl+Alt+Shift+<e.code>`) to ids: the user's own combination
   if they changed the row (localStorage `shortcuts.bindings`), the registry default otherwise.

Matching is always on `e.code`, never `e.key`, so shortcuts survive a Hebrew keyboard.

## Groups (screen order)

Personal shortcuts (the user's own — today "open this book", kept in `shortcuts.personal`) ·
Tab management (native tab strip only) · Navigation · Quick navigation (Ctrl+1–9, Ctrl+U, F1) ·
Book view · Documents · Search results (fixed) · View. The defaults are the registry's
`defaults` fields; the user-facing list is the settings grid.

## Fixed rows

Listed in the grid, never re-bindable: Ctrl+Tab / Ctrl+Shift+Tab (the native strip's
hold-to-switch, driven from C#), zoom (Ctrl +/-/0 — four handlers with focus rules of their
own), Ctrl+Up/Down and Enter in the book view (caret-browsing interplay), list keys in search
results and the workspace picker, the copy family in documents, and F7 (Chromium's caret
browsing). They count in conflict checks, so nothing can be bound on top of them.

## Conflicts

Two shortcuts may share a combination only when their scopes are never active together
(`book.toc` and `doc.toc` are both Ctrl+K). `app` and `pane` scopes collide with everything.
The grid refuses a combination held by a fixed row, asks before moving one from another
shortcut, and refuses combinations without Ctrl/Alt (F1–F12 excepted), the copy/paste/undo
keys and Windows' own keys.

## Inside Word (measured 2026-11-16)

Real keystrokes sent to a focused Kitvei Hakodesh pane in Word, recorded by a first-in-line
listener injected over the WebView2 debugging port:

- Never reach the page: **Ctrl+Shift+J, Ctrl+Shift+K, Ctrl+Shift+Q, Ctrl+Shift+W, F10,
  Ctrl+Shift+0**. Ctrl+Shift+J is the default for "commentaries on the right", so in Word that
  row shows a warning and the user can move it.
- Reach the page: every Ctrl+letter, Ctrl+Alt+letter, Alt+letter, Alt+Shift+letter, the other
  Ctrl+Shift+letters, Ctrl/Alt/Ctrl+Shift+digits, F1–F9, F11, F12, modified F-keys, Ctrl+Tab,
  Ctrl+Enter, Alt/Ctrl+arrows, Ctrl+-, Ctrl+=, Ctrl+\, Ctrl+Space.
- A system-wide hotkey of another program wins over both (on the measuring machine Google
  Drive took Ctrl+Alt+G). That differs per computer, which is why the grid records the real
  press: what cannot arrive cannot be chosen.

The full method is in the comment above `UNREACHABLE_IN_WORD` in `shortcutCombo.ts`.

## Bridge actions

F11 / Ctrl+Shift+F (fullscreen) calls the C# `toggleFullscreen` bridge action
(`CSharpBackend/KitveiHakodeshLib/AppViewer.cs`, `HandleToggleFullscreen`); Ctrl+Tab hands the
direction to the native tab strip (`toggleChromeTabList`). Everything else is frontend-only.

## Adding a shortcut

1. A row in `SHORTCUTS` (`shortcutRegistry.ts`) and its description in
   `settings.shortcutsList` in both locales.
2. A `case` for its id in the handler of its scope.
3. Any tooltip that names it uses `withShortcut(text, id)` (`shortcuts/shortcutLabel.ts`) —
   never a key spelled into a translated string, which would lie once the user changes it.
4. Test with the title bar hidden (Ctrl+H) and, for anything used inside Word, in the task pane.
