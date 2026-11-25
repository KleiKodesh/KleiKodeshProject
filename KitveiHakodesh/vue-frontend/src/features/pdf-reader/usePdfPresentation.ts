import { ref, type Ref } from 'vue'
import { useEventListener } from '@vueuse/core'
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'
import { ScrollMode, SpreadMode, type PdfScroller } from './usePdfScroller'

/** Cursor hides after this long without mouse movement (PDF.js's DELAY_BEFORE_HIDING_CONTROLS). */
const CURSOR_HIDE_MS = 3000
/** Wheel distance (px) that turns one page; a gesture that keeps going turns again after the cooldown. */
const WHEEL_PAGE_DISTANCE = 100
const WHEEL_COOLDOWN_MS = 300
const LINE_HEIGHT_PX = 40

/** Clicks on these are the page's own (links, form fields), never a page turn. */
const INTERACTIVE = 'a, button, input, select, textarea, .annotationLayer section, .xfaLayer'

/**
 * Presentation mode: the pages full screen, one page (or spread) at a time.
 * As PDF.js's presentation mode: wheel, click (Shift+click / right-click back) and keys turn pages,
 * the cursor hides when the mouse rests. As the old viewer (legacy CUSTOMIZATIONS.md "Spread modes in
 * presentation mode", "Presentation mode zoom"): the reader's spread mode is kept and can be changed
 * (D), and Ctrl+wheel / pinch / Ctrl+plus-minus zoom (usePdfZoomInput) still work.
 * Leaving full screen (Esc) puts back the zoom, scroll and spread modes the reader had.
 */
export function usePdfPresentation(target: Ref<HTMLElement | null>, pdf: PdfScroller) {
  const hostWindow = useHostWindow()
  const active = ref(false)
  let saved: { preset: string | number; scroll: number; spread: number } | null = null

  async function start() {
    const el = target.value
    const v = pdf.viewer.value
    if (!el || !v || active.value) return
    saved = { preset: pdf.preset.value ?? pdf.scale.value, scroll: pdf.scrollMode.value, spread: pdf.spreadMode.value }
    await el.requestFullscreen()
    active.value = true
    pdf.setScrollMode(ScrollMode.PAGE)
    // The spread mode stays as the reader had it (old viewer); PDF.js forces single pages.
    pdf.setPreset('page-fit')
    showCursor()
  }

  useEventListener(target, 'fullscreenchange', () => {
    const doc = target.value?.ownerDocument
    if (doc?.fullscreenElement || !active.value) return
    active.value = false
    showCursor(false)
    if (!saved) return
    pdf.setScrollMode(saved.scroll)
    pdf.setSpreadMode(saved.spread)
    if (typeof saved.preset === 'number') pdf.setScale(saved.preset)
    else pdf.setPreset(saved.preset as Parameters<PdfScroller['setPreset']>[0])
    saved = null
  })

  // Cursor: hidden after a rest, back on any movement.
  let hideTimer: number | undefined
  function showCursor(rearm = true) {
    const el = target.value
    hostWindow.clearTimeout(hideTimer)
    if (el) el.style.cursor = ''
    if (!rearm) return
    hideTimer = hostWindow.setTimeout(() => {
      if (active.value && target.value) target.value.style.cursor = 'none'
    }, CURSOR_HIDE_MS)
  }
  useEventListener(target, 'mousemove', () => { if (active.value) showCursor() }, { passive: true })

  const turn = (delta: number) => pdf.goToPage(Math.min(pdf.pageCount.value, Math.max(1, pdf.pageNumber.value + delta)))
  // A spread shows two pages: step by the spread (PDF.js's nextPage/previousPage do the same).
  const step = () => (pdf.spreadMode.value === SpreadMode.NONE ? 1 : 2)

  // Wheel: one page per notch-sized distance. Ctrl+wheel (zoom, pinch) is left to usePdfZoomInput.
  let wheelDistance = 0
  let lastTurnAt = -Infinity
  useEventListener(target, 'wheel', (e: WheelEvent) => {
    if (!active.value || e.ctrlKey || e.metaKey) return
    e.preventDefault()
    const unit = e.deltaMode === 1 ? LINE_HEIGHT_PX : e.deltaMode === 2 ? WHEEL_PAGE_DISTANCE : 1
    const delta = (Math.abs(e.deltaY) >= Math.abs(e.deltaX) ? e.deltaY : e.deltaX) * unit
    if (Math.sign(delta) !== Math.sign(wheelDistance)) wheelDistance = 0
    wheelDistance += delta
    if (Math.abs(wheelDistance) < WHEEL_PAGE_DISTANCE || e.timeStamp - lastTurnAt < WHEEL_COOLDOWN_MS) return
    turn(Math.sign(wheelDistance) * step())
    wheelDistance = 0
    lastTurnAt = e.timeStamp
  }, { passive: false })

  // Click: next page; Shift+click or right-click: previous. Links and form fields keep their click.
  useEventListener(target, 'mousedown', (e: MouseEvent) => {
    if (!active.value) return
    if ((e.target as Element | null)?.closest?.(INTERACTIVE)) return
    if (e.button === 0) {
      e.preventDefault()
      turn((e.shiftKey ? -1 : 1) * step())
    } else if (e.button === 2) {
      e.preventDefault()
      turn(-step())
    }
  })
  useEventListener(target, 'contextmenu', (e: MouseEvent) => {
    if (active.value && !(e.target as Element | null)?.closest?.(INTERACTIVE)) {
      e.preventDefault()
      e.stopPropagation()
    }
  }, { capture: true })

  // Keys. Arrows are mapped for right-to-left books (Hebrew): Left = forward, Right = back,
  // the opposite of PDF.js's left-to-right mapping; Up/Down and PageUp/PageDown as PDF.js.
  useEventListener(target, 'keydown', (e: KeyboardEvent) => {
    if (!active.value || e.ctrlKey || e.metaKey || e.altKey) return
    showCursor()
    if ((e.target as Element | null)?.closest?.('input, select, textarea, [contenteditable="true"]')) return
    if (e.code === 'Home') pdf.goToPage(1)
    else if (e.code === 'End') pdf.goToPage(pdf.pageCount.value)
    else if (e.code === 'KeyD') pdf.setSpreadMode((pdf.spreadMode.value + 1) % 3)
    else if (['ArrowDown', 'ArrowLeft', 'PageDown', 'KeyN', 'KeyJ', 'Enter'].includes(e.code) || (e.code === 'Space' && !e.shiftKey)) turn(step())
    else if (['ArrowUp', 'ArrowRight', 'PageUp', 'KeyP', 'KeyK', 'Backspace'].includes(e.code) || (e.code === 'Space' && e.shiftKey)) turn(-step())
    else return
    e.preventDefault()
  })

  return { active, start }
}
