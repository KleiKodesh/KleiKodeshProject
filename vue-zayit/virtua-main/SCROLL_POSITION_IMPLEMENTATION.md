# Scroll Position Implementation

## Overview

Virtua provides two methods for pixel-perfect scroll position persistence: `getScrollPosition()` and `scrollToPosition()`. These methods use DOM-based measurements with `getBoundingClientRect()` to accurately track and restore scroll positions, even when scrolled partway through long items.

## Methods

### `getScrollPosition(): { index: number, offset: number }`

Returns the current scroll position with item index and pixel offset into that item.

**Implementation:**

1. Gets the scroll container's bounding rect
2. Tries three positions to find an element (with 5px padding offset):
   - Top-left of scroll container
   - Top-right of scroll container
   - Top-center of scroll container
3. Uses `document.elementFromPoint()` to find the element at each position
4. Uses `closest('[data-index]')` to find the item element
5. Calculates offset: `elementRect.top - scrollerRect.top`

**Returns:**

- `index`: The item index at the top of the viewport
- `offset`: Pixels from scroller top to element top (negative when scrolled into the item)

**Example:**

```javascript
const position = virtuaRef.value.getScrollPosition();
// { index: 42, offset: -3000 }
// Means: item 42 is at viewport top, scrolled 3000px into that item
```

### `scrollToPosition(index: number, offsetIntoItem: number): void`

Scrolls to a specific position within an item.

**Implementation:**

1. Uses Virtua's `scrollToIndex(index, { align: 'start' })` to scroll to the item
2. Waits for render with `requestAnimationFrame()`
3. Finds the item element using `querySelector('[data-index="${index}"]')`
4. Calculates current offset: `elementRect.top - scrollerRect.top`
5. Calculates adjustment needed: `currentOffset - targetOffset`
6. Manually adjusts scroll position: `scrollContainer.scrollTop += adjustment`

**Parameters:**

- `index`: The item index to scroll to
- `offsetIntoItem`: The pixel offset from scroller top (negative when scrolled into item)

**Example:**

```javascript
// Restore position: item 42, scrolled 3000px into it
virtuaRef.value.scrollToPosition(42, -3000);
```

## Why This Approach?

### Problem

Virtua's internal cache tracks item sizes, but these may not match actual rendered DOM sizes due to:

- Dynamic content
- Font rendering differences
- CSS that affects layout
- Browser zoom levels

### Solution

Using `getBoundingClientRect()` measures actual rendered positions in the DOM, providing pixel-perfect accuracy regardless of Virtua's internal state.

### Fallback Strategy

The three-position fallback (left, right, center) ensures we find an element even if:

- Items don't stretch full width
- Items have unusual layouts
- Padding/margins create gaps

## Usage in Vue App

```typescript
// Save scroll position
function saveScrollPosition() {
  const position = virtuaRef.value.getScrollPosition();
  tabState.scrollIndex = position.index;
  tabState.scrollOffset = position.offset;
}

// Restore scroll position
function restoreScrollPosition() {
  virtuaRef.value.scrollToPosition(tabState.scrollIndex, tabState.scrollOffset);
}
```

## Technical Details

- **Coordinate System**: All measurements use viewport coordinates from `getBoundingClientRect()`
- **Offset Calculation**: `element.getBoundingClientRect().top - container.getBoundingClientRect().top`
- **Negative Offsets**: When an item is scrolled partway through, its top is above the container top, resulting in a negative offset
- **Padding Offset**: 5px offset accounts for container padding when detecting elements
- **Horizontal Support**: Both methods support horizontal scrolling via the `isHorizontal` flag

## Requirements

- Items must have `data-index` attribute (automatically added by Virtua's ListItem component)
- Scroll container must be the direct parent of the Virtualizer container
