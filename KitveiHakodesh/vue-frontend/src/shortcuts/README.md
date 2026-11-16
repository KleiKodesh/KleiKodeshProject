# shortcuts — every keyboard shortcut, in one list, re-bindable by the user

**shortcutRegistry.ts** — the list. One row per shortcut: id (`book.toc`), group (the settings
grid's headings), scope (where it acts — used for conflicts), default combination(s), the
`settings.shortcutsList.*` description key, `fixed` for rows that are listed but never
re-bindable, `available` (false where the action does not exist in this host: no tab strip,
no split view in Word's pane) and `shown` (hidden from the grid for now, e.g. the side
commentary keys in a narrow pane). Order is screen order.

**shortcutCombo.ts** — a combination is one string, `Ctrl+Alt+Shift+<KeyboardEvent.code>`
(`Ctrl+KeyJ`, `F8`, `Alt+ArrowRight`). Always the physical `code`, never `key`, so shortcuts
keep working when the keyboard is in Hebrew. Also: which combinations may not be chosen
(`comboProblem` — no Ctrl/Alt and not F1–F12, copy/paste keys, Windows keys), display labels,
and `UNREACHABLE_IN_WORD`, the measured list of combinations Word's task pane never delivers
(measurement and method in the comment above it).

**shortcutsStore.ts** — the user's changes, only the rows that differ from the defaults, in
localStorage under `shortcuts.bindings` (`lsGet`/`lsSet`, like every setting), and the user's
**personal shortcuts** under `shortcuts.personal` (ids `personal.*`; today one kind, "open this
book": book id, title, one combination). `idsFor(e)` maps a keydown to the shortcut ids bound
to it, personal ones included; `conflictsFor`, `assign`, `reset`, `resetAll`,
`addPersonalBook`, `removePersonal` serve the grid. A personal shortcut acts pane-wide, so it
collides with every other shortcut. "Restore all" leaves personal shortcuts in place but takes
back a default key one of them was using. Each host has its own WebView2 profile, so Word's
pane and the standalone app keep separate shortcuts. "Reset settings" clears both keys with the
other preferences.

**shortcutCapture.ts** — records the combination the user presses in the grid. Imported FIRST
in `main.ts` so its keydown listener runs before every shortcut handler and swallows the press
(otherwise choosing Ctrl+K would also open the table of contents). Esc cancels, Backspace or
Delete removes the shortcut. A combination Word or another program takes first never arrives,
so it can never be recorded — the grid cannot accept a shortcut that would never fire.

**shortcutLabel.ts** — `shortcutLabel(id)` / `withShortcut(text, id)`: the current combination
for tooltips and menus. Translated strings carry only the words; the key is added here, so a
tooltip follows the user's change.

## Where the actions run

The handlers ask the store which id a keydown is and run the action for it:

- `layout/useAppTitleBarShortcuts.ts` — pane-scoped ids (navigation, tabs, panels, quick
  navigation), app-wide ones (fullscreen, reading mode, split view, ...), and personal
  "open this book" shortcuts, which open the book the way a book link does (`openBookTarget`:
  in place in Word's single-tab pane, a new tab where there is a tab strip).
- `features/book-view/useBookViewKeyboardShortcuts.ts` — `book.cleanText`, `book.bookmark`,
  `book.nextSection`, `book.previousSection`.
- `features/fast-doc-view/FastDocViewPage.vue` — `doc.cleanText`, `doc.nextHeading`,
  `doc.previousHeading`.

Fixed rows keep their own key checks where they always were (zoom, Ctrl+Up/Down and Enter in
the book view, list keys, Ctrl+Tab, the copy family, F7 caret browsing).

## Adding a shortcut

1. A row in `SHORTCUTS` (and a description in `settings.shortcutsList` in both locales).
2. A `case` for its id in the handler of its scope.
3. A tooltip that names it uses `withShortcut(text, id)` — never a key spelled into a string.

The tests (`shortcuts.test.ts`) check ids are unique and that no two defaults that can be
active together share a combination.

## Known limits

- The HTML/text viewer's iframe forwards only Ctrl+key presses to the app (JsBridge), so a
  shortcut moved to an Alt or F-key combination does not fire while focus is inside that iframe.
- Zoom stays fixed: it is handled in four places with focus rules of their own.
