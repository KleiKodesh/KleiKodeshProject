import { computed, type ComputedRef, type Ref } from 'vue'
import { useElementSize } from '@vueuse/core'

/**
 * Geometry the fit arithmetic mirrors from the toolbar's CSS (BookViewToolbar and the
 * `--toolbar-*` variables in main.css) - a 28px button with no gap between buttons.
 * Change it there, change it here.
 */
const TOOLBAR_BUTTON_SIZE = 28
/**
 * The clean-text combo (BookViewCleanTextCombo) takes its eraser button plus the chevron (14px), its two separator borders (1px each) and
 * their padding (2px each). It is one pair: both halves always stay together.
 */
export const CLEAN_TEXT_COMBO_WIDTH_IN_BUTTONS = 1 + (14 + 2 * 1 + 2 * 2) / TOOLBAR_BUTTON_SIZE
/**
 * How far the anchored more button sits from the edge it is pinned to. Mirrors
 * `--toolbar-edge-inset` in BookViewToolbar.
 */
const TOOLBAR_EDGE_INSET = 2
/**
 * How much MORE room than it strictly needs a collapsed control must see before it returns.
 *
 * Only the restoring direction is damped, so a control never sits half in and half out: a
 * width that collapses it is a width that keeps it collapsed until there is real room again.
 * Small enough not to be noticeable, large enough to cover a splitter resting on a boundary.
 */
const TOOLBAR_HYSTERESIS_PX = 6
/**
 * The room the toolbar reserves at each end for the anchored more button - the button plus
 * its inset on both sides. It is padding rather than a laid-out child, so the buttons never
 * run under it and the arithmetic never has to charge for it: it is simply not part of the
 * length they share. Mirrors `--toolbar-reserved-edge`.
 */
const TOOLBAR_RESERVED_EDGE = TOOLBAR_BUTTON_SIZE + 2 * TOOLBAR_EDGE_INSET
/**
 * What the toolbar spends on itself before any button gets a pixel, subtracted from the
 * measured border box to leave the room the buttons actually share.
 *
 * A horizontal toolbar reserves the edge on BOTH sides, so its buttons stay centred on the
 * toolbar rather than drifting toward the side the button is not on. A vertical one packs
 * its buttons from the top and reserves only the end the button sits at: 2px at the top,
 * and the reserved edge at the bottom - `padding-block-end` REPLACES that end's 2px rather
 * than adding to it, so it is not 2px plus the edge. There is no border on the axis the
 * buttons run along - the toolbar's single border faces the content - so padding is the
 * whole of it.
 */
const TOOLBAR_HORIZONTAL_CHROME = 2 * TOOLBAR_RESERVED_EDGE
const TOOLBAR_VERTICAL_CHROME = 2 + TOOLBAR_RESERVED_EDGE

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
 * side submenu beside the flyout (useOverflowSubmenu), like a context menu's.
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

/**
 * Which controls have to collapse, worst-first, for a toolbar of `availableLengthPx` to fit.
 *
 * Pure and exported so the arithmetic can be tested at a boundary without a layout engine -
 * it failed once in a way review could not see and only the screen could: comparing a
 * FLOORED button count against a cost that carries the separators' fraction throws away up
 * to a whole button, so a second control collapsed alongside the first. Everything here is
 * therefore in pixels, with no rounding anywhere.
 *
 * Collapses one control at a time and stops the moment the rest fit, so a toolbar one button
 * short gives up one control. Working in widths rather than counts is what lets zoom pull
 * its own weight: it frees two buttons and costs one row, so collapsing it can end the loop
 * where collapsing a single button would not.
 *
 * @param availableLengthPx - room the toolbar's children share, along the axis they run on
 * @param pinnedInButtons   - what the non-collapsible children take, in button-widths
 * @param present           - the collapsible controls actually rendered, in collapse order
 */
export function keysToCollapse(
  availableLengthPx: number,
  pinnedInButtons: number,
  present: readonly ToolbarOverflowKey[],
  /**
   * What is collapsed right now. Only used to hold a decision that is on the boundary - see
   * TOOLBAR_HYSTERESIS_PX - so it can be omitted by anything asking what a width alone
   * implies.
   */
  currentlyCollapsed: readonly ToolbarOverflowKey[] = [],
): ToolbarOverflowKey[] {
  // Zero length = not measured yet (first render): assume room for everything rather than
  // flashing a collapsed toolbar for a frame.
  if (availableLengthPx <= 0) return []

  // The more button is not charged here at all. It is anchored outside the flex flow and the
  // toolbar reserves its room in padding (see .overflow-btn), so `availableLengthPx` is
  // already the room left AFTER it - the same room whether anything has collapsed or not.
  //
  // That is what makes one button of deficit cost exactly one control. While the button was
  // laid out in the row and charged a slot on first collapse, the control that collapsed
  // freed a button and the more button took it straight back: the deficit never moved and a
  // second control had to go with it. Anchoring it settled the arithmetic as well as the
  // position.
  const costOf = (key: ToolbarOverflowKey) => widthOf(key) * TOOLBAR_BUTTON_SIZE
  let needed =
    pinnedInButtons * TOOLBAR_BUTTON_SIZE + present.reduce((sum, key) => sum + costOf(key), 0)

  // A control comes BACK only once there is room for it plus the hysteresis margin. Without
  // it a pane parked within a pixel of a boundary - a splitter resting there, or the tail of
  // the collapse animation - flips a control in and out repeatedly, which is the one thing
  // worse than the jump this whole feature exists to avoid. Collapsing stays immediate: a
  // button that does not fit has to go now, not a few pixels later.
  const collapsed: ToolbarOverflowKey[] = []
  for (const key of present) {
    const wasCollapsed = currentlyCollapsed.includes(key)
    const budget = wasCollapsed ? availableLengthPx - TOOLBAR_HYSTERESIS_PX : availableLengthPx
    if (needed <= budget) break
    collapsed.push(key)
    needed -= costOf(key)
  }

  // Zoom frees more than two buttons, so collapsing it can leave room for a cheaper control
  // that went before it. Give those back - latest first - while they fit, so nothing sits in
  // the flyout with its own room still free on the toolbar.
  for (let i = collapsed.length - 2; i >= 0; i--) {
    const key = collapsed[i]!
    const budget = currentlyCollapsed.includes(key)
      ? availableLengthPx - TOOLBAR_HYSTERESIS_PX
      : availableLengthPx
    if (needed + costOf(key) > budget) continue
    needed += costOf(key)
    collapsed.splice(i, 1)
  }
  return collapsed
}

/**
 * Which of the toolbar's collapsible buttons fit its length, and which must collapse into
 * its "more" flyout.
 *
 * A toolbar too short for all its buttons does not clip them away invisibly (it has no
 * scrollbar): the buttons collapse into a "more" button, in the fixed worst-first order
 * above rather than in toolbar order, so what survives a squeeze is what a reader needs
 * most.
 *
 * The fit is computed from the toolbar's measured size, never observed off the buttons
 * themselves, so nothing has to render before it can be counted and the answer never
 * disagrees with itself mid-frame. Both axes are one arithmetic: the toolbar is a flex row
 * when docked top/bottom and a flex column when docked left/right, and its buttons are
 * square, so "how much room is there" is the width in one case and the height in the other.
 *
 * @param toolbarEl     - the toolbar's root element
 * @param presentKeys   - the collapsible controls the CURRENT book actually renders, in
 *                        TOOLBAR_OVERFLOW_ORDER. A book with no commentaries has no
 *                        sync button, and a control nothing renders must not be counted
 *                        against the fit.
 */
export function useBookViewToolbarOverflow(
  toolbarEl: Ref<HTMLElement | null>,
  presentKeys: ComputedRef<ToolbarOverflowKey[]>,
  isVertical: ComputedRef<boolean>,
) {
  // Measured as the BORDER box, then the toolbar's own chrome is taken off, rather than
  // asking for the content box directly: useElementSize seeds its refs from offsetWidth /
  // offsetHeight on mount and only switches to the observed box once the ResizeObserver
  // first fires, so a content-box read is a whole slot too large for that first frame. A
  // toolbar one button too long for one frame overflows its pane visibly, and the button is
  // not in the flyout either, because the arithmetic still counts it as fitting.
  const { width, height } = useElementSize(toolbarEl, undefined, { box: 'border-box' })

  /** The room the toolbar's flex children actually share, along the axis they run on. */
  const availableLength = computed(() =>
    isVertical.value
      ? Math.max(0, height.value - TOOLBAR_VERTICAL_CHROME)
      : Math.max(0, width.value - TOOLBAR_HORIZONTAL_CHROME),
  )

  // Feeds the previous answer back in so the hysteresis has something to hold. Read off a
  // plain variable rather than the computed itself, which cannot depend on its own value.
  let lastCollapsed: ToolbarOverflowKey[] = []

  /** The controls that had to collapse, worst-first - see keysToCollapse. */
  const overflowedKeys = computed<ToolbarOverflowKey[]>(() => {
    lastCollapsed = keysToCollapse(
      availableLength.value,
      0,
      presentKeys.value,
      lastCollapsed,
    )
    return lastCollapsed
  })

  const hasToolbarOverflow = computed(() => overflowedKeys.value.length > 0)

  /** Whether a given collapsible button still has its place on the toolbar itself. */
  function toolbarButtonVisible(key: ToolbarOverflowKey) {
    return !overflowedKeys.value.includes(key)
  }

  return { hasToolbarOverflow, overflowedKeys, toolbarButtonVisible }
}
