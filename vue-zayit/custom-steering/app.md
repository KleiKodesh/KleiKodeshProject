# Application Architecture Guidelines

## Table of Contents (TOC) System

### Dual Display Modes

The TOC system supports two distinct display modes based on user interaction:

#### Full-Width Mode (First Open)

- **Trigger**: First time opening TOC for a book session
- **Behavior**:
  - Takes entire screen width with solid background
  - Closes automatically after selecting an item
  - Used for initial navigation to book content

#### Compact Mode (Subsequent Opens)

- **Trigger**: After closing and reopening TOC
- **Behavior**:
  - Auto-width sizing (200px min, 500px max) based on content
  - Pinned to right side of BookView page
  - Semi-transparent background (95% opacity)
  - Stays open after selecting items for continuous navigation
  - Click outside to close functionality

### State Management

#### TOC Visibility State

```typescript
interface BookState {
  isTocOpen?: boolean; // Whether TOC overlay is open
  isFirstTocOpen?: boolean; // Whether this is first time opening (for display mode)
}
```

#### State Transitions

- `openBookToc()`: Sets `isFirstTocOpen = true` if undefined, `isTocOpen = true`
- `closeToc()`: Sets `isTocOpen = false`, `isFirstTocOpen = false` (marks as no longer first time)

### TOC Structure and Ordering

#### Entry Types

- **Regular TOC**: Main book structure (chapters, sections, etc.)
- **Alt TOC**: Alternative organizational structure ("חלוקה נוספת")

#### Display Order

1. **"חלוקה נוספת"** (Alternative Division) - First, collapsed by default
2. **First regular TOC root item** - Expanded by default
3. **Other regular TOC items** - Collapsed by default

### User Interface Components

#### Search Integration

- **Embedded reset button**: Inside search input with rounded corners (6px)
- **Auto-focus**: Search input automatically focused on TOC open/toggle
- **Keyboard shortcuts**:
  - `Escape`: Clear search (if text exists) or close TOC
  - `Arrow keys`: Navigate to first tree item

#### Styling Specifications

- **Compact mode positioning**: `position: absolute, right: 0, height: 100%`
- **Border**: Left border only (`border-left: 1px solid rgba(0, 0, 0, 0.1)`)
- **No rounded corners**: Clean, sharp edges
- **Subtle shadow**: Left side only for definition

### Implementation Patterns

#### Component Hierarchy

```
BookViewPage
├── BookTocTreeView (main container)
    ├── BookTocTreeSearch (search results)
    └── BookTocTree (tree display)
        └── BookTocTreeNode (individual items)
```

#### Props Flow

- `isCompactMode`: Passed down from BookViewPage based on `!isFirstTocOpen`
- Compact styling applied conditionally throughout component tree

#### Event Handling

- **Selection behavior**: Different per mode (close vs stay open)
- **Click-off**: Only active in compact mode
- **Global escape**: Works regardless of focus state

### TOC Data Processing

#### Entry Building (tocBuilder.ts)

```typescript
// Alt TOC entries added first (unshift)
tree.unshift(altRootNode); // "חלוקה נוספת" at top

// First regular entry expanded by default
if (regularTree.length > 0) {
  regularTree[0].isExpanded = true;
}
```

#### Title Localization

- Alt TOC section: "חלוקה נוספת" (Alternative Division)
- Menu toggle: "הצג/הסתר כותרות נוספות" (Show/Hide Additional Headers)

## State Management

### Component State Ownership

- Each component owns its local state
- Parent components control child layout, not children themselves
- State flows down via props, events flow up via emits
- Use stores for shared state across components

### Persistence Patterns

- Save state on user actions (scroll, tab switch, close)
- Restore state on mount/activation
- Use debouncing for frequent events (300ms for scroll)
- Immediate save on critical events (unmount, tab close)

## Large Data Loading

### Progressive Loading Strategy

1. **Render placeholders first** - Show structure immediately
2. **Load visible content** - Prioritize what user sees
3. **Background load rest** - Non-blocking, can be aborted
4. **Buffer optimization** - Pre-load data without blocking UI

### Performance Principles

- No complex calculations - Keep logic simple
- Always responsive - UI never blocks
- Progressive enhancement - Start minimal, add more
- Abort on navigation - Cancel unnecessary work

### Loading States

- **Initial load**: Placeholders only
- **Restore**: Load saved position + surrounding content
- **User action**: Load target + surrounding content, apply buffer

## Tab Management

### Tab Lifecycle

- Reuse lowest available ID when creating tabs
- Each tab has isolated component instance (use unique keys)
- Clean up resources on tab close
- Preserve state when switching between tabs

### Component Keys

Use composite keys for proper isolation:

```vue
:key="`${tabId}-${pageType}`"
```

### Homepage Navigation

- Use centralized `navigateToHomepage()` function for all default tab creation
- Never duplicate connectivity checking logic across multiple functions
- All tab creation functions (addTab, resetTab, closeAllTabs, etc.) should call the centralized function
- Pattern: Return both `pageType` and `title` together to prevent mismatches

```typescript
// Good: Centralized homepage logic
async function navigateToHomepage(): Promise<{
  pageType: PageType;
  title: string;
}> {
  const isOnline = await checkConnectivity();
  const pageType: PageType = isOnline ? "homepage" : "kezayit-landing";
  return { pageType, title: PAGE_TITLES[pageType] };
}

// Good: Functions use centralized logic
const addTab = async () => {
  const { pageType, title } = await navigateToHomepage();
  // ... create tab with pageType and title
};

// Bad: Duplicating connectivity logic
const addTab = async () => {
  const isOnline = await checkConnectivity();
  const pageType = isOnline ? "homepage" : "kezayit-landing";
  // ... duplicated logic
};
```

## Data Layer

### Separation of Concerns

- **Router layer**: Environment detection and routing only
- **Implementation layer**: Actual data operations
- **Query layer**: SQL templates (single source of truth)

### Rules

- All SQL in query templates file
- No SQL in routing or implementation layers
- Components use router only, never direct implementation
- No code duplication across layers

## Commentary View Scroll Tracking

### Section Structure Requirements

Each commentary group MUST be wrapped in a single `<section>` tag containing:

- The commentary header/title
- ALL associated links for that commentary

```vue
<section class="commentary-section" :data-group-index="groupIndex">
  <!-- Header -->
  <div class="group-header">{{ groupName }}</div>
  
  <!-- All links for this group -->
  <div v-for="link in links" class="link-item">
    {{ link.html }}
  </div>
</section>
```

### Scroll-Based Detection Logic

The scroll tracking uses direct position checking on scroll events (not intersection observer):

#### Scrolling UP

- Find the section with the **smallest distance to viewport top**
- This is the section closest to the top of the visible area
- Uses `Math.abs(rect.top - scrollerTop)` to find minimum distance

#### Scrolling DOWN

- Find the section with the **largest `top` value** that's still visible
- This is the section closest to the bottom of the visible area
- Tracks `rect.top > bestTop` to find the bottommost visible section

#### No Direction

- Defaults to section closest to viewport top
- Same logic as scrolling up

#### Performance

```typescript
// Debounced at 16ms (~1 frame at 60fps) for responsive updates
scrollTimeout = window.setTimeout(() => {
  updateCurrentSection();
}, 16);
```

### State Separation

- `currentGroupIndex`: Tracks scroll position (updated by scroll handler)
- `comboboxSelectedValue`: Separate value for combobox display/editing
- `isNavigating`: Flag to prevent scroll tracking during programmatic navigation
- `scrollDirection`: Tracks 'up', 'down', or 'none' based on scroll position changes

### Critical Rules

1. **Section tags must wrap complete groups** - Both header and all links
2. **Detection must be on section tags** - Not on individual headers or links
3. **Direction awareness is mandatory** - Different logic for up vs down scrolling
4. **No circular updates** - Separate variables prevent watcher loops
5. **Fast response time** - 16ms debounce for smooth, responsive updates during fast scrolling

### Search Navigation

Search navigation in virtualized commentary follows the same pattern as BookLineViewer:

1. **Set navigation flag** - `isNavigating.value = true` to prevent scroll tracking interference
2. **Scroll to virtual item** - `scrollToItem(match.itemIndex)` to render the item
3. **Wait for render** - 150ms timeout to ensure virtualization has rendered
4. **Fine-tune position** - Query scroller element for `mark.current` and use `scrollIntoView`
5. **Re-enable tracking** - Reset `isNavigating` after 100ms

```typescript
function handleNavigateToMatch(matchIndex: number) {
  search.navigateToMatch(matchIndex);
  const match = search.currentMatch.value;
  if (match && commentaryScrollerRef.value) {
    isNavigating.value = true;
    commentaryScrollerRef.value.scrollToItem(match.itemIndex);

    setTimeout(() => {
      const scrollerEl = commentaryScrollerRef.value?.$el;
      const currentMark = scrollerEl?.querySelector("mark.current");
      if (currentMark) {
        currentMark.scrollIntoView({ behavior: "auto", block: "center" });
      }
      setTimeout(() => {
        isNavigating.value = false;
      }, 100);
    }, 150);
  }
}
```

### Persistence

Commentary positions are persisted per filter in tab state:

```typescript
commentaryPositionsByFilter: {
  [filterKey: string]: {
    groupIndex: number,
    targetBookId?: number,
    scrollPosition: number
  }
}
```

- Saved on scroll and group index changes
- Restored when loading commentary or switching filters
- Persists across tab switches and sessions

### Line Navigation with Commentary Continuity

The next/previous line buttons maintain commentary context for continuous reading:

**Flow:**

1. User views a specific commentary (e.g., רש"י with targetBookId 5545)
2. Clicks "שורה הבאה" (next line)
3. System scans up to 20 lines ahead to find next line with same commentary
4. Sets `pendingNavigationTargetBookId` to remember which commentary
5. Navigates to that line
6. When new line loads, finds and scrolls to the same commentary group

**Implementation:**

```typescript
// In navigation functions
pendingNavigationTargetBookId.value = currentTarget;
emit("update:selectedLineIndex", found);

// In loadCommentaryLinks
if (scrollToTargetBookId !== undefined) {
  const targetGroupIndex = linkGroups.value.findIndex(
    (g) => g.targetBookId === scrollToTargetBookId,
  );
  if (targetGroupIndex >= 0) {
    scrollToGroup(targetGroupIndex);
  }
}
```

This enables reading one commentary continuously across multiple lines without losing position.

### Virtual Scroller Configuration

**Critical**: Set `min-item-size` to a realistic value based on typical item height:

```typescript
const commentaryMinItemSize = computed(() => 80);
```

- Commentary sections typically include: header (~40-50px) + links (~20-30px) + padding (24px)
- Too small a value (e.g., 32px) causes first item to wrap/overlap behind itself
- Virtual scroller uses this as initial estimate before measuring actual height
- Prevents rendering issues on initial load

## Documentation

### Keep It Minimal

- One doc file per feature/module maximum
- Prefer inline comments and JSDoc
- Only create docs when explicitly needed
- Update existing docs, don't create new ones

### When to Document

1. Complex architecture needs explanation
2. Integration with external systems
3. User explicitly requests it

**Don't create**: SUMMARY.md, MIGRATION.md, VERIFICATION.md, CLEANUP.md, etc.
