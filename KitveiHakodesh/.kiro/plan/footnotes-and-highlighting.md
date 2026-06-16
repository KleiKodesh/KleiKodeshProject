# User Annotations: Highlights and Notes

This document describes the database schema and architecture for user annotations — highlights and notes — in the book viewer. **Highlights are fully implemented.** This section focuses on notes, which follow the same architecture.

## Database Schema

The annotations data lives in a separate SQLite database:
**Location:** `{UserAppData}/io.github.kdroidfilter.seforimapp/databases/settings/user_settings.db`

This database is user-scoped and persists across app sessions. All access must go through `src/webview-host/userSettingsDb.ts`.

### user_highlights table

Stores user-applied text highlights with color.

| column      | type    | notes                                          |
| ----------- | ------- | ---------------------------------------------- |
| id          | INTEGER | PK, auto-increment                             |
| bookId      | INTEGER | FK to main seforim database `book.id`          |
| lineId      | INTEGER | FK to main seforim database `line.id`          |
| startOffset | INTEGER | character offset within the line where highlight begins |
| endOffset   | INTEGER | character offset within the line where highlight ends   |
| colorArgb   | INTEGER | ARGB color value as 32-bit signed integer (e.g., `-5317` for yellow) |
| createdAt   | INTEGER | UNIX timestamp in milliseconds                 |

**Status:** ✅ Fully implemented. Highlights sync correctly between book view and commentary view.

### user_notes table

Stores user-written footnotes/annotations tied to a text range.

| column     | type    | notes                                      |
| ---------- | ------- | ------------------------------------------ |
| id         | INTEGER | PK, auto-increment                         |
| bookId     | INTEGER | FK to main seforim database `book.id`      |
| lineId     | INTEGER | FK to main seforim database `line.id`      |
| startOffset | INTEGER | character offset within the line           |
| endOffset   | INTEGER | character offset within the line           |
| note       | TEXT    | user-written annotation text               |
| quote      | TEXT    | the selected text from the book (snapshot) |
| createdAt  | INTEGER | UNIX timestamp in milliseconds             |
| updatedAt  | INTEGER | UNIX timestamp in milliseconds             |

**Key properties:**
- A note is always tied to a text range on a specific line
- `quote` field contains a snapshot of the selected text at creation time (for display if the line content changes)
- `note` field is the user's written annotation — can be any length, any language (Hebrew, English, mixed)
- Multiple notes can exist on the same line or overlap
- `updatedAt` reflects the last time the note text was modified

## Architecture

### Access Layer

All user-settings database access goes through `src/webview-host/userSettingsDb.ts`:

- `queryUserSettings<T>(sql, params)` — execute SQL against the user settings database
- Similar to the main seforim database access via `db.ts`, but for a separate IDB instance

All raw SQL strings for the user settings database live in `src/webview-host/userSettingsDb.sql.ts` — never inline SQL.

### Highlights Implementation

Highlights are fully implemented across both the book view and commentary view:

**Book View:**
- `src/features/book-view/lines/useBookViewHighlights.ts` — manages highlights for a single book, called by `BookViewLinesContent.vue`
- Loads highlights for `tabStore.activeTab.bookId` on mount
- Context menu (via `BookViewAnnotationMenuRow`) allows apply/clear highlights with color selection

**Commentary View:**
- `src/features/book-view/commentary/useCommentaryHighlights.ts` — manages highlights for multiple commentary books simultaneously
- Watches `props.groups` and lazily loads highlights per commentary book
- Routes apply/clear operations to the correct commentary book's id so they persist correctly
- Highlights applied in commentary sync correctly to the book viewer and vice versa

**Rendering:**
- Highlights are rendered as `<mark class="user-highlight">` with inline background color from `highlightColorToThemeColor`
- `applyUserHighlights` in `useBookViewLineRenderer.ts` handles HTML-aware, diacritic-aware injection of mark tags
- Search highlights render on top of user highlights
- Both book view and commentary view use the same rendering pipeline

### Notes (To Be Implemented)

User notes follow the same pattern as highlights but require additional UI for note creation/editing:

- A composable to manage notes per book (similar to `useBookViewHighlights`)
- Context menu extension to add "Add note" option (with modal/dialog for note text entry)
- Note indicators rendered next to lines (icon or small badge)
- Click-to-edit modal that displays note text, timestamps, and allow editing/deletion

## Interaction Patterns

### Highlights (Complete)

Highlights work in both book view and commentary view. Select text → context menu → pick color → highlight applied and persisted.

### Notes (To Implement)

1. User selects text in the line content
2. A context menu appears with a "Add note" option
3. User taps it; a note-entry dialog opens with the selected text pre-filled in `quote`
4. User types their annotation in the `note` field
5. `useBookViewNotes.createNote({ bookId, lineId, startOffset, endOffset, note, quote })` is called
6. The note is inserted into `user_notes`, added to the local `notes` array, and a small indicator appears on the line
7. User can click the note indicator to open an edit/view modal
8. Clicking "Delete" removes the note from the database and UI

## Performance Considerations

- Annotations are fetched once per book per session — do not re-fetch unless the user explicitly refreshes
- Keep the in-memory maps indexed by `lineId` for O(1) lookup during line rendering
- Always keep the user settings database queries indexed on `(bookId, lineId)` for fast lookups
- Commentary highlights use lazy loading: only the currently visible commentary books are loaded

## Future Extensions

- Sync highlights/notes to cloud (add `isSynced` boolean, sync timestamp)
- Export highlights/notes as markdown or PDF
- Search across all user notes globally
- Tags and collections for organizing highlights/notes
- Sharing annotations with other users

