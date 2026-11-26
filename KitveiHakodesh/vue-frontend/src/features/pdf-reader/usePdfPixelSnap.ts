import { computed, onScopeDispose, ref, watch, type Ref } from 'vue'
import { useEventListener } from '@vueuse/core'
import { useHostResizeObserver } from '@/composables/useHostResizeObserver'
import type { EventBus } from 'pdfjs-dist/web/pdf_viewer.mjs'
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'

/**
 * Keeps every page canvas on whole screen pixels, so the screen shows PDF.js's drawing 1:1.
 *
 * PDF.js draws each page at the screen's pixel size, but the page sits where the layout puts it:
 * centred pages and the panels around them often land half a CSS pixel off, and at 125% / 150% /
 * 175% Windows scaling most CSS positions fall between screen pixels. The browser then resamples
 * the whole canvas and thin text turns grey (measured on a Talmud page at 125%: share of black
 * pixels 9.3% drawn -> 5.9% on screen, edge sharpness -16%; aligned: identical to the drawing).
 *
 * Fix: nudge each page by a relative offset of under one alignment step, so its canvas starts on a
 * screen pixel. The step is the smallest whole CSS length that is a whole number of screen pixels
 * (125% -> 4px, 150% -> 2px); offsets must be exact, a fractional CSS offset is rounded by layout
 * and stays blurred. Scrolling keeps the alignment (the browser scrolls by whole screen pixels),
 * so a scroll only aligns the pages it brings near the screen (PDF.js's updateviewarea).
 */
export function usePdfPixelSnap(container: Ref<HTMLElement | null>, eventBus: EventBus) {
  const hostWindow = useHostWindow()
  let frame = 0
  /** Bumped on each document open, so the pages box is looked up again. */
  const pagesInit = ref(0)
  const onPagesInit = () => pagesInit.value++
  eventBus.on('pagesinit', onPagesInit)

  function alignmentStep(dpr: number): number {
    for (let step = 1; step <= 16; step++) if (Math.abs(step * dpr - Math.round(step * dpr)) < 1e-6) return step
    return 1
  }

  /** The offset (>= 0, < step + 1) that moves a CSS position onto a screen pixel. */
  function offsetFor(position: number, dpr: number, step: number): number {
    const toWhole = Math.ceil(position - 1e-6) - position // layout positions are exact 1/64ths: this is exact
    let best = 0
    let bestMiss = Infinity
    for (let k = 0; k < step; k++) {
      const device = (position + toWhole + k) * dpr
      const miss = Math.abs(device - Math.round(device))
      if (miss < bestMiss - 1e-6) {
        bestMiss = miss
        best = k
      }
    }
    return toWhole + best
  }

  function snap() {
    frame = 0
    const el = container.value
    if (!el) return
    const dpr = hostWindow.devicePixelRatio || 1
    const step = alignmentStep(dpr)
    const view = el.getBoundingClientRect()
    // A right-to-left scroller wider than the view (pages zoomed wider than the window in the Hebrew
    // layout) scrolls from its far end: its scroll offset includes the overflow width, which can
    // end between screen pixels. The browser paints the content at that offset rounded to a whole
    // screen pixel while layout uses the exact one, so every page is painted off by the remainder,
    // and moving the pages cannot help (the overflow edge moves with them). Their layers are moved
    // back by it instead (a transform, painted exactly; pdfReaderTheme.css).
    const rtl = hostWindow.getComputedStyle(el).direction === 'rtl'
    const scrollX = (rtl ? el.scrollWidth - el.clientWidth : 0) + el.scrollLeft
    const paintFix = rtl ? (Math.round(scrollX * dpr) - scrollX * dpr) / dpr : 0
    for (const page of el.querySelectorAll<HTMLElement>('.pdfViewer .page')) {
      const target = page.querySelector<HTMLElement>('.canvasWrapper') ?? page
      const oldX = parseFloat(page.style.left) || 0
      const oldY = parseFloat(page.style.top) || 0
      const oldFix = parseFloat(page.style.getPropertyValue('--kk-snap-fix-x')) || 0
      const rect = target.getBoundingClientRect()
      // Only pages on screen or near it: the rest are aligned when scrolling brings them close.
      if (!rect.width || rect.bottom < view.top - view.height || rect.top > view.bottom + view.height) continue
      // Layout positions are whole 1/64ths of a pixel; the transform is not (and reads back inexactly).
      const x = offsetFor(Math.round((rect.left - oldX - oldFix) * 64) / 64, dpr, step)
      const y = offsetFor(rect.top - oldY + el.scrollTop, dpr, step)
      if (paintFix !== oldFix) page.style.setProperty('--kk-snap-fix-x', paintFix ? `${paintFix}px` : '')
      if (x === oldX && y === oldY) continue
      page.style.position = 'relative'
      page.style.left = x ? `${x}px` : ''
      page.style.top = y ? `${y}px` : ''
    }
  }

  function schedule() {
    if (!frame) frame = hostWindow.requestAnimationFrame(snap)
  }

  for (const name of ['pagesinit', 'scalechanging', 'pagerendered', 'rotationchanging', 'scrollmodechanged', 'spreadmodechanged', 'updateviewarea']) {
    eventBus.on(name, schedule)
  }
  useHostResizeObserver(container, schedule)
  // The pages box narrows when the vertical scrollbar appears (the scroller keeps its size),
  // which re-centres every page.
  const pagesBox = computed(() => (pagesInit.value, container.value?.querySelector<HTMLElement>('.pdfViewer') ?? null))
  useHostResizeObserver(pagesBox, schedule)
  useEventListener(hostWindow, 'resize', schedule)

  // Windows scaling changed (window moved to another screen): the step and the positions change.
  let dprQuery: MediaQueryList | null = null
  function watchDpr() {
    dprQuery?.removeEventListener('change', onDprChange)
    dprQuery = hostWindow.matchMedia(`(resolution: ${hostWindow.devicePixelRatio}dppx)`)
    dprQuery.addEventListener('change', onDprChange)
  }
  function onDprChange() {
    watchDpr()
    schedule()
  }
  watchDpr()
  watch(container, schedule)

  onScopeDispose(() => {
    eventBus.off('pagesinit', onPagesInit)
    for (const name of ['pagesinit', 'scalechanging', 'pagerendered', 'rotationchanging', 'scrollmodechanged', 'spreadmodechanged', 'updateviewarea']) {
      eventBus.off(name, schedule)
    }
    dprQuery?.removeEventListener('change', onDprChange)
    if (frame) hostWindow.cancelAnimationFrame(frame)
  })
}
