---
inclusion: fileMatch
fileMatchPattern: '**/BookCommentaryView.vue|**/commentaryManager*'
---

# Zayit Commentary System

## CRITICAL: Commentary Persistence by targetBookId

The commentary pane maintains user position across line selections and sessions using `targetBookId` matching, NOT index-based persistence.

### Why targetBookId?
Each commentary group (Rashi, Targum, etc.) has a `targetBookId` that uniquely identifies the commentary source book. This is more reliable than:
- Group name (can vary in formatting)
- Group index (can change based on what commentaries are available for each line)

## Commentary State Persistence

### BookState Properties
```typescript
interface BookState {
    selectedLineIndex?: number;           // Currently selected line for commentary
    commentaryGroupIndex?: number;        // Current group index (for fallback)
    commentaryTargetBookId?: number;      // targetBookId of current commentary (primary)
}
```

### Persistence Priority (loadCommentaryLinks)
When loading commentaries for a new line:

1. **Same Session Navigation** - Check current `targetBookId` before loading
   - If same commentary exists in new line, stay on it
   
2. **Session Restoration** - Check saved `commentaryTargetBookId`
   - Find matching `targetBookId` in loaded commentaries
   
3. **Index Fallback** - Use saved `commentaryGroupIndex` if valid
   
4. **Default** - Fall back to group 0

### Implementation Pattern
```typescript
async function loadCommentaryLinks(bookId: number, lineIndex: number) {
    // 1. Capture current targetBookId BEFORE loading
    const currentTargetBookId = linkGroups.value[currentGroupIndex.value]?.targetBookId
    
    // 2. Load new commentaries
    linkGroups.value = await commentaryManager.loadCommentaryLinks(...)
    
    // 3. Try to match by targetBookId (same session)
    if (currentTargetBookId !== undefined) {
        const matchingIndex = linkGroups.value.findIndex(
            group => group.targetBookId === currentTargetBookId
        )
        if (matchingIndex !== -1) {
            currentGroupIndex.value = matchingIndex
            // CRITICAL: Scroll to show restored commentary
            setTimeout(() => scrollToGroup(matchingIndex), 100)
            return
        }
    }
    
    // 4. Try saved targetBookId (session restoration)
    const savedTargetBookId = tabStore.activeTab?.bookState?.commentaryTargetBookId
    if (savedTargetBookId !== undefined) {
        const matchingIndex = linkGroups.value.findIndex(
            group => group.targetBookId === savedTargetBookId
        )
        if (matchingIndex !== -1) {
            currentGroupIndex.value = matchingIndex
            // CRITICAL: Scroll to show restored commentary
            setTimeout(() => scrollToGroup(matchingIndex), 100)
            return
        }
    }
    
    // 5. Fallback to saved index or 0
}
```

### Saving State
```typescript
function saveGroupIndexToTab() {
    const activeTab = tabStore.activeTab
    if (activeTab?.bookState) {
        // Save BOTH index and targetBookId
        activeTab.bookState.commentaryGroupIndex = currentGroupIndex.value
        const currentGroup = linkGroups.value[currentGroupIndex.value]
        activeTab.bookState.commentaryTargetBookId = currentGroup?.targetBookId
    }
}
```

## User Experience

### Within Session
- User views "Rashi" on line 5
- User clicks line 6
- If line 6 has "Rashi", stays on it (even if at different index)
- If line 6 doesn't have "Rashi", falls back to first group

### Across Sessions
- User closes app while viewing "Rashi" on line 5
- User reopens app
- Tab restores with line 5 and "Rashi" commentary
- User navigates to line 6
- If line 6 has "Rashi", stays on it

### Tab Switching
- User has multiple tabs with different books
- Each tab remembers its commentary position independently
- Switching tabs restores the correct commentary for each book

## Commentary Sorting

### Database Query
```sql
ORDER BY bk.title  -- Pure alphabetical sorting by book title
```

**Previous Issue**: The original query `ORDER BY connectionTypeId, bk.title` was designed to group by connection type first, but since most commentaries had the same `connectionTypeId=2`, it appeared to be pure alphabetical sorting anyway.

**Solution**: Changed to pure alphabetical sorting for consistency and user expectation.

## Critical Implementation Details

### Variable Declaration Order
```typescript
// CRITICAL: Declare reactive variables BEFORE any functions that use them
const currentGroupIndex = ref(0)
const linkGroups = ref<CommentaryLinkGroup[]>([])

// Then define functions that use these variables
async function loadCommentaryLinks() {
    // Can safely access currentGroupIndex here
}
```

### Scrolling After Restoration
```typescript
// CRITICAL: Always scroll to show restored commentary
setTimeout(() => scrollToGroup(matchingIndex), 100)
```
The 100ms delay ensures DOM is updated before scrolling.

## Common Mistakes

❌ **DON'T** reset `currentGroupIndex` to 0 on every `linkGroups` change
```typescript
// WRONG - This breaks persistence
watch(() => linkGroups.value, () => {
    currentGroupIndex.value = 0
})
```

❌ **DON'T** rely only on index for persistence
```typescript
// WRONG - Index can point to different commentary after reload
currentGroupIndex.value = savedGroupIndex
```

❌ **DON'T** forget to scroll after restoration
```typescript
// WRONG - User won't see the restored commentary
currentGroupIndex.value = matchingIndex
// Missing: setTimeout(() => scrollToGroup(matchingIndex), 100)
```

✅ **DO** match by `targetBookId` first, then fall back to index
```typescript
// CORRECT - Find same commentary by ID
const matchingIndex = linkGroups.value.findIndex(
    group => group.targetBookId === savedTargetBookId
)
```

✅ **DO** scroll to show restored commentary
```typescript
// CORRECT - User sees the restored position
currentGroupIndex.value = matchingIndex
setTimeout(() => scrollToGroup(matchingIndex), 100)
```

## Debugging Tips

### Add Logging for Troubleshooting
```typescript
console.log(`[Commentary] Saved targetBookId from tab: ${savedTargetBookId}`)
console.log(`[Commentary] Restored to saved commentary at index ${matchingIndex}`)
console.log(`[Commentary] Saved to tab: groupIndex=${currentGroupIndex.value}, targetBookId=${currentGroup?.targetBookId}`)
```

### Common Issues
- **"Cannot access before initialization"** → Check variable declaration order
- **Restoration not working** → Check if `targetBookId` exists in new commentary set
- **User doesn't see restored commentary** → Add scrolling after restoration
- **Pure alphabetical sorting unexpected** → Check database `connectionTypeId` distribution

## Data Structure

### CommentaryLinkGroup
```typescript
interface CommentaryLinkGroup {
    groupName: string;              // Display name (e.g., "רש\"י")
    targetBookId?: number;          // Source book ID (e.g., 123 for Rashi)
    targetLineIndex?: number;       // Target line for navigation
    links: Array<{
        text: string;
        html: string;
    }>;
}
```

### Database Schema
Commentary connections are stored in the `Connections` table with:
- `SourceBookId` - The book being read
- `SourceLineId` - The line being read
- `TargetBookId` - The commentary source (this is what we persist)
- `TargetLineId` - The commentary line
- `ConnectionTypeId` - Type of connection (commentary, targum, etc.)

## Session Restoration Flow

1. **App Startup** → Tab store loads from localStorage
2. **Component Mount** → BookCommentaryView receives props
3. **Commentary Load** → Checks saved `commentaryTargetBookId`
4. **Restoration** → Finds matching commentary by `targetBookId`
5. **UI Update** → Scrolls to show restored commentary
6. **User Interaction** → Saves new position for next session
