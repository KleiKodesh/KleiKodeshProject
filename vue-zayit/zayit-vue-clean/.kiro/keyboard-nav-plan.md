# Keyboard Navigation Plan

## Status: IMPLEMENTED ✓

---

## What Was Built

Three composables in `src/composables/`:

| File | Purpose |
|---|---|
| `useVirtualListKeys.ts` | Virtual lists (TanStack) — replaces `useVirtualScrollerKeys` |
| `useListKeys.ts` | Plain DOM lists and trees |
| `useTilesKeys.ts` | 2D tile grids |

Global focus style added to `src/assets/styles/main.css`:
```css
[data-nav-item].is-focused {
  outline: 1px solid color-mix(in srgb, var(--accent-color) 60%, transparent);
  outline-offset: -1px;
}
```

---

## Components Wired

| Component | Composable | Notes |
|---|---|---|
| `BooksTreeView` (list) | `useVirtualListKeys` | Enter/Space → enterFolder or selectBook |
| `BooksTreeView` (tiles) | `useTilesKeys` | `tabindex="0"` on `.tiles-grid` |
| `BooksSearchResults` (list/tree) | `useVirtualListKeys` | Enter/Space → selectBook or selectToc |
| `BooksSearchResults` (tiles) | `useTilesKeys` | `tabindex="0"` on `.tiles-grid` |
| `SearchResultsList` | `useVirtualListKeys` | Enter/Space → resultClick |
| `HebrewBooksPage` | `useListKeys` | Container-level nav, removed per-item tabindex |
| `TreeView` (TOC) | `useListKeys` | `tabindex="0"` on `.tree-entries`, Enter/Space → select |
| `TreeNode` | — | Accepts `focused` prop, has `data-nav-item` |
| `HebrewBooksListItem` | — | Accepts `focused` prop, has `data-nav-item`, removed own tabindex |

`CommentaryTreePanel` — out of scope. Recursive structure with checkboxes is a different interaction model.

---

## Key Design Decisions

### Composable contract — all three composables

- Enter **and** Space both fire `onActivate(index)` — standard accessibility behavior
- `onActivate` is optional — omit for lists where click is the only action
- Expand/collapse, Space-as-expand, and other node-level logic is **the component's responsibility**, not the composable's
- Each list instance owns its own `focusedIndex` state — no global singleton (important: multiple tabs open simultaneously)
- All composables use `useEventListener` from VueUse and clean up automatically

### `useVirtualListKeys` — scroll behavior

`align: 'auto'` is the correct option for "nearest" behavior in TanStack Virtual. Verified by reading the source (`node_modules/@tanstack/virtual-core/dist/esm/index.js`):

```js
// From getOffsetForIndex() in virtual-core source:
if (align === "auto") {
  if (item.end >= scrollOffset + size - scrollPaddingEnd) {
    align = "end"   // item below viewport → scroll so it appears at bottom edge
  } else if (item.start <= scrollOffset + scrollPaddingStart) {
    align = "start" // item above viewport → scroll so it appears at top edge
  } else {
    return [scrollOffset, align]  // item already fully visible → NO SCROLL AT ALL
  }
}
```

This is exactly equivalent to `scrollIntoView({ block: 'nearest' })`:
- Scrolling down: next item appears at the bottom edge — no page jump
- Scrolling up: previous item appears at the top edge
- Already visible: zero scroll movement

The concern about "always scrolls to top" only applies to `align: 'start'` (the old default). `align: 'auto'` is the right choice and was already in the composable.

One caveat: `align: 'auto'` requires the item to be in `measurementsCache`. For items far outside the overscan window, TanStack falls back to an estimated position. For keyboard navigation (one step at a time), the next item is always within the overscan buffer, so this is never a problem in practice.

### `useListKeys` — DOM-based scroll

Uses `el.scrollIntoView({ block: 'nearest' })` directly — the native browser equivalent. Items are found via `querySelectorAll('[data-nav-item]')` at key-press time, so the list always reflects the current DOM (handles filtered/collapsed trees correctly).

### `useTilesKeys` — column count

Column count is derived at key-press time from `containerEl.clientWidth / (72 + 12)` (tile width + gap). No ResizeObserver needed — it's always fresh when a key is pressed.

---

## Composable APIs

### `useVirtualListKeys`
```ts
const { focusedIndex } = useVirtualListKeys(
  scrollerEl,       // Ref<HTMLElement | null> — must have tabindex="0"
  getVirtualizer,   // () => Virtualizer<Element, Element>
  getCount,         // () => number
  onActivate?,      // (index: number) => void — Enter + Space
)
```
Keys: `ArrowDown`, `ArrowUp`, `Enter`, `Space`, `Ctrl+Home`, `Ctrl+End`

### `useListKeys`
```ts
const { focusedIndex } = useListKeys(
  containerEl,      // Ref<HTMLElement | null> — must have tabindex="0"
  getCount,         // () => number
  onActivate?,      // (index: number) => void — Enter + Space
  options?: { itemSelector?: string }  // default: '[data-nav-item]'
)
```
Keys: `ArrowDown`, `ArrowUp`, `Enter`, `Space`, `Home`, `End`

### `useTilesKeys`
```ts
const { focusedIndex } = useTilesKeys(
  containerEl,      // Ref<HTMLElement | null> — must have tabindex="0"
  getCount,         // () => number
  onActivate?,      // (index: number) => void — Enter + Space
)
```
Keys: `ArrowRight` (RTL prev), `ArrowLeft` (RTL next), `ArrowDown`, `ArrowUp`, `Enter`, `Space`, `Home`, `End`

---

## Remaining / Out of Scope

- `CommentaryTreePanel` — recursive tree with checkboxes, different interaction model, revisit separately
- `useVirtualScrollerKeys` — superseded by `useVirtualListKeys`. Can be deleted once confirmed no other callers remain.
