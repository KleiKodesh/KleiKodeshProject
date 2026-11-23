// Fixed-position anchor for popups opened from the home bottom bar: centered
// over the trigger, clamped so it never escapes the viewport, and pinned just
// above the bar.
export const BAR_POPUP_MARGIN = 8

export interface BarPopupAnchor {
  left: number
  bottom: number
  maxHeight: number
}

export function computeBarPopupAnchor(button: HTMLElement, popupWidth: number): BarPopupAnchor {
  const rect = button.getBoundingClientRect()
  const center = rect.left + rect.width / 2
  let left = center - popupWidth / 2
  const maxLeft = window.innerWidth - popupWidth - BAR_POPUP_MARGIN
  left = Math.min(Math.max(left, BAR_POPUP_MARGIN), Math.max(BAR_POPUP_MARGIN, maxLeft))
  return {
    left,
    bottom: window.innerHeight - rect.top + BAR_POPUP_MARGIN,
    // The popup opens upward from just above the bar; cap its height to the space
    // between the top margin and the bar so it never overflows the top.
    maxHeight: Math.max(120, rect.top - BAR_POPUP_MARGIN * 2),
  }
}
