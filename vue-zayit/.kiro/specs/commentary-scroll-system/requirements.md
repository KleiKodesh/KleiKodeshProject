# Commentary Scroll System Requirements

## Overview

Simplify BookCommentaryView scrolling: composable handles persistence, scroll observer tracks current commentary, overrides only for first load and line navigation.

## User Stories

### 1. Scroll position persists automatically

- Scroll position saved per tab + book + filter combination
- Restored on filter change, tab switch, page reload
- Each tab maintains independent scroll positions for the same book

### 2. Default commentator shown on first load

- When opening book with no saved position, show default commentator (book.defaultCommentatorBookId)
- Scroll to it and select in combobox
- Set default book ID in tab state

### 3. Combobox reflects current view

- As user scrolls, combobox updates to show commentary at viewport center

### 4. Line navigation stays on same commentary

- Next/previous line uses default book ID (kept current by scroll observer)
- Overrides saved scroll position
- Falls back to first commentary if not found

### 5. Manual navigation works

- Combobox selection scrolls to selected commentary
- Next/previous group buttons scroll to adjacent commentary
- Temporarily disables scroll observer during navigation, re-enables after

## Technical Design

### State

- Default book ID in tab state (updated by scroll observer = current commentary, used for line navigation)
- Scroll position in localStorage (handled by composable)
- `skipRestore` ref (passed to composable, set true during line navigation to prevent restore)
- Remove: `commentaryPositionsByFilter`, `isLoadingFromNavigation`, `pendingNavigationTargetBookId`

### Override Logic

1. **Line navigation**: Set `skipRestore = true`, scroll to default book ID on new line, then `skipRestore = false`

### Scroll Observer

- Updates combobox based on viewport center
- Updates default book ID in tab state (viewport center = current = default)
- Controlled by `allowScrollTracking` flag (disabled during overrides)

### Composable Changes

- Accept optional `skipRestore` ref parameter
- Check `skipRestore.value` before auto-restoring
- If true, skip restore and reset to false

## Success Criteria

- [x] First load shows default commentator
- [x] Scroll persists per filter (each filter has independent scroll position)
- [x] Scroll persists across tabs (same book in different tabs)
- [x] Scroll persists across page reloads
- [x] Line navigation stays on same commentary
- [x] Combobox reflects viewport center (except during default commentator load or line navigation, which set to default book ID)
- [x] Combobox selection scrolls to commentary
- [x] Next/previous group buttons work
