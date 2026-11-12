/**
 * The picked-up look of a folder being reordered: its icon lifts off (slightly larger,
 * with a shadow) and follows the pointer, the folder itself stays behind dimmed as the
 * gap it leaves, and the cursor is a closed hand for the whole drag — so the reader
 * feels they are carrying it, not just pressing a handle.
 *
 * Plain DOM, not a Vue component: the icon is a clone of whatever the view rendered
 * (tile, card, list row), so one helper serves all four catalog views.
 */
export interface ReorderDragGhost {
  moveTo(clientX: number, clientY: number): void
  remove(): void
}

const SOURCE_OPACITY = '0.35'
const GHOST_ICON_PX = 32

/**
 * Lifts the folder `item` (the element carrying data-reorder-id). Its icon is the
 * item's first svg outside `handle` — the move handle has an icon of its own.
 */
export function liftForReorder(item: HTMLElement, handle: HTMLElement, clientX: number, clientY: number): ReorderDragGhost {
  const icon = [...item.querySelectorAll<SVGElement>('svg')].find((svg) => !handle.contains(svg))
  const ghost = document.createElement('div')
  Object.assign(ghost.style, {
    position: 'fixed',
    left: '0',
    top: '0',
    width: `${GHOST_ICON_PX}px`,
    height: `${GHOST_ICON_PX}px`,
    zIndex: '10000',
    pointerEvents: 'none',
    filter: 'drop-shadow(0 6px 10px rgba(0, 0, 0, 0.35))',
    transition: 'scale 0.12s ease-out',
    scale: '1',
  })
  if (icon) {
    const copy = icon.cloneNode(true) as SVGElement
    // The theme pins svg colour through classes the copy no longer sits under.
    copy.style.color = getComputedStyle(icon).color
    copy.style.width = copy.style.height = '100%'
    copy.style.display = 'block'
    ghost.appendChild(copy)
  }
  document.body.appendChild(ghost)

  const sourceOpacity = item.style.opacity
  const sourceTransition = item.style.transition
  item.style.transition = 'opacity 0.12s ease'
  item.style.opacity = SOURCE_OPACITY
  document.documentElement.classList.add('is-reorder-dragging')

  // Centred on the pointer, nudged up-left so the hand does not hide it.
  function moveTo(x: number, y: number) {
    ghost.style.translate = `${x - GHOST_ICON_PX * 0.75}px ${y - GHOST_ICON_PX * 0.75}px`
  }
  moveTo(clientX, clientY)
  requestAnimationFrame(() => { ghost.style.scale = '1.15' })

  return {
    moveTo,
    remove() {
      ghost.remove()
      item.style.opacity = sourceOpacity
      item.style.transition = sourceTransition
      document.documentElement.classList.remove('is-reorder-dragging')
    },
  }
}
