# Line Loading & Virtualization Architecture

## Overview

The Zayit project uses **vue3-virtual-scroller** for efficient virtualization combined with smart streaming architecture for loading book content. This system provides instant navigation while efficiently managing memory and database access through intelligent batching and priority-based loading.

## Current Architecture: Vue3-Virtual-Scroller + Smart Streaming

The system combines **vue3-virtual-scroller** for DOM virtualization with **smart streaming** for data loading:

### **Virtualization Characteristics**

- **DOM Rendering**: Only visible lines rendered (vue3-virtual-scroller)
- **Data Loading**: Smart streaming with priority-based batch loading
- **Search**: Progressive source data search (virtualization-compatible)
- **Memory**: Efficient (only visible DOM + loaded content buffer)
- **Navigation**: Reliable double-scroll hack for accurate positioning
- **Performance**: Handles thousands of lines without performance issues

## Vue3-Virtual-Scroller Implementation

### **Core Components**

**BookLineViewer** (`src/components/BookLineViewer.vue`) - **Virtualized UI Component**:

```vue
<template>
  <DynamicScroller
    ref="scrollerRef"
    class="scroller height-fill line-viewer"
    :items="virtualItems"
    :min-item-size="minItemSize"
    :buffer="300"
    key-field="index"
    @scroll.passive="handleScrollForPositionTracking"
  >
    <template #default="{ item, index, active }">
      <DynamicScrollerItem
        :item="item"
        :active="active"
        :size-dependencies="[
          item.content,
          myTab?.bookState?.isLineDisplayInline,
          myTab?.bookState?.showAltToc,
        ]"
      >
        <BookLine
          :content="item.content || '\u00A0'"
          :line-index="index"
          :data-line-index-observer="index"
        />
      </DynamicScrollerItem>
    </template>
  </DynamicScroller>
</template>
```

**BookCommentaryView** (`src/components/BookCommentaryView.vue`) - **Virtualized Commentary Component**:

```vue
<template>
  <DynamicScroller
    ref="commentaryScrollerRef"
    class="commentary-scroller height-fill"
    :items="virtualCommentaryItems"
    :min-item-size="commentaryMinItemSize"
    :buffer="100"
    key-field="id"
    @scroll.passive="handleCommentaryVirtualScroll"
  >
    <template #default="{ item, index, active }">
      <DynamicScrollerItem
        :item="item"
        :active="active"
        :size-dependencies="[item.content, item.type]"
        :data-commentary-item-observer="item.id"
      >
        <!-- Group Header -->
        <div v-if="item.type === 'group'" class="bold group-header">
          {{ item.content }}
        </div>
        <!-- Commentary Link -->
        <div
          v-else-if="item.type === 'link'"
          class="link-item"
          v-html="item.content"
        ></div>
      </DynamicScrollerItem>
    </template>
  </DynamicScroller>
</template>
```

**Key Configuration**:

- `minItemSize`: Conservative estimates (24px inline, 40px block for lines; 32px for commentary)
- `buffer`: 300 lines for main viewer, 100 for commentary
- `size-dependencies`: Content, display mode, TOC visibility for lines; content and type for commentary
- `data-line-index-observer` / `data-commentary-item-observer`: Essential for position tracking

### **Reliable Navigation System**

**Double-Scroll Hack for Accurate Positioning**:

```typescript
async function scrollToLine(lineIndex: number) {
  if (!scrollerRef.value) return;

  // Prioritize loading lines around target
  await viewerState.prioritizeLines(lineIndex, 100);
  await nextTick();

  // Hide scrolling during double-call to prevent flickering
  const scrollerEl = scrollerRef.value.$el;
  if (scrollerEl) {
    scrollerEl.style.overflow = "hidden";
    scrollerEl.style.pointerEvents = "none";
  }

  try {
    // First call (may be inaccurate due to dynamic heights)
    scrollerRef.value.scrollToItem(lineIndex);

    // Second call after 50ms (corrects position)
    setTimeout(() => {
      if (scrollerRef.value) {
        scrollerRef.value.scrollToItem(lineIndex);

        // Re-enable UI after completion
        setTimeout(() => {
          if (scrollerEl) {
            scrollerEl.style.overflow = "";
            scrollerEl.style.pointerEvents = "";
          }
        }, 10);
      }
    }, 50);
  } catch (error) {
    // Fallback with same double-call pattern
    const scrollTop = lineIndex * minItemSize.value;
    if (scrollerEl) {
      scrollerEl.scrollTop = scrollTop;
      setTimeout(() => {
        if (scrollerEl) {
          scrollerEl.scrollTop = scrollTop;
          scrollerEl.style.overflow = "";
          scrollerEl.style.pointerEvents = "";
        }
      }, 50);
    }
  }
}
```

**Why Double-Scroll Works**:

1. **First Call**: Gets approximately to target (virtual scroller estimates)
2. **50ms Delay**: Allows virtual scroller to render and calculate actual sizes
3. **Second Call**: Corrects position based on actual rendered content
4. **Anti-Flicker**: UI hidden during adjustment prevents visible jumps

### **Accurate Position Tracking**

**DOM-Based Line Detection** (not scroll position estimation):

```typescript
function getTopVisibleLineIndex(): number | null {
  if (!scrollerRef.value?.$el) return null;

  const scrollerEl = scrollerRef.value.$el;
  const scrollerRect = scrollerEl.getBoundingClientRect();
  const lineElements = scrollerEl.querySelectorAll(
    "[data-line-index-observer]",
  );

  let topMostLine: { element: Element; lineIndex: number; top: number } | null =
    null;

  for (const lineEl of lineElements) {
    const lineRect = lineEl.getBoundingClientRect();

    // Check if line is visible in viewport
    if (
      lineRect.bottom > scrollerRect.top &&
      lineRect.top < scrollerRect.bottom
    ) {
      const lineIndex = parseInt(
        lineEl.getAttribute("data-line-index-observer") || "-1",
      );
      if (lineIndex >= 0) {
        // Find line closest to top of viewport
        if (!topMostLine || lineRect.top < topMostLine.top) {
          topMostLine = { element: lineEl, lineIndex, top: lineRect.top };
        }
      }
    }
  }

  // Validate it's actually at the top (prevent "one line down" bug)
  if (topMostLine) {
    const distanceFromTop = topMostLine.top - scrollerRect.top;
    if (distanceFromTop >= -10 && distanceFromTop <= 50) {
      return topMostLine.lineIndex;
    }
  }

  return null;
}
```

**Position Tracking with Navigation Protection**:

```typescript
function handleScrollForPositionTracking() {
  if (!scrollerRef.value || viewerState.isInitialLoad) return;

  scrollUpdateTimeout = setTimeout(() => {
    // Skip if currently navigating (search, TOC, restoration)
    if (isNavigating.value) return;

    // Get actual top visible line ID from DOM
    const topVisibleLineIndex = getTopVisibleLineIndex();

    if (topVisibleLineIndex !== null) {
      // Save to tab state for restoration
      if (myTab.value?.bookState) {
        myTab.value.bookState.initialLineIndex = topVisibleLineIndex;
      }
    }
  }, 500);
}
```

### **Navigation Integration**

**All Navigation Uses Same Reliable Method**:

```typescript
// TOC Navigation (BookLineViewer)
async function handleTocSelection(lineIndex: number) {
  isNavigating.value = true;
  await viewerState.handleTocSelection(lineIndex);
  await nextTick();
  await scrollToLine(lineIndex); // Uses double-scroll hack
  setTimeout(() => {
    isNavigating.value = false;
  }, 200);
}

// Search Navigation (BookLineViewer)
function handleNavigateToMatch(matchIndex: number) {
  search.navigateToMatch(matchIndex);
  const match = search.currentMatch.value;
  if (match) {
    isNavigating.value = true;
    scrollToLine(match.itemIndex); // Uses double-scroll hack
    setTimeout(() => {
      const currentMark = document.querySelector(".line-viewer mark.current");
      if (currentMark) {
        currentMark.scrollIntoView({ behavior: "auto", block: "center" });
      }
      setTimeout(() => {
        isNavigating.value = false;
      }, 100);
    }, 150);
  }
}

// Commentary Group Navigation (BookCommentaryView)
const scrollToGroup = (index: number) => {
  if (!commentaryScrollerRef.value) return;

  const groupItemId = `group-${index}`;
  const itemIndex = virtualCommentaryItems.value.findIndex(
    (item) => item.id === groupItemId,
  );

  if (itemIndex !== -1) {
    isNavigating.value = true;

    // Use double-scroll hack with anti-flicker
    const scrollerEl = commentaryScrollerRef.value.$el;
    if (scrollerEl) {
      scrollerEl.style.overflow = "hidden";
      scrollerEl.style.pointerEvents = "none";
    }

    commentaryScrollerRef.value.scrollToItem(itemIndex);

    setTimeout(() => {
      if (commentaryScrollerRef.value) {
        commentaryScrollerRef.value.scrollToItem(itemIndex);

        setTimeout(() => {
          if (scrollerEl) {
            scrollerEl.style.overflow = "";
            scrollerEl.style.pointerEvents = "";
          }
          setTimeout(() => {
            isNavigating.value = false;
          }, 50);
        }, 10);
      }
    }, 50);
  }
};

// Commentary Search Navigation (BookCommentaryView)
function handleNavigateToMatch(matchIndex: number) {
  search.navigateToMatch(matchIndex);
  const match = search.currentMatch.value;
  if (match && commentaryScrollerRef.value) {
    isNavigating.value = true;

    commentaryScrollerRef.value.scrollToItem(match.itemIndex);

    setTimeout(() => {
      const currentMark = document.querySelector(
        ".commentary-content mark.current",
      );
      if (currentMark) {
        currentMark.scrollIntoView({ behavior: "auto", block: "center" });
      }

      setTimeout(() => {
        isNavigating.value = false;
      }, 100);
    }, 100);
  }
}

// Session/Tab Restoration (BookLineViewer)
watch(
  () => myTab.value?.bookState?.bookId,
  async (bookId, oldBookId) => {
    if (bookId && bookId !== oldBookId) {
      const initialLineIndex = myTab.value?.bookState?.initialLineIndex;
      if (initialLineIndex !== undefined) {
        isNavigating.value = true;
        setTimeout(async () => {
          await scrollToLine(initialLineIndex); // Uses double-scroll hack
          setTimeout(() => {
            isNavigating.value = false;
          }, 200);
        }, 100);
      }
    }
  },
);
```

## Smart Streaming Data Layer

### **BookLineViewerState** (`src/data/bookLineViewerState.ts`)

**Primary State Manager** for all line loading operations:

```typescript
class BookLineViewerState {
  // Public reactive state - single source of truth
  lines: Ref<Record<number, string>> = ref({}); // Currently loaded lines
  totalLines: Ref<number> = ref(0); // Total book length
  isInitialLoad = true; // Loading state flag

  // Private streaming state
  private bookId: number | null = null; // Current book
  private streamingAbort: (() => void) | null; // Cleanup function
  private pendingBatches: Set<number>; // Loading batches
  private priorityQueue: number[]; // Batch priority queue
}
```

**Key Methods**:

- `loadBook()` - Initialize book with instant scaffolding + start streaming
- `prioritizeLines()` - Priority load around specific line (TOC, scroll-to)
- `getSearchData()` - Return loaded lines for search (virtualization-compatible)
- `cleanup()` - Resource cleanup and abort streaming

### **Virtual Items Integration**

**BookLineViewer Virtual Items**:

```typescript
const virtualItems = computed(() => {
  const items = [];
  const lines = viewerState.lines.value;
  const diacriticsState = myTab.value?.bookState?.diacriticsState;
  const query = search.searchQuery.value;
  const currentMatch = search.currentMatch.value;

  for (let i = 0; i < viewerState.totalLines.value; i++) {
    const line = lines[i];
    let processedContent = line || "\u00A0";

    // Apply diacritics filtering
    if (
      processedContent !== "\u00A0" &&
      diacriticsState &&
      diacriticsState > 0
    ) {
      processedContent = applyDiacriticsFilter(
        processedContent,
        diacriticsState,
      );
    }

    // Apply search highlighting
    if (processedContent !== "\u00A0" && query) {
      const currentOccurrence =
        currentMatch?.itemIndex === i ? currentMatch.occurrence : -1;
      processedContent = search.highlightMatches(
        processedContent,
        query,
        currentOccurrence,
      );
    }

    items.push({
      index: i,
      content: processedContent,
      altTocEntries: props.altTocByLineIndex?.get(i),
    });
  }

  return items;
});
```

**BookCommentaryView Virtual Items**:

```typescript
const virtualCommentaryItems = computed(() => {
  const items: Array<{
    id: string;
    type: "group" | "link";
    content: string;
    groupIndex?: number;
    targetBookId?: number;
    targetLineIndex?: number;
  }> = [];

  processedLinkGroups.value.forEach((group, groupIndex) => {
    // Add group header
    items.push({
      id: `group-${groupIndex}`,
      type: "group",
      content: group.groupName,
      groupIndex: groupIndex,
      targetBookId: group.targetBookId,
      targetLineIndex: group.targetLineIndex,
    });

    // Add group links
    group.links.forEach((link, linkIndex) => {
      items.push({
        id: `group-${groupIndex}-link-${linkIndex}`,
        type: "link",
        content: link.html,
      });
    });
  });

  return items;
});
```

## Implementation Best Practices

### **Essential Configuration**

1. **Package Installation**:

```bash
npm install vue3-virtual-scroller
```

2. **Global Registration** (`src/main.ts`):

```typescript
import { DynamicScroller, DynamicScrollerItem } from "vue3-virtual-scroller";
import "vue3-virtual-scroller/dist/vue3-virtual-scroller.css";

app.component("DynamicScroller", DynamicScroller);
app.component("DynamicScrollerItem", DynamicScrollerItem);
```

3. **TypeScript Definitions** (`src/types/vue3-virtual-scroller.d.ts`):

```typescript
declare module "vue3-virtual-scroller" {
  import { DefineComponent } from "vue";

  interface DynamicScrollerMethods {
    scrollToItem: (index: number) => void;
    scrollToBottom: () => void;
    getScroll: () => { start: number; end: number };
  }

  export const DynamicScroller: DefineComponent<
    {
      items: any[];
      minItemSize: number;
      buffer?: number;
      keyField?: string;
    },
    DynamicScrollerMethods
  >;

  export const DynamicScrollerItem: DefineComponent<{
    item: any;
    active: boolean;
    sizeDependencies?: any[];
  }>;
}
```

### **Critical Implementation Details**

1. **Navigation Flag Protection**:

```typescript
const isNavigating = ref(false);

// Set during any programmatic navigation
isNavigating.value = true;
// ... perform navigation
setTimeout(() => {
  isNavigating.value = false;
}, 200);

// Check in scroll tracking (both components)
if (isNavigating.value) return; // Skip tracking during navigation
```

2. **Conservative minItemSize**:

```typescript
// BookLineViewer
const minItemSize = computed(() => {
  const isInline = myTab.value?.bookState?.isLineDisplayInline || false;
  return isInline ? 24 : 40; // Conservative to prevent height miscalculation
});

// BookCommentaryView
const commentaryMinItemSize = computed(() => 32); // Conservative for commentary items
```

3. **Essential Data Attributes**:

```vue
<!-- BookLineViewer -->
<DynamicScrollerItem :data-index="index"
                     :data-line-index-observer="index">

<!-- BookCommentaryView -->
<DynamicScrollerItem :data-commentary-item-observer="item.id">
```

4. **Proper Size Dependencies**:

```vue
<!-- BookLineViewer -->
:size-dependencies="[ item.content, myTab?.bookState?.isLineDisplayInline,
myTab?.bookState?.showAltToc ]"

<!-- BookCommentaryView -->
:size-dependencies="[ item.content, item.type ]"
```

5. **Anti-Flicker Double-Scroll Implementation**:

```typescript
// Hide UI during double-scroll to prevent flickering
const scrollerEl = scrollerRef.value.$el;
if (scrollerEl) {
  scrollerEl.style.overflow = "hidden";
  scrollerEl.style.pointerEvents = "none";
}

// First scroll call
scrollerRef.value.scrollToItem(targetIndex);

// Second scroll call after delay
setTimeout(() => {
  if (scrollerRef.value) {
    scrollerRef.value.scrollToItem(targetIndex);

    // Re-enable UI
    setTimeout(() => {
      if (scrollerEl) {
        scrollerEl.style.overflow = "";
        scrollerEl.style.pointerEvents = "";
      }
    }, 10);
  }
}, 50);
```

## Troubleshooting

### **Common Issues & Solutions**

**Issue: Lines overlapping or incorrect positioning**

- **Cause**: `minItemSize` too large or size dependencies missing
- **Solution**: Use conservative `minItemSize`, include all size-affecting dependencies

**Issue: Search navigation jumps back to start**

- **Cause**: Scroll position tracking interfering with navigation
- **Solution**: Use `isNavigating` flag to disable tracking during navigation

**Issue: Session restoration not working**

- **Cause**: Using scroll position estimation instead of actual line IDs
- **Solution**: Use DOM-based line detection with `data-line-index-observer`

**Issue: Limited scroll range**

- **Cause**: Virtual scroller miscalculating total height
- **Solution**: Reduce `minItemSize`, increase `buffer`, check size dependencies

### **Performance Optimization**

1. **Buffer Size**: 300 lines provides good balance of performance vs memory
2. **Size Dependencies**: Only include properties that actually affect line height
3. **Navigation Protection**: Always use `isNavigating` flag during programmatic scrolls
4. **Batch Loading**: Maintain smart streaming for data efficiency

## Migration Guide

### **From Non-Virtualized to Vue3-Virtual-Scroller**

**BookLineViewer Migration**:

1. **Replace v-for with DynamicScroller**:

```vue
<!-- Before -->
<BookLine v-for="index in totalLines" :key="index - 1" />

<!-- After -->
<DynamicScroller :items="virtualItems">
  <template #default="{ item, index, active }">
    <DynamicScrollerItem :item="item" :active="active">
      <BookLine :content="item.content" :line-index="index" />
    </DynamicScrollerItem>
  </template>
</DynamicScroller>
```

**BookCommentaryView Migration**:

1. **Replace nested v-for with DynamicScroller**:

```vue
<!-- Before -->
<div v-for="(group, groupIndex) in linkGroups" :key="groupIndex">
  <div class="group-header">{{ group.groupName }}</div>
  <div v-for="(link, linkIndex) in group.links" :key="linkIndex"
       v-html="link.html"></div>
</div>

<!-- After -->
<DynamicScroller :items="virtualCommentaryItems">
  <template #default="{ item, index, active }">
    <DynamicScrollerItem :item="item" :active="active">
      <div v-if="item.type === 'group'" class="group-header">
        {{ item.content }}
      </div>
      <div v-else-if="item.type === 'link'" v-html="item.content"></div>
    </DynamicScrollerItem>
  </template>
</DynamicScroller>
```

2. **Update Navigation Methods**:

```typescript
// Replace direct scrollIntoView with scrollToLine/scrollToGroup
// Both handle double-scroll hack and virtualization
await scrollToLine(targetLineIndex); // BookLineViewer
scrollToGroup(targetGroupIndex); // BookCommentaryView
```

3. **Add Position Tracking**:

```typescript
// Replace scroll position math with DOM-based detection (BookLineViewer)
const topLine = getTopVisibleLineIndex(); // Not Math.floor(scrollTop / itemSize)

// Use group index tracking for commentary (BookCommentaryView)
const handleCommentaryVirtualScroll = () => {
  // Find active group based on DOM elements, not scroll position
  const groupElements = scrollerEl.querySelectorAll(
    '[data-commentary-item-observer^="group-"]',
  );
  // ... determine active group from visible elements
};
```

4. **Add Navigation Protection**:

```typescript
// Wrap all programmatic navigation (both components)
isNavigating.value = true;
await scrollToLine(index); // or scrollToGroup(index)
setTimeout(() => {
  isNavigating.value = false;
}, 200);
```

5. **Flatten Data Structure (Commentary Only)**:

```typescript
// Transform nested groups+links into flat virtual items
const virtualCommentaryItems = computed(() => {
  const items = [];

  processedLinkGroups.value.forEach((group, groupIndex) => {
    // Add group header as virtual item
    items.push({
      id: `group-${groupIndex}`,
      type: "group",
      content: group.groupName,
      groupIndex,
    });

    // Add each link as virtual item
    group.links.forEach((link, linkIndex) => {
      items.push({
        id: `group-${groupIndex}-link-${linkIndex}`,
        type: "link",
        content: link.html,
      });
    });
  });

  return items;
});
```

This architecture provides reliable virtualization for large documents while maintaining all existing functionality including search, TOC navigation, and session restoration.

## Commentary Pane Virtualization Patterns

### **Flattening Nested Data Structures**

Commentary data comes as nested groups with links, but virtual scrollers need flat arrays:

```typescript
// Original nested structure
interface CommentaryLinkGroup {
  groupName: string;
  targetBookId?: number;
  targetLineIndex?: number;
  links: Array<{ html: string }>;
}

// Flattened for virtualization
interface VirtualCommentaryItem {
  id: string; // Unique identifier
  type: "group" | "link"; // Item type for rendering
  content: string; // Display content
  groupIndex?: number; // Original group index
  targetBookId?: number; // For navigation
  targetLineIndex?: number; // For navigation
}

const virtualCommentaryItems = computed(() => {
  const items: VirtualCommentaryItem[] = [];

  processedLinkGroups.value.forEach((group, groupIndex) => {
    // Add group header
    items.push({
      id: `group-${groupIndex}`,
      type: "group",
      content: group.groupName,
      groupIndex,
      targetBookId: group.targetBookId,
      targetLineIndex: group.targetLineIndex,
    });

    // Add group links
    group.links.forEach((link, linkIndex) => {
      items.push({
        id: `group-${groupIndex}-link-${linkIndex}`,
        type: "link",
        content: link.html,
      });
    });
  });

  return items;
});
```

### **Group-Based Navigation**

Commentary navigation works by group index, not scroll position:

```typescript
// Find virtual item index for a group
const scrollToGroup = (groupIndex: number) => {
  const groupItemId = `group-${groupIndex}`;
  const itemIndex = virtualCommentaryItems.value.findIndex(
    (item) => item.id === groupItemId,
  );

  if (itemIndex !== -1) {
    // Use same double-scroll pattern as BookLineViewer
    isNavigating.value = true;
    commentaryScrollerRef.value.scrollToItem(itemIndex);
    // ... double-scroll implementation
  }
};

// Track active group from DOM elements
const handleCommentaryVirtualScroll = () => {
  if (isNavigating.value) return; // Skip during navigation

  const groupElements = scrollerEl.querySelectorAll(
    '[data-commentary-item-observer^="group-"]:not([data-commentary-item-observer*="-link-"])',
  );

  let activeIndex = 0;
  for (const groupEl of groupElements) {
    const groupRect = groupEl.getBoundingClientRect();
    if (groupRect.top <= scrollerTop) {
      const itemId = groupEl.getAttribute("data-commentary-item-observer");
      const match = itemId?.match(/^group-(\d+)$/);
      if (match) {
        activeIndex = parseInt(match[1]);
      }
    }
  }

  if (currentGroupIndex.value !== activeIndex) {
    currentGroupIndex.value = activeIndex;
  }
};
```

### **Search Integration for Commentary**

Commentary search works on the flattened virtual items:

```typescript
function handleSearchQueryChange(query: string) {
  // Build searchable items from virtual commentary items
  const items: Array<{ index: number; content: string }> = [];
  virtualCommentaryItems.value.forEach((item, index) => {
    items.push({
      index: index,
      content: item.content,
    });
  });

  search.searchInItems(items, query);
  searchRef.value?.setMatches(search.totalMatches.value);
}

function handleNavigateToMatch(matchIndex: number) {
  search.navigateToMatch(matchIndex);
  const match = search.currentMatch.value;
  if (match && commentaryScrollerRef.value) {
    isNavigating.value = true;

    // Scroll to virtual item (not group)
    commentaryScrollerRef.value.scrollToItem(match.itemIndex);

    // Fine-tune to highlighted text
    setTimeout(() => {
      const currentMark = document.querySelector(
        ".commentary-content mark.current",
      );
      if (currentMark) {
        currentMark.scrollIntoView({ behavior: "auto", block: "center" });
      }
      setTimeout(() => {
        isNavigating.value = false;
      }, 100);
    }, 100);
  }
}
```

### **Position Restoration for Commentary**

Commentary position is saved by group index and targetBookId:

```typescript
// Save position
function saveCurrentTargetBookId() {
  const currentGroup = linkGroups.value[currentGroupIndex.value];
  if (currentGroup) {
    const filterKey = getFilterKey(
      selectedConnectionTypeId.value,
      props.selectedLineIndex,
    );
    activeTab.bookState.commentaryPositionsByFilter[filterKey] = {
      groupIndex: currentGroupIndex.value,
      targetBookId: currentGroup.targetBookId,
      scrollPosition: 0, // Not needed for virtual scroller
    };
  }
}

// Restore position
function restoreByTargetBookId(): boolean {
  const filterKey = getFilterKey(
    selectedConnectionTypeId.value,
    props.selectedLineIndex,
  );
  const savedData = activeTab.bookState.commentaryPositionsByFilter[filterKey];

  if (savedData?.targetBookId) {
    // Find group with matching targetBookId
    const matchingIndex = linkGroups.value.findIndex(
      (group) => group.targetBookId === savedData.targetBookId,
    );

    if (matchingIndex !== -1) {
      suppressNextSave.value = true;
      currentGroupIndex.value = matchingIndex;
      setTimeout(() => scrollToGroup(matchingIndex), 100);
      return true;
    }
  }

  return false;
}
```

```typescript
class DatabaseManager {
  // Line loading operations
  async getTotalLines(bookId: number): Promise<number>;
  async loadLineRange(
    bookId: number,
    start: number,
    end: number,
  ): Promise<LineLoadResult[]>;
  async getLineContent(
    bookId: number,
    lineIndex: number,
  ): Promise<string | null>;
  async searchLines(
    bookId: number,
    searchTerm: string,
  ): Promise<LineLoadResult[]>;
}
```

**Routing Logic**:

- **Production**: Routes to C# WebView bridge for SQLite access
- **Development**: Routes to browser-based SQLite (sql.js)
- **Automatic**: Detects environment and chooses appropriate backend

### **BookLineViewer** (`src/components/BookLineViewer.vue`)

**UI Component** that integrates with the loading system:

```typescript
// Integration points
const viewerState = new BookLineViewerState();
watch(
  () => myTab.value?.bookState?.bookId,
  async (bookId) => {
    await viewerState.loadBook(bookId, isRestore, initialLineIndex);
  },
);

// Priority loading for navigation
async function scrollToLine(lineIndex: number) {
  await viewerState.prioritizeLines(lineIndex, LOAD_BUFFER_SIZE);
  // ... scroll to line
}
```

## Smart Streaming Flow

### **Stage 1: Instant Scaffolding**

```typescript
// Immediate UI responsiveness
async loadBook(bookId: number, isRestore: boolean, initialLineIndex?: number) {
  this.totalLines.value = await dbManager.getTotalLines(bookId)

  // Stage 1: Immediate scaffolding with placeholders
  const placeholders: Record<number, string> = {}
  for (let i = 0; i < this.totalLines.value; i++) {
    placeholders[i] = '\u00A0' // Hard space placeholder
  }
  this.lines.value = placeholders // Instant navigation ready

  // Stage 2: Start smart streaming
  this.startSmartStreaming()
}
```

**Benefits**:

- **Instant Navigation**: TOC, search, scroll-to work immediately
- **No Loading Delays**: User can navigate while content streams in
- **Predictable UI**: Fixed document height, stable scrolling

### **Stage 2: Priority-Based Streaming**

```typescript
// Smart streaming with priority queue
private startSmartStreaming() {
  // Initialize priority queue with all batches (default order)
  const totalBatches = Math.ceil(this.totalLines.value / BATCH_SIZE)
  this.priorityQueue = Array.from({ length: totalBatches }, (_, i) => i)

  // Start streaming
  this.processQueue()
}

// Priority loading for user actions
async prioritizeLines(centerLine: number, padding = PADDING_LINES) {
  // Calculate needed batches around center line
  const neededBatches = new Set<number>()
  for (let i = start; i <= end; i++) {
    const batchIndex = Math.floor(i / BATCH_SIZE)
    neededBatches.add(batchIndex)
  }

  // Move to front of priority queue
  this.priorityQueue.unshift(...batchArray)

  // Load most critical batch immediately
  await this.loadBatch(batchArray[0]!)
}
```

**Priority System**:

1. **User Navigation** (TOC, scroll-to) - Immediate priority
2. **Visible Content** - High priority
3. **Buffer Zones** - Medium priority
4. **Sequential Loading** - Background priority

### **Stage 3: Efficient Batch Loading**

```typescript
// Optimized database queries
private async loadBatch(batchIndex: number) {
  const start = batchIndex * BATCH_SIZE
  const end = Math.min(start + BATCH_SIZE - 1, this.totalLines.value - 1)

  // Single range query instead of individual line queries
  const loadedLines = await dbManager.loadLineRange(this.bookId, start, end)

  // Update reactive state
  loadedLines.forEach(line => {
    this.lines.value[line.lineIndex] = line.content
  })
}
```

**Database Optimization**:

```sql
-- Efficient range query (GOOD)
SELECT lineIndex, content FROM line
WHERE bookId = ? AND lineIndex BETWEEN 100 AND 299
ORDER BY lineIndex

-- Instead of multiple individual queries (BAD)
SELECT content FROM line WHERE bookId = ? AND lineIndex = 100
SELECT content FROM line WHERE bookId = ? AND lineIndex = 101
-- ... 200 individual queries
```

## Search Integration (Virtualization-Ready)

### **Progressive Source Data Search**

The search system is designed to work with the streaming architecture:

```typescript
// Search loaded content (source data, not DOM)
async getSearchData(): Promise<Array<{ index: number, content: string }>> {
  const allLines: Array<{ index: number, content: string }> = []

  Object.entries(this.lines.value).forEach(([indexStr, content]) => {
    if (content && content !== '\u00A0') { // Exclude placeholders
      allLines.push({
        index: Number(indexStr),
        content // Raw source content, not rendered HTML
      })
    }
  })

  return allLines.sort((a, b) => a.index - b.index)
}

// Auto-update search results as new content streams in
watch(() => Object.keys(viewerState.lines.value).length, () => {
  if (currentSearchQuery.value.trim()) {
    performSearch() // Re-search with newly loaded content
  }
})
```

**Search Characteristics**:

- **Source Data**: Searches raw content, not DOM elements
- **Progressive**: Results expand as more content loads
- **Virtualization Ready**: Will work with future full virtualization
- **Performance**: Efficient text search on loaded content

## Configuration Constants

```typescript
// BookLineViewerState configuration
const PADDING_LINES = 200; // Lines to load around priority areas
const BATCH_SIZE = 200; // Lines per database query

// BookLineViewer configuration
const LOAD_BUFFER_SIZE = 100; // Lines to prioritize around scroll target
```

**Tuning Guidelines**:

- **BATCH_SIZE**: Balance between query efficiency and memory usage
- **PADDING_LINES**: Larger = smoother navigation, more memory
- **LOAD_BUFFER_SIZE**: Affects scroll-to-line responsiveness

## Performance Characteristics

### **Memory Usage**

- **Loaded Content**: Only streamed-in lines (typically 10-50% of document)
- **Placeholders**: Minimal memory (single character per line)
- **Growth Pattern**: Gradual increase as user navigates
- **Cleanup**: Automatic cleanup possible (future enhancement)

### **Database Efficiency**

- **Batch Queries**: 200 lines per query vs individual line queries
- **Priority Loading**: User needs loaded first
- **Reduced Requests**: ~5-20 queries vs 1000s of individual queries
- **Connection Reuse**: Single connection for all operations

### **User Experience**

- **Instant Navigation**: TOC, search, scroll-to work immediately
- **Smooth Scrolling**: Priority loading ensures content availability
- **Progressive Enhancement**: Content appears as it loads
- **No Blocking**: UI remains responsive during loading

## Database Schema

### **Core Tables**

```sql
-- Book metadata
CREATE TABLE book (
  Id INTEGER PRIMARY KEY,
  TotalLines INTEGER,  -- Used for instant scaffolding
  -- ... other book metadata
);

-- Line content
CREATE TABLE line (
  bookId INTEGER,
  lineIndex INTEGER,   -- 0-based line number
  content TEXT,        -- HTML content
  PRIMARY KEY (bookId, lineIndex)
);
```

### **Query Patterns**

```sql
-- Get total lines for scaffolding
SELECT TotalLines as totalLines FROM book WHERE Id = ?

-- Load batch of lines
SELECT lineIndex, content FROM line
WHERE bookId = ? AND lineIndex >= ? AND lineIndex <= ?
ORDER BY lineIndex

-- Search within book
SELECT lineIndex, content FROM line
WHERE bookId = ? AND content LIKE ?
ORDER BY lineIndex
```

## Integration Points

### **Tab State Management**

```typescript
// BookLineViewer integration
watch(
  () => myTab.value?.bookState?.bookId,
  async (bookId, oldBookId) => {
    if (bookId && bookId !== oldBookId) {
      const initialLineIndex = myTab.value?.bookState?.initialLineIndex;
      await viewerState.loadBook(bookId, isRestore, initialLineIndex);

      // Scroll to initial position if provided
      if (initialLineIndex !== undefined) {
        await scrollToLine(initialLineIndex, "start");
      }
    }
  },
);
```

### **TOC Navigation**

```typescript
// Priority loading for TOC selection
async function handleTocSelection(lineIndex: number) {
  await viewerState.handleTocSelection(lineIndex); // Priority load
  await nextTick();
  scrollToLine(lineIndex); // Navigate to loaded content
}
```

### **Search Integration**

```typescript
// Progressive search with streaming content
async function performSearch() {
  const query = currentSearchQuery.value;
  if (!query.trim()) return;

  // Search loaded lines (expands as content streams)
  const allLines = await viewerState.getSearchData();
  search.searchInItems(allLines, query);
}

// Re-search when new content loads
watch(
  () => Object.keys(viewerState.lines.value).length,
  () => {
    if (currentSearchQuery.value.trim()) {
      performSearch();
    }
  },
);
```

## Future Virtualization Enhancements

The current architecture is designed to support full virtualization:

### **Planned Enhancements**

1. **Memory Cleanup**: Remove non-visible content from memory
2. **Intersection Observer**: Load content based on viewport visibility
3. **Virtual Scrolling**: Render only visible DOM elements
4. **Advanced Caching**: LRU cache for recently accessed content
5. **Predictive Loading**: Load content based on scroll direction

### **Architecture Readiness**

- ✅ **Source Data Search**: Already implemented
- ✅ **Batch Loading**: Efficient database queries
- ✅ **Priority System**: User actions prioritized
- ✅ **Reactive Updates**: Search updates as content loads
- ✅ **Placeholder System**: Instant navigation ready

## Best Practices

### **Implementation Guidelines**

1. **Always use source data for search** - Never search DOM elements
2. **Prioritize user actions** - TOC, scroll-to get immediate loading
3. **Batch database queries** - Use range queries, not individual lines
4. **Progressive enhancement** - Features work with partial content
5. **Maintain reactivity** - Update search/UI as content streams

### **Performance Optimization**

1. **Tune batch sizes** based on content and performance testing
2. **Monitor memory usage** and implement cleanup when needed
3. **Profile database queries** to optimize SQL performance
4. **Use priority loading** for better perceived performance
5. **Consider caching** for frequently accessed content

### **Error Handling**

1. **Graceful degradation** when database queries fail
2. **Retry logic** for failed batch loads
3. **Fallback content** when specific lines can't load
4. **User feedback** for loading states and errors
5. **Cleanup resources** on component unmount

## Troubleshooting

### **Common Issues**

**Search not updating with new content**:

- Verify `watch` on `viewerState.lines.value` length
- Check `getSearchData()` excludes placeholders
- Ensure search re-triggers on content changes

**Slow navigation to specific lines**:

- Check `prioritizeLines()` is called before navigation
- Verify batch loading is working efficiently
- Monitor database query performance

**Memory usage growing indefinitely**:

- Implement cleanup for non-visible content
- Monitor `lines.value` size over time
- Consider LRU cache for loaded content

**Placeholder content showing**:

- Verify batch loading is completing successfully
- Check database queries return expected results
- Ensure priority loading works for user actions

### **Debugging Tools**

```typescript
// Monitor loading progress
console.log(`✅ Loaded batch ${batchIndex} (lines ${start}-${end})`);

// Track memory usage
console.log("Lines in memory:", Object.keys(this.lines.value).length);

// Debug priority queue
console.log("Priority queue:", this.priorityQueue);

// Monitor search data
console.log("Search data size:", (await getSearchData()).length);
```

## Session Accomplishments

### ✅ Copy Behavior for Virtualized Content

#### Problem: Ctrl+C Copying All Content Regardless of Selection

The virtualized components (BookLineViewer and BookCommentaryView) had custom copy handlers that would copy all source content (including non-virtualized items) on any Ctrl+C, not just after Ctrl+A.

#### Solution: Chained Shortcut Pattern with Selection Tracking

Implemented a flag-based system that mimics normal browser behavior:

```typescript
// Track if Ctrl+A was pressed and selection is still active
const selectAllWasPressed = ref(false);

// Reset flag when user interacts in ways that would deselect
useEventListener(containerRef, "mousedown", () => {
  selectAllWasPressed.value = false;
});

useEventListener(document, "selectionchange", () => {
  if (!hasFocus.value) return;

  const selection = window.getSelection();
  if (!selection || selection.rangeCount === 0 || selection.isCollapsed) {
    selectAllWasPressed.value = false;
  }
});

// Keyboard shortcuts
useEventListener("keydown", (event: KeyboardEvent) => {
  if (!hasFocus.value) return;
  const hasCtrlOrMeta = event.ctrlKey || event.metaKey;

  // Ctrl+A: Set flag and select all
  if (hasCtrlOrMeta && event.code === "KeyA") {
    event.preventDefault();
    selectAllWasPressed.value = true;
    selectAllInContainer();
  }

  // Reset flag on actions that would deselect
  if (!hasCtrlOrMeta && !event.shiftKey) {
    if (
      event.code.startsWith("Arrow") ||
      event.code === "Escape" ||
      event.code === "Home" ||
      event.code === "End" ||
      event.code === "PageUp" ||
      event.code === "PageDown" ||
      (event.key.length === 1 && !event.ctrlKey && !event.metaKey)
    ) {
      selectAllWasPressed.value = false;
    }
  }
});

// Copy handler checks flag
function handleCopy(event: ClipboardEvent) {
  // ... selection validation ...

  // Only copy all content as HTML if Ctrl+A was pressed
  if (!selectAllWasPressed.value) {
    console.log("Ctrl+A was not pressed, using default copy behavior");
    return; // Let browser handle partial selection copy
  }

  // Reset flag after use
  selectAllWasPressed.value = false;

  // Copy all source content including non-virtualized items
  // ... copy implementation ...
}
```

#### Behavior Characteristics

**Flag persists as long as selection remains active:**

- Ctrl+A sets the flag
- Multiple Ctrl+C presses work while selection is active
- Flag resets only when user does something that would normally deselect:
  - Clicking anywhere (mousedown)
  - Pressing arrow keys, Escape, Home, End, PageUp, PageDown
  - Typing regular characters
  - Selection becomes empty or collapsed (selectionchange event)
  - Opening search (Ctrl+F)

**Flag does NOT reset on:**

- Ctrl+C (so you can copy multiple times)
- Other Ctrl shortcuts that don't affect selection
- Shift+Arrow (extending selection)

#### Implementation in Both Components

- **BookLineViewer**: Copies all source lines including non-virtualized content
- **BookCommentaryView**: Copies all commentary groups and links including non-virtualized content

This properly mimics normal browser behavior where Ctrl+A selection stays active until you explicitly do something to change it, while supporting the virtualized architecture where not all content is in the DOM.

### ✅ Architecture Analysis Complete

#### Line Loading System Understanding

- **Smart Streaming**: Analyzed complete flow from scaffolding to batch loading
- **Priority System**: Documented how user actions get immediate priority
- **Database Integration**: Mapped C# WebView bridge and development fallbacks
- **Search Integration**: Confirmed source data search architecture

#### Key Architectural Insights

1. **Two-Stage Loading**: Instant scaffolding + priority streaming
2. **Efficient Batching**: 200-line range queries vs individual requests
3. **Virtualization Ready**: Source data search and reactive updates
4. **Memory Efficient**: Only loaded content in memory, not full documents
5. **User-Centric**: Priority loading for navigation and search

#### Documentation Updates

- **Complete Architecture Guide**: Comprehensive line loading documentation
- **Implementation Details**: Code examples and integration patterns
- **Performance Characteristics**: Memory, database, and UX metrics
- **Future Roadmap**: Virtualization enhancement planning
- **Best Practices**: Guidelines for optimization and troubleshooting

The line loading architecture is well-designed for both current performance and future virtualization enhancements, with excellent separation of concerns and efficient resource management.

## Critical: Virtual Scroller forceUpdate() Pattern

### **Problem: Programmatic Scrolling Fails in Virtual Scrollers**

When calling `scrollToItem()` immediately after data changes or component mounting, the virtual scroller may not have calculated item heights and positions yet. This causes:

- `scrollToItem()` to fail silently (doesn't scroll)
- Elements not appearing in DOM even after scroll
- Inconsistent behavior (works sometimes, fails other times)

### **Root Cause**

Virtual scrollers calculate item positions lazily:

1. Data changes → Vue updates reactive state
2. `nextTick()` → Vue flushes DOM updates
3. **BUT** virtual scroller hasn't calculated heights yet
4. `scrollToItem()` called → uses stale/missing height data → fails

### **Solution: Call forceUpdate() Before Scrolling**

```typescript
async function scrollToGroup(groupIndex: number) {
  if (!commentaryScrollerRef.value) return;

  // Update current group index
  currentGroupIndex.value = groupIndex;

  // CRITICAL: Force virtual scroller to recalculate heights
  await nextTick();
  if (
    commentaryScrollerRef.value &&
    typeof commentaryScrollerRef.value.forceUpdate === "function"
  ) {
    commentaryScrollerRef.value.forceUpdate();
    console.log("🎯 Forced virtual scroller update");
    await nextTick();
  }

  // Now scrollToItem will work reliably
  const groupHeaderId = `group-header-${groupIndex}`;
  const itemIndex = virtualCommentaryItems.value.findIndex(
    (item) => item.id === groupHeaderId,
  );

  if (itemIndex !== -1) {
    commentaryScrollerRef.value.scrollToItem(itemIndex);

    // Wait for rendering, then use scrollIntoView for precise positioning
    await nextTick();
    await new Promise((resolve) => requestAnimationFrame(resolve));

    const scrollerEl = commentaryScrollerRef.value.$el as HTMLElement;
    const targetElement = scrollerEl.querySelector(
      `[data-group-index="${groupIndex}"]`,
    ) as HTMLElement;
    if (targetElement) {
      targetElement.scrollIntoView({ block: "start", behavior: "auto" });
    }
  }
}
```

### **When to Use forceUpdate()**

Use `forceUpdate()` before `scrollToItem()` when:

1. **After data changes**: New items loaded, items filtered, items reordered
2. **After component mounting**: Scroller just became available
3. **After display mode changes**: Inline/block toggle, font size changes
4. **Before programmatic navigation**: TOC clicks, search navigation, restoration

### **Complete Pattern with Verification**

```typescript
async function scrollToGroup(groupIndex: number) {
  if (!commentaryScrollerRef.value) return;

  // 1. Force virtual scroller to recalculate
  await nextTick();
  if (typeof commentaryScrollerRef.value.forceUpdate === "function") {
    commentaryScrollerRef.value.forceUpdate();
    await nextTick();
  }

  // 2. Find target item index
  const itemIndex = virtualCommentaryItems.value.findIndex(
    (item) => item.id === `group-header-${groupIndex}`,
  );
  if (itemIndex === -1) return;

  // 3. Scroll to item
  commentaryScrollerRef.value.scrollToItem(itemIndex);

  // 4. Wait for rendering with polling
  const scrollerEl = commentaryScrollerRef.value.$el as HTMLElement;
  const maxAttempts = 20;
  let attempts = 0;
  let targetElement: HTMLElement | null = null;

  while (attempts < maxAttempts && !targetElement) {
    await new Promise((resolve) => requestAnimationFrame(resolve));
    targetElement = scrollerEl.querySelector(
      `[data-group-index="${groupIndex}"]`,
    );
    attempts++;
  }

  // 5. Fine-tune with scrollIntoView
  if (targetElement) {
    targetElement.scrollIntoView({ block: "start", behavior: "auto" });

    // 6. Verify success
    await nextTick();
    const rect = targetElement.getBoundingClientRect();
    const scrollerRect = scrollerEl.getBoundingClientRect();
    const isVisible =
      rect.top >= scrollerRect.top && rect.top <= scrollerRect.bottom;
    console.log("✅ Scroll verified - target visible:", isVisible);
  }
}
```

### **Key Takeaways**

1. **Always call `forceUpdate()` before `scrollToItem()`** when scrolling programmatically
2. **Use `requestAnimationFrame()` polling** to wait for DOM rendering (not just `nextTick()`)
3. **Combine `scrollToItem()` + `scrollIntoView()`** for reliable positioning
4. **Verify scroll success** by checking element position in viewport
5. **This pattern is essential** for any virtual scroller programmatic navigation

This pattern ensures 100% reliable scrolling in virtual scrollers, eliminating the "sometimes works, sometimes doesn't" problem.
