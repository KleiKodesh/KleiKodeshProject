# Footnotes and Highlighting Feature

This document describes the database schema and architecture for user annotations — highlights and footnotes — in the book viewer.

## Database Schema

The footnotes and highlighting data lives in a separate SQLite database:
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
| colorArgb   | INTEGER | ARGB color value as 32-bit integer (e.g., `0xFFFFFF00` for yellow) |
| createdAt   | INTEGER | UNIX timestamp in milliseconds                 |

**Key properties:**
- A highlight spans from `startOffset` to `endOffset` within a single line
- Multiple highlights can overlap on the same line
- `colorArgb` is stored as a 32-bit signed integer; decode as `0xAARRGGBB`
- All timestamps are in milliseconds since epoch (JavaScript-compatible)

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

### Storage in IDB

User annotations are **not** synced to the main app's IndexedDB (`app-tabs`, `app-lastread`, etc.). They remain in the user settings database (`user_settings.db`) on disk and are fetched fresh for each book view session.

**Rationale:** Highlights and notes are not per-tab or per-session state — they are user data that persists globally. Storing them in the user settings database separates them from app state and keeps them accessible across multiple app instances.

### Reactive State in Vue

Highlights and notes for the currently viewed book are loaded into a composable store on book mount:

- `useBookViewHighlights.ts` — manages highlights: fetch, add, update, delete, queries
- `useBookViewNotes.ts` — manages notes: fetch, add, update, delete, queries

Each composable:
- Fetches its data on mount using the current `bookId` from `tabStore.activeTab`
- Maintains a reactive `highlights` / `notes` array
- Provides methods to mutate annotations (create, update, delete)
- Automatically syncs changes back to the user settings database
- Clears its state when the book changes

### Rendering

Highlights and notes are rendered by the main text component `BookViewLinesContent.vue`:

- As the virtual scroller renders each line, it queries both highlight and note composables for annotations on that line
- Highlights are rendered as `<mark>` elements with inline `background-color` set from `colorArgb`
- Notes are rendered as a small indicator icon next to the line or as a tooltip/popup on hover/tap
- Clicking a note opens a sidebar or modal with the full note text and metadata (created/updated timestamps)

### Querying

Both composables expose query methods:

- `getHighlightsOnLine(lineId)` — returns all highlights on a line, sorted by `startOffset`
- `getNoteOnLine(lineId)` — returns the note (if any) on a line; a line may have at most one note
- `getHighlightColor(id)` — decode ARGB color, return as CSS color string

## Interaction Patterns

### Creating a Highlight

1. User selects text in the line content (native text selection)
2. A context menu appears with color swatches
3. User picks a color
4. `useBookViewHighlights.createHighlight({ bookId, lineId, startOffset, endOffset, colorArgb })` is called
5. The highlight is inserted into `user_highlights`, added to the local `highlights` array, and rendered immediately

### Creating a Note

1. User selects text in the line content
2. A context menu appears with a "Add note" option
3. User taps it; a note-entry dialog opens with the selected text pre-filled in `quote`
4. User types their annotation in the `note` field
5. `useBookViewNotes.createNote({ bookId, lineId, startOffset, endOffset, note, quote })` is called
6. The note is inserted into `user_notes`, added to the local `notes` array, and a small indicator appears on the line

### Editing a Note

1. User taps a note indicator on a line
2. A modal opens showing the note text, timestamps, and the original quote
3. User edits the text
4. `useBookViewNotes.updateNote(id, { note })` is called
5. The note is updated in `user_notes` and the local array, and `updatedAt` is refreshed

### Deleting an Annotation

1. User taps an annotation (highlight or note)
2. A "Delete" option appears (via context menu or in the edit modal)
3. `useBookViewHighlights.deleteHighlight(id)` or `useBookViewNotes.deleteNote(id)` is called
4. The annotation is removed from `user_highlights` / `user_notes` and from the local array
5. The rendered indicator/markup is immediately removed from the line

## Color Encoding

Colors are stored as 32-bit signed integers in ARGB format.

**Example:** Yellow highlight `0xFFFFFF00`
- Alpha: `FF` (fully opaque)
- Red: `FF` (255)
- Green: `FF` (255)
- Blue: `00` (0)

**JavaScript conversion:**

```typescript
// Encode: RGB object → ARGB integer
function colorToArgb(color: { r: number, g: number, b: number }, alpha: number = 255): number {
  const a = Math.max(0, Math.min(255, alpha));
  const r = Math.max(0, Math.min(255, color.r));
  const g = Math.max(0, Math.min(255, color.g));
  const b = Math.max(0, Math.min(255, color.b));
  // Use >>> 0 to convert to unsigned 32-bit (bitwise OR with 0 is also safe but less explicit)
  return ((a << 24) | (r << 16) | (g << 8) | b) >>> 0;
}

// Decode: ARGB integer → CSS color string
function argbToCssColor(argb: number): string {
  const a = ((argb >>> 24) & 0xFF) / 255;
  const r = (argb >>> 16) & 0xFF;
  const g = (argb >>> 8) & 0xFF;
  const b = argb & 0xFF;
  return `rgba(${r}, ${g}, ${b}, ${a})`;
}
```

## Supported Highlight Colors

From Zayit's source code (`HighlightColors` object in Kotlin), confirmed against real entries in `user_settings.db`.

Colors are stored as **signed 32-bit integers** in SQLite (Java/Kotlin's `Int` type is signed). The unsigned ARGB bit pattern must be reinterpreted: `unsigned = signed & 0xFFFFFFFF`.

| Color Name | Signed (SQLite) | Unsigned ARGB  | CSS                    |
| ---------- | --------------- | -------------- | ---------------------- |
| Yellow     | `-5317`         | `0xFFFFEB3B`   | `rgb(255, 235, 59)`    |
| Green      | `-11751600`     | `0xFF4CAF50`   | `rgb(76, 175, 80)`     |
| Blue       | `-14575885`     | `0xFF2196F3`   | `rgb(33, 150, 243)`    |
| Pink       | `-1499549`      | `0xFFE91E63`   | `rgb(233, 30, 99)`     |
| Orange     | `-26624`        | `0xFFFF9800`   | `rgb(255, 152, 0)`     |

**Critical — signed integer handling:**
When reading `colorArgb` from SQLite in JavaScript, the value will be a negative number. Always convert to unsigned before extracting channels:

```typescript
function argbToCssColor(signedArgb: number): string {
    const unsigned = signedArgb >>> 0;  // reinterpret signed int as unsigned 32-bit
    const a = ((unsigned >>> 24) & 0xFF) / 255;
    const r = (unsigned >>> 16) & 0xFF;
    const g = (unsigned >>> 8) & 0xFF;
    const b = unsigned & 0xFF;
    return `rgba(${r}, ${g}, ${b}, ${a})`;
}
```

When writing a color from the frontend to SQLite, store the signed value (JavaScript's bitwise OR converts to signed 32-bit automatically):

```typescript
// e.g. Yellow: 0xFFFFEB3B as unsigned → -5317 as signed
const signedArgb = colorUnsigned | 0;  // converts unsigned 32-bit to signed Int32
```

**Notes:**
- All colors use full opacity (`FF` alpha channel)
- Colors are from the Material Design palette

**Color Palette in Frontend:**

Define a constants object to mirror Zayit's palette:

```typescript
const HIGHLIGHT_COLORS = {
  YELLOW: 0xFFFFEB3B,
  GREEN: 0xFF4CAF50,
  BLUE: 0xFF2196F3,
  PINK: 0xFFE91E63,
  ORANGE: 0xFFFF9800,
};

const HIGHLIGHT_COLORS_LIST = [
  HIGHLIGHT_COLORS.YELLOW,
  HIGHLIGHT_COLORS.GREEN,
  HIGHLIGHT_COLORS.BLUE,
  HIGHLIGHT_COLORS.PINK,
  HIGHLIGHT_COLORS.ORANGE,
];
```

**Color Selection UI:**
The color picker displays these five color swatches in a horizontal or grid layout. User taps a color swatch to apply it to the selected text range.

## Performance Considerations

- Highlights and notes are fetched once per book per session — do not re-fetch unless the user explicitly refreshes
- Keep the in-memory arrays indexed by `lineId` for O(1) lookup during line rendering
- For books with thousands of highlights, batch-load in chunks if needed to avoid UI lag
- Always keep the user settings database queries indexed on `(bookId, lineId)` for fast lookups

## Future Extensions

- Sync highlights/notes to cloud (add `isSynced` boolean, sync timestamp)
- Export highlights/notes as markdown or PDF
- Search across all user notes globally
- Tags and collections for organizing highlights/notes
- Sharing annotations with other users
