# Search Functionality Guidelines

## Overview

This guide covers TWO distinct search systems in the Zayit project:

1. **Library-Wide Search (SearchPage)** - Search across all books in the library using Bloom filter
2. **In-Book Search (BookLineViewer, BookCommentaryView)** - Search within a single book's content

---

## Library-Wide Search (SearchPage)

### Overview

The library-wide search allows users to search across all books in the library simultaneously using a Bloom filter search engine. Results are displayed in a dedicated SearchPage with virtualized scrolling.

### Multiple Search Tabs Support

Users can open multiple library-wide search tabs simultaneously, each with independent search queries and results.

**Key features:**

- Each search tab maintains its own query, results, and scroll position
- Tab titles display the search query (e.g., "חיפוש: בראשית")
- Long queries are truncated with CSS ellipsis automatically
- Search tabs persist across sessions
- Results loaded from cache (memory-efficient)
- Uses Bloom filter search engine for fast cross-library searching

### Tab Title Management

Library-wide search tab titles are dynamically updated:

```typescript
// When search is executed
const currentTab = tabStore.tabs.find(
  (t) => t.isActive && t.currentPage === "kezayit-search",
);
if (currentTab) {
  currentTab.title = `חיפוש: ${searchQuery.value}`;
}

// When search is cleared
if (currentTab) {
  currentTab.title = "חיפוש";
}

// On mount with saved query
if (currentSearchState.value.searchQuery.trim()) {
  currentTab.title = `חיפוש: ${currentSearchState.value.searchQuery}`;
}
```

### Memory Management

Library-wide search results are NOT stored in tab state to prevent memory bloat:

```typescript
// ❌ WRONG - Don't store results in tab state
currentSearchState.value.results = results.value;

// ✅ CORRECT - Load from cache
const cachedResults = await bloomSearchCacheService.get(normalizedQuery);
if (cachedResults !== null) {
  results.value = cachedResults;
}
```

**What IS stored in SearchState (library-wide search):**

- `searchQuery` - The search text
- `scrollPosition` - Legacy scroll position
- `firstVisibleItemIndex` - Virtual scroll position
- `hasSearched` - Whether search was executed
- `highlightTerms` - Terms to highlight when navigating to books
- `highlightSnippet` - Snippet for background highlighting

**What is NOT stored:**

- `results` - Loaded from cache on demand

### Persistence

Library-wide search tabs are persisted in `saveToStorage()`:

```typescript
const contentTabs = tabs.value.filter(
  (tab) =>
    tab.currentPage === "bookview" ||
    tab.currentPage === "pdfview" ||
    tab.currentPage === "hebrewbooks-view" ||
    tab.currentPage === "kezayit-search", // Library-wide search tabs persisted
);
```

On restore, SearchPage loads results from cache:

```typescript
if (hasSearched.value && searchQuery.value.trim()) {
  const normalizedQuery = searchQuery.value.trim().toLowerCase();
  const cachedResults = await bloomSearchCacheService.get(normalizedQuery);

  if (cachedResults !== null) {
    results.value = cachedResults;
    // Restore scroll position
    await nextTick();
    if (scrollerRef.value) {
      restoreScrollPosition();
    }
  } else {
    // Cache miss - re-execute search
    await executeSearch();
  }
}
```

---

## In-Book Search (BookLineViewer, BookCommentaryView)

### Architecture

The in-book search system allows searching within a single book's content using an overlay search bar.

### 🎯 **CRITICAL: Two Search Composables Architecture**

The in-book search system uses two separate composables for different content types:

#### useVirtualizedSearch (BookLineViewer)

**Location**: `src/composables/useVirtualizedSearch.ts`

**Purpose**: Search for virtualized content where only some lines are rendered in DOM

**Key Features**:

- Searches directly on bond data (all lines), not rendered DOM
- Simple match tracking: `{ lineIndex, matchIndex }`
- All matches highlighted, current match extra-highlighted
- DOM manipulation to update current mark without full re-render
- Conditional dash normalization (only if query contains spaces/dashes)
- Normalization order: lowercase → normalize dashes → remove diacritics

**Usage**:

```typescript
const search = useVirtualizedSearch();

// Search through all lines
await search.search(query, dataSource, totalLines);

// Navigate matches
search.nextMatch();
search.previousMatch();

// Highlight in HTML content
const highlighted = search.highlightInContent(htmlContent, isCurrentLine);
```

#### useContentSearch (BookCommentaryView)

**Location**: `src/composables/useContentSearch.ts`

**Purpose**: Search for non-virtualized or already-loaded content

**Key Features**:

- Searches through array of items with content
- Match tracking: `{ itemIndex, occurrence, totalInItem }`
- Same highlighting and normalization as virtualized search
- Simpler API for non-virtualized content

**Usage**:

```typescript
const search = useContentSearch();

// Search through items
search.searchInItems(items, query);

// Navigate to specific match
search.navigateToMatch(matchIndex);

// Highlight matches in HTML
const highlighted = search.highlightMatches(
  htmlContent,
  query,
  currentOccurrence,
);
```

### Search Source Principle

**✅ CORRECT**: Search operates on source data (bond data, items array)
**❌ WRONG**: Search operates on rendered DOM elements

### Why Two Composables?

1. **Virtualization**: BookLineViewer uses virtual scrolling, only some lines are in DOM
2. **Performance**: Searching source data is faster than DOM traversal
3. **Accuracy**: Searches actual content, not processed display text
4. **Flexibility**: Each composable optimized for its use case

### Core Components

#### GenericSearch Component (In-Book Search Overlay)

**Location**: `src/components/common/GenericSearch.vue`

**Purpose**: Overlay search bar for searching within a single book

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

#### In-Book Search Integration Components

**BookLineViewer** (`src/components/BookLineViewer.vue`):

- Searches through book text content using `useVirtualizedSearch`
- Highlights matches with `mark` elements
- Scrolls to search results with smooth centering
- Search bar opens with Ctrl+F

**BookCommentaryView** (`src/components/BookCommentaryView.vue`):

- Searches through commentary content using `useContentSearch`
- Groups-based navigation for complex content structure
- Same smooth centering behavior

### Search Result Navigation & Scrolling

#### Scroll Optimization: Only Scroll If Not In View

The search system uses smart scrolling that only scrolls when necessary:

```typescript
async function scrollToMatch(lineIndex: number) {
  // Check if the line is already in view
  const scrollerEl = scrollerRef.value?.$el;
  if (scrollerEl) {
    const lineEl = scrollerEl.querySelector(
      `[data-line-index-observer="${lineIndex}"]`,
    );

    if (lineEl) {
      // Line is rendered, check if it's in viewport
      const lineRect = lineEl.getBoundingClientRect();
      const scrollerRect = scrollerEl.getBoundingClientRect();
      const searchBarOffset = 50;

      const isInView =
        lineRect.top >= scrollerRect.top + searchBarOffset &&
        lineRect.bottom <= scrollerRect.bottom;

      if (!isInView) {
        // Only scroll if not in view
        await scrollToLine(lineIndex);
      }
    } else {
      // Line not rendered, need to scroll to it
      await scrollToLine(lineIndex);
    }
  }

  // Update current mark styling via DOM (avoid full re-render)
  updateCurrentMarkStyling();
}
```

#### DOM Manipulation for Current Mark

To prevent jumping during navigation in virtualized content, we use DOM manipulation instead of triggering full re-renders:

```typescript
function updateCurrentMarkStyling() {
  const scrollerEl = scrollerRef.value?.$el;
  if (!scrollerEl) return;

  const currentMatch = search.currentMatch.value;
  if (!currentMatch) return;

  // Remove 'current' class from all marks
  const allMarks = scrollerEl.querySelectorAll("mark.current");
  allMarks.forEach((mark: Element) => mark.classList.remove("current"));

  // Find the line element
  const lineEl = scrollerEl.querySelector(
    `[data-line-index-observer="${currentMatch.lineIndex}"]`,
  );
  if (!lineEl) return;

  // Find all marks in this line and add 'current' to the right one
  const marks = lineEl.querySelectorAll("mark");
  if (marks[currentMatch.matchIndex]) {
    marks[currentMatch.matchIndex].classList.add("current");
  }
}
```

#### Search Bar Overlay Handling

The search bar overlays the content, so we account for its height (50px) when scrolling:

```typescript
const searchBarOffset = 50;

// Check if mark is hidden behind search bar
if (markRect.top < scrollerRect.top + searchBarOffset) {
  // Scroll up a bit to clear the search bar
  scrollerEl.scrollTop -=
    scrollerRect.top + searchBarOffset - markRect.top + 10;
}
```

#### Implementation Pattern

1. **Check Visibility First**: Only scroll if match is not already visible
2. **Account for Overlay**: Add 50px offset for search bar
3. **DOM Manipulation**: Update current mark class without re-rendering
4. **Prevent Jumping**: Use navigation flag to prevent scroll tracking interference
5. **Priority Loading**: Ensure content around match is loaded before scrolling

## Search State Management

### Multiple Search Tabs Support

Users can open multiple search tabs simultaneously, each maintaining independent state:

- Each tab has its own search query displayed in the tab title
- Results are loaded from cache (not stored in tab state)
- Scroll positions are preserved per tab
- Search tabs persist across sessions

### Persistence Rules

**DO PERSIST** (in SearchState):

- `searchQuery` - Search text (also shown in tab title)
- `scrollPosition` - Legacy scroll position
- `firstVisibleItemIndex` - Virtual scroll position
- `hasSearched` - Whether search was executed
- `highlightTerms` - Terms for book navigation highlighting
- `highlightSnippet` - Snippet for background highlighting

**DO NOT PERSIST**:

- `isSearchOpen` - Search bar visibility state (in BookLineViewer)
- Search results - Loaded from cache on demand
- Current match index
- Filter state

### Tab Title Management (Library-Wide Search Only)

Library-wide search tab titles dynamically display the search query:

```typescript
// On search execution
const currentTab = tabStore.tabs.find(
  (t) => t.isActive && t.currentPage === "kezayit-search",
);
if (currentTab) {
  currentTab.title = `חיפוש: ${searchQuery.value}`;
}

// On search clear
if (currentTab) {
  currentTab.title = "חיפוש";
}

// On mount with saved query
if (currentSearchState.value.searchQuery.trim()) {
  currentTab.title = `חיפוש: ${currentSearchState.value.searchQuery}`;
}
```

Long queries are automatically truncated with CSS ellipsis in the TabHeader component.

### Memory-Efficient Result Loading (Library-Wide Search)

Library-wide search results are loaded from cache instead of being stored in tab state:

```typescript
// On mount or tab switch
if (hasSearched.value && searchQuery.value.trim()) {
  const normalizedQuery = searchQuery.value.trim().toLowerCase();
  const cachedResults = await bloomSearchCacheService.get(normalizedQuery);

  if (cachedResults !== null) {
    results.value = cachedResults;
    // Restore scroll position after results load
    await nextTick();
    if (scrollerRef.value) {
      restoreScrollPosition();
    }
  } else {
    // Cache miss - re-execute search
    await executeSearch();
  }
}
```

**Benefits:**

- Prevents memory bloat with multiple search tabs
- Fast restoration from cache
- Automatic re-search on cache miss

### Implementation in TabStore (Library-Wide Search)

Library-wide search tabs are persisted alongside other content tabs:

```typescript
const contentTabs = tabs.value.filter(
  (tab) =>
    tab.currentPage === "bookview" ||
    tab.currentPage === "pdfview" ||
    tab.currentPage === "hebrewbooks-view" ||
    tab.currentPage === "kezayit-search", // Search tabs included
);
```

Search state is cleaned before saving (results removed, in-book search state removed):

```typescript
// Results are NOT saved - only query and position
const cleanedSearchState = {
  searchQuery: tab.searchState.searchQuery,
  scrollPosition: tab.searchState.scrollPosition,
  firstVisibleItemIndex: tab.searchState.firstVisibleItemIndex,
  hasSearched: tab.searchState.hasSearched,
  highlightTerms: tab.searchState.highlightTerms,
  highlightSnippet: tab.searchState.highlightSnippet,
  // results field omitted
};
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

## Text Normalization

### Conditional Dash Normalization

The search system treats dashes as spaces, but ONLY if the query contains spaces or dashes:

```typescript
// Check if query contains spaces or dashes
const queryHasSpacesOrDashes = /[\s\u002D\u2013\u2014\u05BE]/.test(query);

// Normalize query - dashes BEFORE diacritics removal
let normalizedQuery = query.toLowerCase();
if (queryHasSpacesOrDashes) {
  normalizedQuery = normalizeDashes(normalizedQuery);
}
normalizedQuery = removeDiacritics(normalizedQuery);
```

**Why Conditional?**

- If user searches for "ברא" (no spaces), it should NOT match "בר־א"
- If user searches for "בר א" (with space), it SHOULD match "בר־א"
- This allows both exact and flexible searching

### Normalization Order

**CRITICAL**: Normalize dashes BEFORE removing diacritics:

```typescript
// ✅ CORRECT ORDER
let normalized = text.toLowerCase();
if (queryHasSpacesOrDashes) {
  normalized = normalizeDashes(normalized); // 1. Dashes first
}
normalized = removeDiacritics(normalized); // 2. Diacritics second

// ❌ WRONG ORDER
let normalized = text.toLowerCase();
normalized = removeDiacritics(normalized); // Wrong: diacritics first
normalized = normalizeDashes(normalized); // Wrong: dashes second
```

**Why Order Matters**: Diacritics can be on letters adjacent to dashes. If we remove diacritics first, we might lose important positional information.

### Dash Types Supported

The normalization handles all common dash types:

- Hyphen-minus: `-` (U+002D)
- En dash: `–` (U+2013)
- Em dash: `—` (U+2014)
- Maqaf (Hebrew): `־` (U+05BE)

```typescript
function normalizeDashes(text: string): string {
  // Replace all dash types with space
  return text.replace(/[\u002D\u2013\u2014\u05BE]/g, " ");
}
```

### Diacritics Removal

Hebrew diacritics (niqqud and cantillation marks) are removed for flexible searching:

```typescript
function removeDiacritics(text: string): string {
  // Remove Hebrew diacritics (U+0591-U+05C7)
  return text.replace(/[\u0591-\u05C7]/g, "");
}
```

## Search Highlighting

### CSS Classes

- `mark` - Basic search highlight (orange/amber background)
- `mark.current` - Currently selected match (darker orange, bold, higher opacity)

### Highlight Colors

Both BookLineViewer and BookCommentaryView use consistent orange/amber colors:

```css
mark {
  background-color: rgba(251, 191, 36, 0.3); /* Amber-400 at 30% */
  color: inherit;
}

mark.current {
  background-color: rgba(251, 191, 36, 0.6); /* Amber-400 at 60% */
  font-weight: bold;
}
```

### Content Processing

Search highlighting is applied during content processing:

```typescript
// For virtualized content (BookLineViewer)
if (query && linesWithMatches.has(i)) {
  const isCurrentLine = currentMatch?.lineIndex === i;
  processedContent = search.highlightInContent(processedContent, isCurrentLine);
}

// For non-virtualized content (BookCommentaryView)
const highlighted = search.highlightMatches(
  htmlContent,
  query,
  currentOccurrence,
);
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

1. **Use the right composable**: `useVirtualizedSearch` for virtualized content, `useContentSearch` for non-virtualized
2. **Search source data**: Always search bond data or items array, never DOM
3. **DOM manipulation for current mark**: Update styling without triggering re-renders
4. **Only scroll when needed**: Check if match is already visible before scrolling
5. **Account for overlays**: Add offset for search bar (50px)
6. **Conditional dash normalization**: Only normalize dashes if query contains spaces/dashes
7. **Correct normalization order**: Dashes before diacritics
8. **Use smooth animations** for better user experience
9. **Provide keyboard shortcuts** for power users

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
5. **Optimize highlighting**: Only highlight lines with matches

## Troubleshooting

### Common Issues

**Search bar persists across sessions**:

- Check tabStore persistence logic
- Ensure `isSearchOpen` is excluded from saved state

**Search results not visible (hidden behind search bar)**:

- Verify 50px offset is applied when scrolling
- Check that scroll calculations account for search bar height
- Ensure mark is scrolled into view with proper offset

**Search jumping during navigation**:

- Use DOM manipulation to update current mark class
- Set `isNavigating` flag to prevent scroll tracking interference
- Avoid triggering full re-renders when changing current match

**Search not working with dashes**:

- Verify conditional dash normalization is implemented
- Check that query is tested for spaces/dashes before normalizing
- Ensure normalization order: dashes before diacritics

**Search matching incorrectly with dashes**:

- Confirm query "ברא" (no spaces) does NOT match "בר־א"
- Confirm query "בר א" (with space) DOES match "בר־א"
- Check regex pattern: `/[\s\u002D\u2013\u2014\u05BE]/`

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
- [ ] Search results highlight correctly (orange/amber background)
- [ ] Current match has darker background and bold text
- [ ] Navigation between results works (Enter/Shift+Enter)
- [ ] Results only scroll if not already visible
- [ ] Search bar overlay doesn't hide results (50px offset)
- [ ] Search closes with Esc
- [ ] Search state doesn't persist across sessions
- [ ] Dash normalization works conditionally (only with spaces in query)
- [ ] Query "ברא" does NOT match "בר־א"
- [ ] Query "בר א" DOES match "בר־א"

### Performance

- [ ] Search input is debounced (no excessive queries)
- [ ] Large content searches don't freeze UI
- [ ] No jumping during navigation in virtualized content
- [ ] DOM manipulation updates current mark without re-render
- [ ] Memory usage is reasonable with many results

### Edge Cases

- [ ] Empty search query clears results
- [ ] No results found shows 0/0 counter
- [ ] Search works with special characters
- [ ] Search works with Hebrew text and diacritics
- [ ] Search works with all dash types (-, –, —, ־)
- [ ] Component cleanup prevents memory leaks
- [ ] Search works correctly after tab switching

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
