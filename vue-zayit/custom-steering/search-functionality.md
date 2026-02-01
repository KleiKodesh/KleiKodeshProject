# Search Functionality Guidelines

## Overview

This guide covers the search functionality implementation in the Zayit project, including the GenericSearch component, search result navigation, and persistence behavior.

## Search Architecture

### 🎯 **CRITICAL: Source Data Search Architecture**

#### Search Source Principle

**✅ CORRECT**: Search operates on source data (`viewerState.lines.value`)
**❌ WRONG**: Search operates on rendered DOM elements

#### Progressive Search Updates with Line Loading Integration

The search system is designed for the smart streaming line loading architecture:

```typescript
// Search only loaded lines (source data from streaming system)
async getSearchData(): Promise<Array<{ index: number, content: string }>> {
    const allLines: Array<{ index: number, content: string }> = []

    Object.entries(this.lines.value).forEach(([indexStr, content]) => {
        if (content && content !== '\u00A0') { // Exclude placeholders from streaming
            allLines.push({
                index: Number(indexStr),
                content // Raw source content, not rendered HTML
            })
        }
    })

    return allLines.sort((a, b) => a.index - b.index)
}

// Auto-update search results as new content streams in from line loading system
watch(() => Object.keys(viewerState.lines.value).length, () => {
    if (currentSearchQuery.value.trim()) {
        performSearch() // Re-search with newly loaded content
    }
})
```

#### Integration with Smart Streaming Line Loading

The search system integrates seamlessly with the line loading architecture:

1. **Instant Search**: Works immediately with placeholder scaffolding
2. **Progressive Results**: Expands as content streams in via priority loading
3. **Source Data Only**: Searches raw content from `BookLineViewerState.lines`
4. **Virtualization Ready**: Will work with future full virtualization
5. **Priority Loading**: Search triggers priority loading for result navigation

#### Why This Architecture Matters

1. **Virtualization Ready**: Works with large books where only visible content is loaded
2. **Progressive Results**: Search results expand as more content streams in
3. **Performance**: Searches raw text, not complex DOM structures
4. **Accuracy**: Searches actual content, not processed/filtered display text
5. **Future-Proof**: Ready for advanced virtualization and lazy loading
6. **Memory Efficient**: Only searches loaded content, not entire documents

### Core Components

#### GenericSearch Component

**Location**: `src/components/common/GenericSearch.vue`

**Features**:

- Debounced search input (300ms delay)
- Navigation controls (Next/Previous)
- Match counter display
- Keyboard shortcuts (Enter, Shift+Enter, Esc, Ctrl+F)
- Customizable positioning via `topOffset` prop

**Events Emitted**:

- `searchQueryChange` - When search query changes (debounced)
- `navigateToMatch` - When user navigates between matches
- `close` - When search is closed

#### Search Integration Components

**BookLineViewer** (`src/components/BookLineViewer.vue`):

- Searches through book text content
- Highlights matches with `mark` elements
- Scrolls to search results with smooth centering

**BookCommentaryView** (`src/components/BookCommentaryView.vue`):

- Searches through commentary content
- Groups-based navigation for complex content structure
- Same smooth centering behavior

### Search Result Navigation & Centering

#### Integration with Line Loading System

Search result navigation integrates with the priority loading system:

```javascript
function handleNavigateToMatch(matchIndex: number) {
    search.navigateToMatch(matchIndex)
    const match = search.currentMatch.value
    if (match) {
        // Priority load content around search result
        await viewerState.prioritizeLines(match.itemIndex, LOAD_BUFFER_SIZE)

        // Navigate to the content area first
        scrollToLine(match.itemIndex) // or scrollToGroup() for commentary

        // Wait for DOM render, then center the highlighted result
        setTimeout(() => {
            const currentMark = document.querySelector('.line-viewer mark.current')
            if (currentMark) {
                currentMark.scrollIntoView({ behavior: 'smooth', block: 'center' })
            }
        }, 100)
    }
}
```

#### Implementation Pattern

1. **Priority Loading**: Ensure content around search result is loaded
2. **Two-Phase Scrolling**: First navigate to content area, then fine-tune to highlight
3. **DOM Timing**: 100ms delay ensures proper rendering before centering
4. **Smooth Animation**: Use `behavior: 'smooth'` for better UX
5. **Perfect Centering**: Use `block: 'center'` for optimal positioning
6. **Graceful Fallback**: Check element exists before scrolling

## Search State Management

### Persistence Rules

**DO NOT PERSIST**:

- `isSearchOpen` - Search bar visibility state
- Search query text
- Current match index
- Search results

**PERSIST**:

- Book content and position
- User preferences
- Tab state (excluding search)

### Implementation in TabStore

```typescript
// Clean up book state - don't persist search state
if (tab.bookState) {
  cleanedTab.bookState = {
    ...tab.bookState,
    isSearchOpen: undefined, // Don't persist search bar state
  };
}
```

### Component State Patterns

**BookLineViewer** (Synced with tab state):

```typescript
const isSearchOpen = computed({
  get: () => myTab.value?.bookState?.isSearchOpen || false,
  set: (value) => {
    if (myTab.value?.bookState) {
      myTab.value.bookState.isSearchOpen = value;
    }
  },
});
```

**BookCommentaryView** (Local state):

```typescript
const isSearchOpen = ref(false); // Always starts closed
```

## Search Highlighting

### CSS Classes

- `.line-viewer mark` - Basic search highlight
- `.line-viewer mark.current` - Currently selected match
- `.commentary-content mark.current` - Commentary search highlight

### Content Processing

Search highlighting is applied during content processing:

```typescript
// Apply search highlighting
if (query) {
  const currentOccurrence =
    currentMatch?.itemIndex === lineIndex ? currentMatch.occurrence : -1;
  processedLine = search.highlightMatches(
    processedLine,
    query,
    currentOccurrence,
  );
}
```

## Keyboard Shortcuts

### Global Shortcuts

- **Ctrl+F / Cmd+F**: Open search bar
- **Esc**: Close search bar

### Search Bar Active Shortcuts

- **Enter**: Navigate to next match
- **Shift+Enter**: Navigate to previous match
- **Esc**: Close search

### Implementation

```typescript
function handleKeyDown(e: KeyboardEvent) {
  if ((e.ctrlKey || e.metaKey) && e.key === "f") {
    e.preventDefault();
    isSearchOpen.value = true;
    return;
  }
  // ... other shortcuts
}
```

## Search Performance

### Debouncing

- **Input Debounce**: 300ms delay on search query changes
- **Scroll Updates**: 300ms debounce on scroll position updates
- **DOM Timing**: 100ms delay for search result centering

### Content Loading

- Search works with virtualized content
- Automatically re-searches when new content loads
- Prioritizes loading around search results

## Best Practices

### Search Implementation

1. **Always use GenericSearch component** for consistent UI/UX
2. **Implement two-phase scrolling** for complex content
3. **Use smooth animations** for better user experience
4. **Handle DOM timing** with appropriate delays
5. **Provide keyboard shortcuts** for power users

### State Management

1. **Never persist search UI state** across sessions
2. **Use computed properties** for tab-synced state
3. **Use local refs** for component-specific search
4. **Clear search state** when closing
5. **Default to closed** on component initialization

### Performance

1. **Debounce search input** to avoid excessive queries
2. **Limit search results** to reasonable numbers (e.g., 100)
3. **Use efficient selectors** for DOM queries
4. **Clean up timeouts** in component lifecycle

## Troubleshooting

### Common Issues

**Search bar persists across sessions**:

- Check tabStore persistence logic
- Ensure `isSearchOpen` is excluded from saved state

**Search results not centering**:

- Verify `mark.current` selector exists
- Check DOM timing (increase setTimeout delay)
- Ensure `scrollIntoView` is called on correct element

**Search not working with new content**:

- Verify search re-triggers when content loads
- Check content processing includes search highlighting
- Ensure search data is updated with new content

**Keyboard shortcuts not working**:

- Check event.preventDefault() is called
- Verify component has focus/tabindex
- Ensure shortcuts don't conflict with browser defaults

## Testing Checklist

### Functionality

- [ ] Search opens with Ctrl+F
- [ ] Search input is focused and selected when opened
- [ ] Search results highlight correctly
- [ ] Navigation between results works (Enter/Shift+Enter)
- [ ] Results are smoothly centered in view
- [ ] Search closes with Esc
- [ ] Search state doesn't persist across sessions

### Performance

- [ ] Search input is debounced (no excessive queries)
- [ ] Large content searches don't freeze UI
- [ ] Smooth scrolling animations work properly
- [ ] Memory usage is reasonable with many results

### Edge Cases

- [ ] Empty search query clears results
- [ ] No results found shows 0/0 counter
- [ ] Search works with special characters
- [ ] Search works with Hebrew text and diacritics
- [ ] Component cleanup prevents memory leaks

## Session Accomplishments

### ✅ Completed in This Session

#### Search Result Centering Fix

- **Problem**: Search results were not properly centered in viewport
- **Solution**: Replaced complex manual geometry calculations with simple `scrollIntoView`
- **Components Fixed**: BookLineViewer.vue and BookCommentaryView.vue
- **Improvement**: Added smooth animation with `behavior: 'smooth', block: 'center'`
- **Code Reduction**: Simplified from 15+ lines to 1 line per component

#### Search State Persistence Fix

- **Problem**: Search bar state was persisting across browser sessions
- **Root Cause**: `isSearchOpen` was being saved in localStorage with tab state
- **Solution**: Modified `tabStore.ts` to exclude search state from persistence
- **Implementation**: Added explicit filtering in `saveToStorage()` function
- **Result**: Search bar now always starts closed in new sessions

#### Search Functionality Documentation

- **Created**: Comprehensive `search-functionality.md` steering guide
- **Coverage**: Architecture, navigation, state management, best practices
- **Patterns**: Documented two-phase scrolling and DOM timing patterns
- **Troubleshooting**: Added common issues and solutions guide
- **Testing**: Included complete testing checklist

#### Key Technical Improvements

1. **Simplified Centering**: Native browser API instead of manual calculations
2. **Clean State Management**: No unwanted UI state persistence
3. **Smooth UX**: Added animation for better user experience
4. **Reliable Timing**: Proper DOM rendering delays (100ms)
5. **Consistent Behavior**: Same implementation across all search contexts

#### Steering System Updates

- **Added**: Search functionality to automatic loading rules
- **Keywords**: search, navigate, highlight, match, scroll, center
- **Manual Key**: `#search` for direct access to search guidelines
- **Integration**: Seamlessly integrated with existing steering architecture

## 🎯 **CRITICAL ARCHITECTURAL NOTE**

### Source Data Search Requirement

**ALWAYS search source data, NEVER search DOM elements**

#### Current Implementation (✅ CORRECT)

```typescript
// BookLineViewer searches source data from viewerState
const allLines = await viewerState.getSearchData(); // Source data
search.searchInItems(allLines, query);

// Progressive updates as content streams in
watch(
  () => Object.keys(viewerState.lines.value).length,
  () => {
    if (currentSearchQuery.value.trim()) {
      performSearch(); // Re-search with newly loaded content
    }
  },
);
```

#### Why This Matters

1. **Virtualization**: Only some content may be in DOM at any time
2. **Progressive Loading**: Search results expand as more lines stream in
3. **Performance**: Raw text search is faster than DOM traversal
4. **Accuracy**: Searches actual content, not processed display text
5. **Future-Proof**: Ready for advanced virtualization strategies

#### Implementation Details

- **Source**: `viewerState.lines.value` (raw content)
- **Exclusions**: Placeholder lines (`\u00A0`) are filtered out
- **Updates**: Automatic re-search when `lines.value` changes
- **Timing**: Immediate search on source data, delayed DOM centering

This architecture ensures search works correctly with the streaming content system and will scale properly with future virtualization improvements.
