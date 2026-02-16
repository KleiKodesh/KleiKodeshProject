# Virtualized Search Requirements

## User Story

As a user reading Hebrew texts, I want to search within virtualized content so I can quickly find specific text passages.

## Acceptance Criteria

### 1. Search Execution

- Type in search box → finds all matches in data source (not just rendered items)
- Zero-copy: searches data source directly without intermediate arrays
- **Substring matching**: Finds query anywhere within words (e.g., "ברא" matches both "בָּרָ֣א" and "בְּרֵאשִׁ֖ית")
- **Handles HTML tags in words**: Tags in the middle of words don't break matching (e.g., `<b>ב</b>ראשית` matches "ברא")
- Matching policy (preserves original positions for highlighting):
  - Extract text content from HTML (ignore tags, but keep Hebrew text in angle brackets like <עם טקסט עברית>)
  - Find matches in plain text first, then map back to HTML nodes for highlighting
  - Ignore nikkud/cantillation marks during matching (treat as if they don't exist)
  - Treat maqaf/dashes as whitespace during matching (match across them)
  - Case-insensitive matching
  - No text normalization - work directly on original content to preserve offsets
- Shows match count (e.g., "3/15")
- No auto-scroll while typing (debounced at 300ms)
- Fast and non-blocking (no UI freeze during search)
- **Initial selection**: Component determines first visible match based on rendered items
- **Alt TOC support**: Lines with alt TOC entries are searchable and navigable

### 2. Navigation

- Search finds all matches but does NOT auto-select
- Component calls `selectFirstVisibleMatch(visibleItemIndices)` to select first visible match
- If no visible matches, selects first match overall
- **Navigation logic**: Simple sequential advancement through matches array
- Next/Previous wraps around (last → first, first → last)
- Scrolling handled by composable using virtual scroller

### 3. Viewport Behavior

- **Scroll implementation**:
  1. Call component's `onScrollToItem` callback to prioritize loading (if needed)
  2. Use virtual scroller's `scrollToItem(itemIndex)` to bring item into view
  3. Wait for DOM update with `nextTick()`
  4. Apply 30px offset to account for search bar: `scrollTop -= 30`
  5. Hide overflow during scroll to prevent visual jumping
- Works correctly with alt TOC entries (variable line heights)
- Timing: 100ms navigation flag duration to prevent scroll tracking interference

### 4. Visual Feedback

- All matches: amber/orange with 30% opacity (#f59e0b light mode, #fbbf24 dark mode)
- Current match: amber/orange with 80% opacity + bold (#f59e0b light mode, #fbbf24 dark mode)
- Highlights applied in data layer via Vue reactivity (computed properties)
- No DOM manipulation for highlights - Vue handles updates efficiently

### 5. Universal Architecture

- **Single composable**: `useVirtualizedSearch` (merged from core + UI layers)
- Works for BookLineViewer (Record<number, string>)
- Works for BookCommentaryView (complex array objects with getContent function)
- Configurable via options:
  - `scrollerRef`: Reference to virtual scroller
  - `itemSelector`: CSS selector for items
  - `itemIndexAttribute`: Attribute containing item index
  - `totalItems`: Computed total item count
  - `onScrollToItem`: Callback for prioritizing item loading (e.g., BookLineViewer needs to load lines)
- Zero code duplication across components

### 6. User Experience

- **Close behavior**: X button and ESC key both close search
- **Search bar coverage**: Results never covered by search bar (30px offset)
- **Smooth navigation**: No visual jumping during scroll
- **Alt TOC handling**: Scrolls to actual match (mark element), not line container

### 7. CommentaryView Specifics

- **Group headers are searchable**: Both group headers and link content are included in search
- **Correct virtual item indexing**: Each group header and link gets a unique index in the flattened array
- **Highlighting in main content**: Group headers rendered with `v-html` to display `<mark>` tags
- **Plain text in combobox**: Dropdown uses original `linkGroups` without HTML highlighting
- **Index mapping**: Build map of virtual item indices for proper highlighting coordination

## Implementation Details

### Position Mapping for Highlighting

- Uses `mapNormalizedToOriginal` function to map positions from normalized text back to original text
- Correctly handles diacritics (nikkud/cantillation) by skipping them during position counting
- **Critical**: Excludes maqaf (־ U+05BE) from diacritic range - it's a dash character, not a diacritic
- Diacritic ranges: 0x0591-0x05BD (cantillation) and 0x05BF-0x05C7 (nikkud), excluding 0x05BE (maqaf)
- Ensures highlighted text includes all diacritics within the matched range
- Preserves exact character positions for accurate highlighting

### Scroll Implementation

```typescript
async function scrollToMatch(itemIndex: number): Promise<void> {
  isNavigating.value = true;

  try {
    // Call component callback to prioritize loading (if needed)
    await onScrollToItem(itemIndex);

    // Hide overflow to prevent visual jumping
    const originalOverflow = scrollerEl.style.overflow;
    scrollerEl.style.overflow = "hidden";

    // Scroll to item using virtual scroller
    scrollerRef.value.scrollToItem(itemIndex);

    // Wait for DOM update
    await nextTick();

    // Apply offset for search bar
    scrollerEl.scrollTop = scrollerEl.scrollTop - 30;

    // Restore overflow
    scrollerEl.style.overflow = originalOverflow;
  } finally {
    // Keep flag true for 100ms to prevent scroll tracking interference
    setTimeout(() => {
      isNavigating.value = false;
    }, 100);
  }
}
```

### Composable Architecture

- **Core search state**: searchQuery, matches, currentMatchIndex, totalMatches, currentMatch
- **Core search methods**: performSearch, selectFirstVisibleMatch, navigateToMatch, nextMatch, previousMatch, highlightMatches, clear
- **UI state**: isSearchOpen, isNavigating
- **UI methods**: handleSearch, handleSearchNext, handleSearchPrevious, openSearch, handleSearchClose, scrollToMatch
- **Viewport helpers**: getVisibleItemIndices

### CommentaryView Implementation

```typescript
// Build index map for proper highlighting
const processedLinkGroups = computed(() => {
  let virtualItemIndex = 0;
  const itemIndexMap = new Map<string, number>();

  // Map all items (headers + links)
  linkGroups.value.forEach((group, groupIndex) => {
    itemIndexMap.set(`group-header-${groupIndex}`, virtualItemIndex++);
    group.links.forEach((link, linkIndex) => {
      itemIndexMap.set(
        `group-${groupIndex}-link-${linkIndex}`,
        virtualItemIndex++,
      );
    });
  });

  // Apply highlighting with correct indices
  return linkGroups.value.map((group, groupIndex) => {
    const headerIndex = itemIndexMap.get(`group-header-${groupIndex}`);
    const processedGroupName =
      query && headerIndex !== undefined
        ? highlightMatches(group.groupName, headerIndex)
        : group.groupName;

    return {
      ...group,
      groupName: processedGroupName,
      links: group.links.map((link, linkIndex) => {
        const linkIndex = itemIndexMap.get(
          `group-${groupIndex}-link-${linkIndex}`,
        );
        const html =
          query && linkIndex !== undefined
            ? highlightMatches(link.html, linkIndex)
            : link.html;
        return { ...link, html };
      }),
    };
  });
});

// Combobox uses original linkGroups (no HTML)
const groupOptions = computed(() => {
  return linkGroups.value.map((group, index) => ({
    label: group.groupName, // Plain text
    value: index,
  }));
});
```

### Template Rendering

```vue
<!-- Group header with HTML highlighting -->
<div v-if="item.type === 'group-header'" v-html="item.groupName">
</div>

<!-- Link with HTML highlighting -->
<div v-else-if="item.type === 'link'" v-html="item.html">
</div>
```
