# Virtual Scroller Keyboard Navigation Utility

## Overview

Create reusable composable for Ctrl+Home/End keyboard shortcuts that works with any virtual scroller.

## User Stories

### 1. Ctrl+Home scrolls to first item

- Press Ctrl+Home to scroll to first item in virtual scroller
- Works in BookLineViewer, BookCommentaryView, and search pages

### 2. Ctrl+End scrolls to last item

- Press Ctrl+End to scroll to last item in virtual scroller
- Adjusts to actual end of scroller after scrolling

## Technical Design

### Composable: `useVirtualScrollerKeyboard`

**Parameters:**

- `scrollerRef`: Ref to DynamicScroller instance
- `totalItems`: Computed number of items
- `enabled`: Optional ref to enable/disable (default: true)

**Implementation:**

- Listen for Ctrl+Home: scroll to item 0
- Listen for Ctrl+End: scroll to last item, then adjust scrollTop to scrollHeight
- Use `useEventListener` from VueUse
- Check `event.code` for keyboard layout independence

### Usage

```typescript
useVirtualScrollerKeyboard(
  scrollerRef,
  computed(() => items.value.length),
);
```

## Success Criteria

- [x] Ctrl+Home scrolls to first item in all three views
- [x] Ctrl+End scrolls to last item in all three views
- [x] Removes duplicate keyboard logic from components
- [x] Works with any keyboard layout (uses event.code)
