---
inclusion: manual
---

# Title Bar TOC Breadcrumb Dropdowns

## What We Want

The shell title bar (`AppTitleBar.vue`) already shows the active book's TOC path in the center as
`title · segment / segment / segment`. We want each `/` separator in the TOC path to become a
clickable chevron — exactly like the book catalog breadcrumb chevrons — that opens a dropdown
listing all siblings of the segment to its right. Clicking a sibling navigates the book view
directly to that TOC entry.

This only applies to `/book-view` tabs. For PDF tabs the `tocPath` is a plain page label string
with no navigable tree structure — leave it as non-interactive text.

---

## Data Flow Problem

`AppTitleBar` has no access to the TOC entry tree. It only sees `tab.tocPath` (a display string)
and `tab.bookId`. The TOC entries and the navigate function live inside `BookViewPage` /
`useBookView`, which is mounted below the title bar in the component tree.

The bridge between them must go through `bookViewStore` — the one store both the title bar and
the book view already share. **Do not query the database from the title bar or any layout
component.**

---

## Implementation Plan

### Step 1 — Add a TOC bridge to `bookViewStore`

Add a per-tab registration map to `bookViewStore`:

```
tocBridgeByTabId: Map<string, TocBridge>
```

where `TocBridge` is:

```ts
interface TocBridge {
  tocEntries: TocEntry[]          // flat list, same ref as useBookView exposes
  navigateToEntry: (entry: TocEntry) => void
}
```

Expose `registerTocBridge(tabId, bridge)` and `unregisterTocBridge(tabId)` on the store.
Expose a getter `getTocBridge(tabId): TocBridge | null`.

`TocEntry` is already exported from
`src/features/book-view/toc/useBookViewToc.ts`.

The `TocBridge` interface itself must live in `bookViewStore.ts` (or a companion
`bookViewTypes.ts` if the store file goes over its 300-line hard limit) — never in a `.vue`
file.

### Step 2 — Register the bridge in `useBookView`

In `useBookView.ts`, after `useToc` is set up:

- On `onMounted`: call `bookViewStore.registerTocBridge(tabId, { tocEntries, navigateToEntry })`
  where `navigateToEntry` calls `onTocSelect(entry)` (already exists in `useBookView`).
- On `onBeforeUnmount`: call `bookViewStore.unregisterTocBridge(tabId)`.

Use `watchEffect` or a `watch` on `tocEntries` so the bridge always reflects the current flat
list without re-registering.

### Step 3 — Parse `tocPath` into segments in `AppTitleBar`

`tocPath` is a `" / "`-separated string (built by `getTocPath` → `tocDisplayPath` in
`tocSearchUtils.ts`). Split it by `" / "` to get an array of segment labels.

For each segment label, find its corresponding `TocEntry` by matching `entry.text === label`
walking from the last matched parent down. This gives you the `TocEntry` for each segment,
from which you can get its `children` via the flat entry list (entries whose `parentId ===
entry.id`).

Build this segment-to-entry mapping as a `computed` in a new composable
`useAppTitleBarTocBreadcrumb.ts` in `src/layout/`. It takes:
- `activeTab` (reactive)
- `bookViewStore`

And returns:
- `segments: ComputedRef<TocBreadcrumbSegment[]>`

where:

```ts
interface TocBreadcrumbSegment {
  label: string
  tocEntry: TocEntry | null       // null if not found (graceful fallback)
  siblings: TocEntry[]            // children of the parent entry (or root entries if depth 0)
  isActive: boolean               // true for the last segment
}
```

### Step 4 — New component `AppTitleBarTocBreadcrumb.vue`

A new component in `src/layout/`. Receives `segments` as a prop and emits
`navigate-to-entry(entry: TocEntry)`.

Renders the title text as:
- Book title (non-interactive, existing `.bar-title-name` span)
- ` · ` separator
- For each segment: `label` text + a `BookCatalogBreadcrumbChevronDropdown`-style chevron
  between segments, listing siblings. The last segment has no chevron after it.

The dropdown chevron reuses the same `<Teleport to="body">` + `getBoundingClientRect` pattern
from `BookCatalogBreadcrumbChevronDropdown.vue`. Either extract a shared
`BreadcrumbChevronDropdown.vue` to `src/components/` (if both catalog and title bar use
it), or duplicate the small component into `src/layout/` under the name
`AppTitleBarBreadcrumbChevronDropdown.vue`.

The dropdown items are `TocEntry` objects — display `entry.text`.

### Step 5 — Wire into `AppTitleBar.vue`

In `AppTitleBar.vue`:

- Import `useAppTitleBarTocBreadcrumb` and the new `AppTitleBarTocBreadcrumb` component.
- Replace the existing `<span class="bar-toc-path">` with
  `<AppTitleBarTocBreadcrumb>` when `activeTab.route === '/book-view'` and
  `segments.length > 0`.
- Keep the plain `bar-toc-path` span for all other routes (PDF etc.).
- On `navigate-to-entry`, call `bookViewStore.getTocBridge(activeTabId)?.navigateToEntry(entry)`.

---

## Shared Chevron Dropdown Component Decision

`BookCatalogBreadcrumbChevronDropdown.vue` is specific to `CategoryNode`. The title bar needs
the same visual and behaviour but for `TocEntry`. The two node types have different shapes.

**Recommended approach:** extract a generic `BreadcrumbChevronDropdown.vue` to
`src/components/` that takes:

```ts
props: {
  items: { id: number; label: string }[]   // display-only shape
  activeItemId: number
}
emits: { select: [id: number] }
```

Then both `BookCatalogBreadcrumbChevronDropdown` and `AppTitleBarBreadcrumbChevronDropdown`
become thin wrappers that map their domain objects to `{ id, label }` and translate the
`select` id back to the domain object before emitting upward.

---

## Files to Create

| File | Purpose |
|------|---------|
| `src/layout/useAppTitleBarTocBreadcrumb.ts` | Parses tocPath into segments with sibling lists |
| `src/layout/AppTitleBarTocBreadcrumb.vue` | Renders the interactive breadcrumb in the title bar |
| `src/layout/AppTitleBarBreadcrumbChevronDropdown.vue` | Teleported chevron dropdown for TOC siblings (or promote to `src/components/BreadcrumbChevronDropdown.vue` if shared) |

## Files to Modify

| File | Change |
|------|--------|
| `src/stores/bookViewStore.ts` | Add `TocBridge`, `registerTocBridge`, `unregisterTocBridge`, `getTocBridge` |
| `src/features/book-view/useBookView.ts` | Register/unregister bridge on mount/unmount |
| `src/layout/AppTitleBar.vue` | Replace plain toc-path span with `AppTitleBarTocBreadcrumb` for book-view tabs |

---

## Constraints

- No database queries from the title bar or any layout component.
- No imports from `.vue` files into `.ts` files — `TocEntry` and `TocBridge` must come from
  `.ts` source files only.
- The `TocBridge` map in `bookViewStore` is in-memory only — never persisted.
- On tab close, `unregisterTocBridge` is called from `onBeforeUnmount` in `useBookView`, which
  already fires when the tab component is destroyed — no extra cleanup needed in the store.
- The dropdown uses `<Teleport to="body">` and `position: fixed` with coordinates from
  `getBoundingClientRect()` — same pattern as `BookCatalogBreadcrumbChevronDropdown.vue`.
- `useDropdownClose` must be used on every dropdown instance — never `onClickOutside` directly.
- The title bar already has `position: relative` and `z-index` handled by the
  `.title-bar-container` — the teleported dropdown bypasses all of this anyway.
