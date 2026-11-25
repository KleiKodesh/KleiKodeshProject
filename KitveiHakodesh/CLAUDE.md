# Commits

Every commit message body ends with one line per change the user will notice:

`User-facing: new|improved|fixed - <Area>: <one plain sentence>`
`User-facing-he: <the same sentence in Hebrew>`

- Area = the name in `.github/scripts/release-glossary.txt`.
- The sentence is what a non-programmer user notices. No jargon, file names or code terms.
- Gemini puts this text into the release notes as written, so write it like a release note.
- The Hebrew line is the author's own wording for the Hebrew release notes; Gemini only
  fixes grammar and glossary terms in it. Write it like a note a yeshiva student reads.
  Commits up to 2026-11-02 carried only the English line; from now on write both.
- Internal-only work (refactors, cleanup, CI, build, tests, docs) gets `User-facing: none`.

Example:

```
Book view: bookmarks anchored to the exact text position

User-facing: new - Book view: save bookmarks at the exact place in the text (Ctrl+Shift+D)
User-facing-he: <Hebrew: save a bookmark at the exact place in the text (Ctrl+Shift+D)>
```

# Every window alike (desktop app)

The page draws every window: the main one, extra app windows, widget and commentary windows
(one page, one renderer). New features must work in all of them:

- Component/composable code uses the window it is drawn in: `const hostWindow = useHostWindow()`
  (`vue-frontend/src/composables/detachedWindow/hostWindow.ts`), then `useEventListener(hostWindow, ...)`,
  `hostWindow.document`, `hostWindow.getSelection()` - never the global `window` / `document`.
- Menus and dialogs: `<Teleport :to="hostWindow.document.body">`, never `to="body"`.
- Helpers use the element's `ownerDocument` / `defaultView`; app-wide actions `activeHostWindow()` /
  `allHostWindows()`. A frame's `postMessage` reaches its own parent window.
- New window types go through the same page and `FluentChromeTabs.ScreenDocking` (dock, tile, never overlap).

