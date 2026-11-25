import { computed, ref, type ShallowRef } from 'vue'
import type { EventBus, PDFViewer } from 'pdfjs-dist/web/pdf_viewer.mjs'

interface Place { pageNumber: number; top?: number; left?: number }

const MAX_ENTRIES = 100

/**
 * Back / forward between the places a jump left (internal links, outline, bookmarks, comments,
 * the first search result). Our own stack, not PDF.js's PDFHistory: that one drives the page's
 * window.history, which belongs to the app's tabs. `linkHistory` is what PDFLinkService.setHistory
 * takes: the link service calls pushCurrentPosition() before each jump it makes.
 */
export function createPdfJumpHistory(eventBus: EventBus, viewer: ShallowRef<PDFViewer | null>) {
  let current: Place | null = null
  eventBus.on('updateviewarea', (e: { location?: Place }) => {
    if (e.location) current = { pageNumber: e.location.pageNumber, top: e.location.top, left: e.location.left }
  })
  const back = ref<Place[]>([])
  const forward = ref<Place[]>([])
  const canGoBack = computed(() => back.value.length > 0)
  const canGoForward = computed(() => forward.value.length > 0)

  const same = (a: Place | undefined, b: Place) =>
    !!a && a.pageNumber === b.pageNumber && Math.round(a.top ?? -1) === Math.round(b.top ?? -1)

  /** Remember where the reader is now, before a jump. */
  function pushCurrentPosition() {
    const here = current ?? (viewer.value ? { pageNumber: viewer.value.currentPageNumber } : null)
    if (!here || same(back.value.at(-1), here)) return
    back.value = [...back.value.slice(-(MAX_ENTRIES - 1)), here]
    forward.value = []
  }

  function show(place: Place) {
    const v = viewer.value
    if (!v) return
    if (place.top == null) v.currentPageNumber = place.pageNumber
    else v.scrollPageIntoView({ pageNumber: place.pageNumber, destArray: [null, { name: 'XYZ' }, place.left ?? null, place.top, null], allowNegativeOffset: true })
  }

  /** Step back (or forward); false when there is nowhere to go. */
  function step(toBack: boolean): boolean {
    const from = toBack ? back : forward
    const to = toBack ? forward : back
    const place = from.value.at(-1)
    if (!place || !viewer.value) return false
    from.value = from.value.slice(0, -1)
    const here = current ?? { pageNumber: viewer.value.currentPageNumber }
    to.value = [...to.value, here]
    show(place)
    return true
  }

  function reset() {
    back.value = []
    forward.value = []
    current = null
  }

  return {
    canGoBack, canGoForward, pushCurrentPosition, reset,
    goBack: () => step(true),
    goForward: () => step(false),
    /** For PDFLinkService.setHistory: the link service only calls these three. */
    linkHistory: { pushCurrentPosition, push: () => {}, pushPage: () => {} },
  }
}
