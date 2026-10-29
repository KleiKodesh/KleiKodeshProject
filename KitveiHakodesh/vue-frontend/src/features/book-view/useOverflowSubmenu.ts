import { ref, watch, nextTick, type Ref } from 'vue'
import { useResizeObserver } from '@vueuse/core'

/** Breathing room kept between a submenu and the viewport edges. */
const SUBMENU_MARGIN = 8

/**
 * A toolbar dropdown's panel while its control sits in the "more" flyout: a side submenu
 * beside its flyout row, like a context menu's.
 *
 * Fixed-positioned (the flyout scrolls, so an absolute child would be clipped by it) but
 * still a DOM child of the flyout, so a click in it is not an outside click that closes the
 * flyout. Opens toward the inline start (left, RTL); flips right when that side has no room,
 * and is clamped into the viewport either way.
 *
 * @param inOverflow - whether this instance renders as a flyout row
 * @param isOpen     - the dropdown's own open state
 * @param rowEl      - the flyout row (the dropdown's toggle)
 * @param panelEl    - the dropdown panel
 * @returns the inline style the panel wears while in the flyout, `undefined` on the toolbar
 */
export function useOverflowSubmenu(
  inOverflow: () => boolean,
  isOpen: Ref<boolean>,
  rowEl: Ref<HTMLElement | null>,
  panelEl: Ref<HTMLElement | null>,
) {
  const style = ref<Record<string, string> | undefined>(undefined)

  // Height capped at what the placement allows, so the max-height it sets does not read
  // as a resize and re-place forever.
  const sizeKey = (panel: HTMLElement) =>
    `${panel.offsetWidth}x${Math.min(panel.offsetHeight, window.innerHeight - 2 * SUBMENU_MARGIN)}`

  /** The panel size the current placement was computed for - see the resize observer below. */
  let placedSize = ''

  async function place() {
    style.value = { position: 'fixed', top: '0px', left: '0px', visibility: 'hidden' }
    await nextTick()
    const row = rowEl.value
    const panel = panelEl.value
    if (!row || !panel) return
    const m = SUBMENU_MARGIN
    const vw = window.innerWidth
    const vh = window.innerHeight
    const rowRect = row.getBoundingClientRect()
    const menuRect = (row.closest('.toolbar-overflow-menu') ?? row).getBoundingClientRect()
    const width = panel.offsetWidth
    placedSize = sizeKey(panel)
    const height = Math.min(panel.offsetHeight, vh - 2 * m)
    let left = menuRect.left - width
    if (left < m && menuRect.right + width <= vw - m) left = menuRect.right
    left = Math.max(m, Math.min(left, vw - width - m))
    const top = Math.max(m, Math.min(rowRect.top - 4, vh - height - m))
    style.value = {
      position: 'fixed',
      top: `${top}px`,
      left: `${left}px`,
      maxHeight: `${vh - 2 * m}px`,
      zIndex: '10000',
    }
  }

  watch(isOpen, (open) => {
    if (!inOverflow()) return
    if (open) void place()
    else style.value = undefined
  })

  // A panel whose content arrives after it opens (book info) grows after it was placed, so
  // the side picked for its first, smaller size can leave it over the flyout. Place again
  // when its size changes; comparing against the placed size keeps this from looping.
  useResizeObserver(panelEl, () => {
    const panel = panelEl.value
    if (!inOverflow() || !isOpen.value || !panel) return
    if (sizeKey(panel) !== placedSize) void place()
  })

  return style
}
