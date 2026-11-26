import type { ComputedRef, Ref } from 'vue'
import {
  keysToCollapse as keysToCollapseGeneric,
  TOOLBAR_BUTTON_SIZE,
} from '@/composables/useToolbarOverflow'
import { useGroupedToolbar, itemWidth, type GroupedToolbarItem } from '@/composables/useGroupedToolbar'

/* The book-view toolbar's collapsible controls. The fit arithmetic itself is shared
   (composables/useToolbarOverflow.ts); this file names the controls and their widths. */

/**
 * The clean-text combo (BookViewCleanTextCombo) takes its eraser button plus the chevron (14px), its two separator borders (1px each) and
 * their padding (2px each). It is one pair: both halves always stay together.
 */
export const CLEAN_TEXT_COMBO_WIDTH_IN_BUTTONS = 1 + (14 + 2 * 1 + 2 * 2) / TOOLBAR_BUTTON_SIZE
/**
 * Every control on the toolbar, in the order it collapses - the FIRST key here is the first
 * to go. No control is pinned: a pane narrow enough loses all of them into the flyout, the
 * most essential last.
 *
 * Book info is not here: it always lives in the flyout (BookViewToolbar), never on the bar.
 *
 * Worst-first: reference panels about the book (scanned editions, versions, marker
 * filter), then one-off actions and settings (export, sync, related books, clean text,
 * zoom), then the panel toggles, and last what reading itself runs on - bookmarks, search,
 * section navigation and the TOC.
 *
 * Every dropdown among them becomes a row with a submenu arrow in the "more" menu (its
 * moreMenuRow, useToolbarMoreMenu), like the PDF toolbar's.
 *
 * Keys are internal names rather than labels, because none of these are destinations - the
 * flyout renders each one's face and the toolbar dispatches its action.
 */
export const TOOLBAR_OVERFLOW_ORDER = [
  'hebrewbooks-editions',
  'versions',
  'word-link-markers',
  'export-to-word',
  'read-aloud',
  'sync-commentaries',
  'related-books',
  'clean-text',
  'lines-layout',
  'zoom',
  'side-commentaries',
  'bottom-commentary',
  'bookmarks',
  'search',
  'first-last',
  'section-nav',
  'toc',
] as const

export type ToolbarOverflowKey = (typeof TOOLBAR_OVERFLOW_ORDER)[number]

/**
 * The toolbar's controls in the order it shows them, grouped and placed in zones as the PDF
 * toolbar's (useGroupedToolbar): reading navigation at the start (right, RTL); search, the
 * text's display and the commentary panels in the centre; the book's references and tools at the end (left).
 * The "more" flyout lists its rows in this order, a separator between groups.
 */
export const TOOLBAR_ITEMS: readonly GroupedToolbarItem<ToolbarOverflowKey>[] = [
  { key: 'toc', group: 'navigation', zone: 'start' },
  { key: 'section-nav', group: 'navigation', zone: 'start', width: 2 },
  { key: 'first-last', group: 'navigation', zone: 'start', width: 2 },
  { key: 'bookmarks', group: 'navigation', zone: 'start' },
  { key: 'related-books', group: 'book', zone: 'start' },
  { key: 'versions', group: 'book', zone: 'start' },
  { key: 'hebrewbooks-editions', group: 'book', zone: 'start' },
  { key: 'search', group: 'search', zone: 'center' },
  { key: 'zoom', group: 'display', zone: 'center', width: 2 },
  { key: 'side-commentaries', group: 'commentary', zone: 'center', width: 2 },
  { key: 'bottom-commentary', group: 'commentary', zone: 'center' },
  { key: 'sync-commentaries', group: 'commentary', zone: 'center' },
  { key: 'clean-text', group: 'view', zone: 'end', width: CLEAN_TEXT_COMBO_WIDTH_IN_BUTTONS },
  { key: 'lines-layout', group: 'view', zone: 'end' },
  { key: 'word-link-markers', group: 'book', zone: 'end' },
  { key: 'read-aloud', group: 'tools', zone: 'end' },
  { key: 'export-to-word', group: 'tools', zone: 'end' },
]

/**
 * How much toolbar room a key is worth, in button-widths. The pairs - zoom out/in, the two
 * side-panel toggles, previous/next section - collapse as one control into one flyout row,
 * and the clean-text combo is its eraser plus chevron. Counting any of them as one would make
 * the toolbar think collapsing it saved less than it does, and stop collapsing early.
 */
export function widthOf(key: ToolbarOverflowKey): number {
  return itemWidth(TOOLBAR_ITEMS.find((it) => it.key === key))
}

/** Which book-view controls have to collapse - the shared arithmetic over this toolbar's widths. */
export function keysToCollapse(
  availableLengthPx: number,
  pinnedInButtons: number,
  present: readonly ToolbarOverflowKey[],
  currentlyCollapsed: readonly ToolbarOverflowKey[] = [],
): ToolbarOverflowKey[] {
  return keysToCollapseGeneric(availableLengthPx, pinnedInButtons, present, widthOf, currentlyCollapsed)
}

/** The book-view toolbar's fit and groups - see useGroupedToolbar. */
export function useBookViewToolbarOverflow(
  toolbarEl: Ref<HTMLElement | null>,
  isPresent: (key: ToolbarOverflowKey) => boolean,
  isVertical: ComputedRef<boolean>,
) {
  return useGroupedToolbar(toolbarEl, TOOLBAR_ITEMS, TOOLBAR_OVERFLOW_ORDER, isPresent, isVertical)
}
