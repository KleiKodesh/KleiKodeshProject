import type { ComputedRef, Ref } from 'vue'
import {
  keysToCollapse as keysToCollapseGeneric,
  useToolbarOverflow,
  TOOLBAR_BUTTON_SIZE,
} from '@/composables/useToolbarOverflow'

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
 * Worst-first: reference panels about the book (info, scanned editions, versions, marker
 * filter), then one-off actions and settings (export, sync, related books, clean text,
 * zoom), then the panel toggles, and last what reading itself runs on - bookmarks, search,
 * section navigation and the TOC.
 *
 * Every dropdown among them opens the same way in the flyout: its row opens the list as a
 * side submenu beside the flyout (useToolbarOverflowSubmenu), like a context menu's.
 *
 * Keys are internal names rather than labels, because none of these are destinations - the
 * flyout renders each one's face and the toolbar dispatches its action.
 */
export const TOOLBAR_OVERFLOW_ORDER = [
  'book-info',
  'hebrewbooks-editions',
  'versions',
  'word-link-markers',
  'export-to-word',
  'sync-commentaries',
  'related-books',
  'clean-text',
  'zoom',
  'side-commentaries',
  'bottom-commentary',
  'bookmarks',
  'search',
  'section-nav',
  'toc',
] as const

export type ToolbarOverflowKey = (typeof TOOLBAR_OVERFLOW_ORDER)[number]

/**
 * How much toolbar room a key is worth, in button-widths. The pairs - zoom out/in, the two
 * side-panel toggles, previous/next section - collapse as one control into one flyout row,
 * and the clean-text combo is its eraser plus chevron. Counting any of them as one would make
 * the toolbar think collapsing it saved less than it does, and stop collapsing early.
 */
export function widthOf(key: ToolbarOverflowKey): number {
  if (key === 'zoom' || key === 'side-commentaries' || key === 'section-nav') return 2
  if (key === 'clean-text') return CLEAN_TEXT_COMBO_WIDTH_IN_BUTTONS
  return 1
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

/** The book-view toolbar's fit - see useToolbarOverflow. */
export function useBookViewToolbarOverflow(
  toolbarEl: Ref<HTMLElement | null>,
  presentKeys: ComputedRef<ToolbarOverflowKey[]>,
  isVertical: ComputedRef<boolean>,
) {
  return useToolbarOverflow(toolbarEl, presentKeys, isVertical, widthOf)
}
