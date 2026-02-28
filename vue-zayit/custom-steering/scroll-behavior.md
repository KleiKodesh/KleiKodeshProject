# Scroll Behavior Guidelines

## Core Principles

1. Always use `block: 'nearest'` with `scrollIntoView()` to prevent aggressive scrolling
2. **ALWAYS use `behavior: 'auto'` (instant scrolling) - NEVER use `behavior: 'smooth'`**
3. **ALWAYS use the composable functions from `useScrollToElement` - NEVER call `scrollIntoView()` directly**
4. **CRITICAL: Virtual scrollers require separate implementations** - Whatever you do in the composable for regular elements, you MUST have a separate custom implementation for virtual scrollers (DynamicScroller)

## Standard Scroll Pattern

For most elements, use the simple pattern:

```typescript
import { scrollToElement } from "@/composables/useScrollToElement";

await scrollToElement(element);
```

## Centered Scroll Pattern

When you need to center an element in the viewport (e.g., TOC tree navigation, search results, focused items):

**CRITICAL: Never use `block: 'center'` directly - always use `scrollToElementCentered()`**

```typescript
import { scrollToElementCentered } from "@/composables/useScrollToElement";

await scrollToElementCentered(element);
```

This uses a two-tier approach that prevents parent container scrolling:

1. **Tier 1**: `scrollIntoView({ block: 'nearest' })` - gets element into view without affecting parents
2. **Tier 2**: Manual centering calculation - adjusts scroll within the container to center the element

**Why this approach?**

- Using `block: 'center'` directly can cause parent containers to scroll unexpectedly
- The two-tier approach ensures only the immediate scrollable container is affected
- Provides consistent centering behavior across all use cases

**Common use cases:**

- TOC tree navigation - center the selected entry
- Search result highlighting - center the matched item
- Keyboard navigation - center the focused item
- Any UI where the user expects the item to be prominently displayed

## Virtual Scroller Pattern (Two-Tier Approach)

When scrolling to elements inside a virtual scroller (DynamicScroller), use a two-tier approach:

1. **First tier**: Use the virtual scroller's built-in `scrollToItem()` API to scroll the virtualized list
2. **Second tier**: Use regular `scrollIntoView()` with `block: 'nearest'` to fine-tune/clamp the scroll position

```typescript
import { scrollToVirtualItem } from "@/composables/useScrollToElement";

await scrollToVirtualItem({
  virtualScrollerRef: scrollerRef,
  itemIndex: 42,
});
```

## Virtual Scroller Centered Pattern (Three-Tier Approach)

When you need to center an item in a virtual scroller:

**CRITICAL: Never use `block: 'center'` with virtual scrollers - always use `scrollToVirtualItemCentered()`**

```typescript
import { scrollToVirtualItemCentered } from "@/composables/useScrollToElement";

await scrollToVirtualItemCentered(scrollerRef, itemIndex);
```

This uses a three-tier approach that prevents parent container scrolling:

1. **Tier 1**: Virtual scroller's `scrollToItem()` - gets item into virtual viewport
2. **Tier 2**: `scrollIntoView({ block: 'nearest' })` - fine-tunes position without affecting parents
3. **Tier 3**: Manual centering calculation - adjusts scroll within the virtual scroller to center the item

**Common use cases:**

- Centered search results in virtualized lists
- Focused items in large virtual lists
- Highlighted entries that need prominence

```typescript
// Example: Scrolling to a line in BookLineViewer
async function scrollToLine(lineIndex: number) {
  // Tier 1: Use virtual scroller's API
  await scrollerRef.value?.scrollToItem(lineIndex);
  await nextTick();

  // Tier 2: Fine-tune with scrollIntoView
  const lineElement = scrollerRef.value?.$el.querySelector(
    `[data-index="${lineIndex}"]`,
  );
  if (lineElement) {
    lineElement.scrollIntoView({
      behavior: "auto",
      block: "nearest",
      inline: "nearest",
    });
  }
}
```

## Search Bar Offset Pattern (Third Tier)

When scrolling to elements that might be hidden behind a fixed search bar, add manual offset calculation as a THIRD tier:

**IMPORTANT**: When using virtual scroller, ALWAYS use its API for offset detection and setting, not manual calculations.

```typescript
async function scrollToElementWithSearchBarOffset(
  element: HTMLElement,
  searchBarHeight: number,
  virtualScrollerRef?: Ref<any>,
  itemIndex?: number,
) {
  // Tier 1: Use virtual scroller's API (if applicable)
  if (virtualScrollerRef?.value && itemIndex !== undefined) {
    await virtualScrollerRef.value.scrollToItem(itemIndex);
    await nextTick();
  }

  // Tier 2: Fine-tune with scrollIntoView
  element.scrollIntoView({
    behavior: "auto",
    block: "nearest",
    inline: "nearest",
  });
  await nextTick();

  // Tier 3: Apply search bar offset using virtual scroller's API if available
  if (virtualScrollerRef?.value) {
    // Use virtual scroller's scrollTop property for offset
    const currentScroll = virtualScrollerRef.value.$el.scrollTop;
    virtualScrollerRef.value.$el.scrollTop = currentScroll - searchBarHeight;
  } else {
    // Fallback for non-virtual scrollers
    const container = element.closest(".scroller") as HTMLElement;
    if (container) {
      container.scrollTop -= searchBarHeight;
    }
  }
}
```

**Three-Tier Pattern Summary:**

1. **Tier 1**: Virtual scroller's `scrollToItem()` - gets item into virtual viewport
2. **Tier 2**: `scrollIntoView({ block: 'nearest' })` - fine-tunes position
3. **Tier 3**: Offset adjustment using virtual scroller's `scrollTop` - accounts for fixed UI elements
   });
   }

````

## Utility/Composable Design

**CRITICAL: Always import and use these functions - NEVER call scrollIntoView() directly**

Create separate exported functions for different use cases:

```typescript
// For regular (non-virtual) scrollers
interface RegularScrollOptions {
  behavior?: 'auto'; // Always 'auto'
  block?: ScrollLogicalPosition; // Default: 'nearest'
  inline?: ScrollLogicalPosition; // Default: 'nearest'
  searchBarOffset?: number; // Optional offset for fixed UI
}

// For virtual scrollers
interface VirtualScrollOptions {
  virtualScrollerRef: Ref<any>; // Required
  itemIndex: number; // Required
  block?: ScrollLogicalPosition; // Default: 'nearest'
  inline?: ScrollLogicalPosition; // Default: 'nearest'
  searchBarOffset?: number; // Optional offset for fixed UI
}

// Export separate functions
export function scrollToElement(
  element: HTMLElement,
  options?: RegularScrollOptions
): Promise<void>

export function scrollToElementCentered(
  element: HTMLElement
): Promise<void>

export function scrollToVirtualItem(
  options: VirtualScrollOptions
): Promise<void>

export function scrollToVirtualItemCentered(
  virtualScrollerRef: Ref<any>,
  itemIndex: number
): Promise<void>

// Usage examples:

// 1. Simple regular scroll
import { scrollToElement } from '@/composables/useScrollToElement';
await scrollToElement(element);

// 2. Regular scroll with search bar offset
await scrollToElement(element, {
  searchBarOffset: 60
});

// 3. Centered scroll (e.g., TOC tree)
import { scrollToElementCentered } from '@/composables/useScrollToElement';
await scrollToElementCentered(element);

// 4. Virtual scroller scroll
import { scrollToVirtualItem } from '@/composables/useScrollToElement';
await scrollToVirtualItem({
  virtualScrollerRef: scrollerRef,
  itemIndex: 42
});

// 5. Virtual scroller + search bar offset
await scrollToVirtualItem({
  virtualScrollerRef: scrollerRef,
  itemIndex: 42,
  searchBarOffset: 60
});

// 6. Virtual scroller centered scroll
import { scrollToVirtualItemCentered } from '@/composables/useScrollToElement';
await scrollToVirtualItemCentered(scrollerRef, 42);
```

## Implementation Details

### scrollToElement (Regular Scrollers)

```typescript
export async function scrollToElement(
  element: HTMLElement,
  options: RegularScrollOptions = {}
): Promise<void> {
  const {
    behavior = 'auto',
    block = 'nearest',
    inline = 'nearest',
    searchBarOffset = 0
  } = options;

  // Tier 1: scrollIntoView
  element.scrollIntoView({ behavior, block, inline });

  // Tier 2: Apply search bar offset if needed
  if (searchBarOffset > 0) {
    await nextTick();
    const container = element.closest('.scroller') as HTMLElement;
    if (container) {
      container.scrollTop -= searchBarOffset;
    }
  }
}
```

### scrollToVirtualItem (Virtual Scrollers)

```typescript
export async function scrollToVirtualItem(
  options: VirtualScrollOptions
): Promise<void> {
  const {
    virtualScrollerRef,
    itemIndex,
    block = 'nearest',
    inline = 'nearest',
    searchBarOffset = 0
  } = options;

  // Tier 1: Use virtual scroller's API
  await virtualScrollerRef.value?.scrollToItem(itemIndex);
  await nextTick();

  // Tier 2: Fine-tune with scrollIntoView
  const element = virtualScrollerRef.value?.$el.querySelector(
    `[data-index="${itemIndex}"]`
  );
  if (element) {
    element.scrollIntoView({
      behavior: 'auto',
      block,
      inline
    });
  }

  // Tier 3: Apply search bar offset using virtual scroller's API
  if (searchBarOffset > 0) {
    await nextTick();
    const scrollerEl = virtualScrollerRef.value?.$el;
    if (scrollerEl) {
      scrollerEl.scrollTop -= searchBarOffset;
    }
  }
}
````

## Implementation Rules

1. **Default to `block: 'nearest'`** - Never use `center` or `start` unless absolutely necessary
2. **Always use `behavior: 'auto'`** - Instant scrolling only, never smooth
3. **ALWAYS use composable functions** - Never call `scrollIntoView()` directly
4. **For centering, use `scrollToElementCentered()`** - Never use `block: 'center'` directly, always use the two-tier centered approach
5. **CRITICAL: Separate implementations for virtual scrollers** - Every scroll function must have a dedicated virtual scroller version:
   - Regular: `scrollToElement()` → Virtual: `scrollToVirtualItem()`
   - Regular: `scrollToElementCentered()` → Virtual: `scrollToVirtualItemCentered()`
   - Virtual scrollers use the scroller's `scrollToItem()` API as the first tier
6. **Virtual scroller first** - Always use the scroller's API before fine-tuning with scrollIntoView
7. **Use virtual scroller's API for offsets** - When applying search bar offsets with virtual scrollers, use the scroller's `scrollTop` property, not manual calculations
8. **Search bar awareness** - Calculate offsets when fixed UI elements might obscure the target (third tier)
9. **Wait for DOM** - Use `await nextTick()` between each tier
10. **Check visibility** - The composable should check if element is already visible before scrolling

## Anti-Patterns to Avoid

```typescript
// ❌ BAD: Calling scrollIntoView directly
element.scrollIntoView({ block: "nearest" });

// ❌ BAD: Using block: 'center' directly - causes parent scrolling
element.scrollIntoView({ block: "center" });

// ❌ BAD: Using smooth scrolling
element.scrollIntoView({ behavior: "smooth", block: "nearest" });

// ❌ BAD: Not using virtual scroller API first
const element = scroller.querySelector('[data-index="42"]');
element.scrollIntoView({ block: "nearest" });

// ❌ BAD: Not accounting for search bar offset
element.scrollIntoView({ block: "nearest" });
// Element is now hidden behind search bar!

// ❌ BAD: Not waiting for virtual scroller to render
scrollerRef.value?.scrollToItem(42);
element.scrollIntoView({ block: "nearest" }); // Element might not exist yet!

// ✅ GOOD: Always use the composable for standard scroll
import { scrollToElement } from "@/composables/useScrollToElement";
await scrollToElement(element);

// ✅ GOOD: Always use the composable for centered scroll
import { scrollToElementCentered } from "@/composables/useScrollToElement";
await scrollToElementCentered(element);
```

## Current Usage Locations

All scroll behavior has been standardized using the composable:

1. `BookLineViewer.vue` - Uses `scrollToElement()` for search result highlighting
2. `BookCommentaryView.vue` - Uses `scrollToElement()` for commentary navigation
3. `BookTocTreeView.vue` - Uses `scrollToElementCentered()` for TOC tree navigation
4. `Combobox.vue` - Uses `scrollToElement()` for dropdown navigation
5. `useListKeyboardNavigation.ts` - Uses `scrollToElement()` for keyboard navigation

## Summary

**Four composable functions for all scroll needs:**

1. **`scrollToElement()`** - Standard minimal scroll (most common)
   - Use for: Search results, dropdown items, keyboard navigation
   - Behavior: Scrolls only if needed, minimal movement
   - Tiers: 2 (scrollIntoView → optional offset)

2. **`scrollToElementCentered()`** - Centered scroll (when prominence is needed)
   - Use for: TOC navigation, focused items, highlighted results
   - Behavior: Centers element without affecting parent containers
   - Tiers: 2 (scrollIntoView nearest → manual centering)

3. **`scrollToVirtualItem()`** - Virtual scroller scroll (for DynamicScroller)
   - Use for: Virtual scrolled lists with thousands of items
   - Behavior: Three-tier approach with virtual scroller API
   - Tiers: 3 (scrollToItem → scrollIntoView → optional offset)

4. **`scrollToVirtualItemCentered()`** - Virtual scroller centered scroll
   - Use for: Centered items in virtual scrolled lists
   - Behavior: Three-tier approach with centering
   - Tiers: 3 (scrollToItem → scrollIntoView nearest → manual centering)

**Key Architecture Rule:**

- Virtual scrollers (DynamicScroller) ALWAYS require separate custom implementations
- Never try to use regular scroll functions with virtual scrollers
- The pattern: For every regular scroll function, create a corresponding virtual scroller version

**Remember: NEVER call `scrollIntoView()` directly - always use these composables!**
