/**
 * Tracks the current page in the PDF.js iframe and updates the active tab's
 * `tocPath` with the matching outline (TOC) entry — exactly like the book view
 * shows the active TOC breadcrumb as the user scrolls.
 *
 * When the PDF has an outline, the displayed path is the title of the deepest
 * outline entry whose destination page is ≤ the current page, prefixed by its
 * ancestor titles separated by " · ". When the PDF has no outline, falls back
 * to "עמוד X מתוך Y".
 *
 * Usage: call `attach(contentWindow)` after the iframe's `load` event; the
 * composable initialises the outline index once the PDF document is ready, then
 * listens to `pagechanging` events. Call `detach()` before the iframe is torn
 * down or when the component unmounts.
 */
import { onBeforeUnmount } from 'vue'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { useBookViewStore } from '@/stores/bookViewStore'
import type { PdfOutlineEntry } from '@/stores/bookViewStore'

// PDF.js types — just enough for what we access at runtime.
interface PdfDestinationRef {
  num: number
  gen: number
}
type PdfDestination = Array<PdfDestinationRef | string | number | null>

interface PdfOutlineItem {
  title: string
  dest: PdfDestination | string | null
  items: PdfOutlineItem[]
}

interface PdfDocument {
  getOutline(): Promise<PdfOutlineItem[] | null>
  getDestination(dest: string): Promise<PdfDestination | null>
  getPageIndex(ref: PdfDestinationRef): Promise<number>
}

interface PdfViewerApplication {
  pdfDocument: PdfDocument | null
  pagesCount: number
  page: number
  initialized: boolean
  eventBus: {
    _on(event: string, handler: (data: unknown) => void): void
    _off(event: string, handler: (data: unknown) => void): void
  }
}

// A flat, sorted record of an outline entry with its resolved page number.
interface OutlineEntry {
  pageNumber: number // 1-based
  path: string // full breadcrumb, e.g. "פרק א · סימן ב"
}

async function buildOutlineIndex(
  pdfDocument: PdfDocument,
): Promise<OutlineEntry[]> {
  const outline = await pdfDocument.getOutline()
  if (!outline || outline.length === 0) return []

  // ABBYY FineReader and some other PDF producers store Hebrew outline titles
  // with trailing punctuation ("מח.") encoded in visual/LTR order as leading
  // punctuation (".מח"). Detect this: if the title starts with punctuation
  // followed by a Hebrew letter, move the leading punctuation to the end.
  function normalizeOutlineTitle(raw: string): string {
    const trimmed = raw.trim()
    const match = trimmed.match(/^([.,:;!?()\[\]{}"']+)(\p{Script=Hebrew}.*)$/u)
    return (match && match[1] !== undefined && match[2] !== undefined) ? match[2] + match[1] : trimmed
  }

  // Step 1 — flatten the outline tree into a list of { dest, path } synchronously.
  // No async here — just traverse the tree and build the full breadcrumb strings.
  interface FlatItem {
    dest: PdfDestination | string | null
    path: string
  }
  const flatItems: FlatItem[] = []

  function collectItems(items: PdfOutlineItem[], ancestorTitles: string[]): void {
    for (const item of items) {
      const title = normalizeOutlineTitle(item.title ?? '')
      const fullPath = [...ancestorTitles, title].filter(Boolean).join(' · ')
      flatItems.push({ dest: item.dest, path: fullPath })
      if (item.items && item.items.length > 0) {
        collectItems(item.items, [...ancestorTitles, title])
      }
    }
  }
  collectItems(outline, [])

  // Step 2 — resolve all destinations in parallel. One Promise.all instead of
  // sequential awaits — all getPageIndex calls fire simultaneously.
  async function resolveDestToPageNumber(
    dest: PdfDestination | string | null,
  ): Promise<number | null> {
    if (dest === null) return null
    let explicitDest: PdfDestination | null = null

    if (typeof dest === 'string') {
      explicitDest = await pdfDocument.getDestination(dest)
    } else {
      explicitDest = dest
    }

    if (!explicitDest || explicitDest.length === 0) return null

    const destReference = explicitDest[0]
    if (destReference === null || destReference === undefined) return null

    if (typeof destReference === 'number') {
      return destReference + 1
    }

    try {
      const zeroBasedIndex = await pdfDocument.getPageIndex(destReference as PdfDestinationRef)
      return zeroBasedIndex + 1
    } catch {
      return null
    }
  }

  const pageNumbers = await Promise.all(flatItems.map((item) => resolveDestToPageNumber(item.dest)))

  // Step 3 — zip and filter, then sort.
  const entries: OutlineEntry[] = []
  for (let index = 0; index < flatItems.length; index++) {
    const pageNumber = pageNumbers[index]
    const flatItem = flatItems[index]
    if (pageNumber != null && flatItem && flatItem.path) {
      entries.push({ pageNumber, path: flatItem.path })
    }
  }

  // Sort by page ascending. On ties sort by depth ascending so the deepest
  // entry for a given page is last — the backwards lookup returns it first.
  entries.sort((a: OutlineEntry, b: OutlineEntry) => {
    if (a.pageNumber !== b.pageNumber) {
      return a.pageNumber - b.pageNumber
    }
    return a.path.split(' · ').length - b.path.split(' · ').length
  })
  return entries
}

// NOTE: the sidebar outline tree's mangled titles used to be corrected here too,
// on first open of the outline panel. That now happens inside the viewer itself
// (public/pdfjs/web/outline-search.js — normalizeOutlineDom), which runs earlier
// (on `outlineloaded`, rather than waiting for the user to open the panel) and
// keeps the rendered rows and the panel's search index consistent. The
// `normalizeOutlineTitle` above is still needed here for the breadcrumb, which
// is built from the outline data rather than from the sidebar DOM.

function findActiveOutlineEntry(
  entries: OutlineEntry[],
  currentPage: number,
): OutlineEntry | null {
  // Walk backwards to find the last entry whose page ≤ current page.
  for (let index = entries.length - 1; index >= 0; index--) {
    const entry = entries[index]
    if (entry !== undefined && entry.pageNumber <= currentPage) {
      return entry
    }
  }
  return null
}

export function usePdfViewPageTracking() {
  const paneNavigation = usePaneNavigation()
  const bookViewStore = useBookViewStore()

  let contentWindowRef: Window | null = null
  let outlineEntries: OutlineEntry[] = []
  let pdfOutlineEntries: PdfOutlineEntry[] = []
  let tabId: string | null = null
  let pendingPage: { pageNumber: number } | null = null
  let pagechangingHandler: ((data: unknown) => void) | null = null
  let documentloadedHandler: ((data: unknown) => void) | null = null
  let applicationRef: PdfViewerApplication | null = null

  function buildPdfOutlineEntries(entries: OutlineEntry[]): PdfOutlineEntry[] {
    // Deduplicate by path — keep first occurrence (lowest page number due to sort order).
    const seen = new Set<string>()
    const result: PdfOutlineEntry[] = []
    for (let index = 0; index < entries.length; index++) {
      const entry = entries[index]!
      if (seen.has(entry.path)) continue
      seen.add(entry.path)
      const segments = entry.path.split(' · ')
      const text = segments[segments.length - 1] ?? entry.path
      const parentPath = segments.length > 1 ? segments.slice(0, -1).join(' · ') : ''
      result.push({ id: index, text, fullPath: entry.path, parentPath })
    }
    return result
  }

  function navigateToPdfEntry(entry: PdfOutlineEntry) {
    // Find the first flat outline entry with this path and jump to its page.
    const flat = outlineEntries.find((e) => e.path === entry.fullPath)
    if (!flat || !applicationRef) return
    applicationRef.page = flat.pageNumber
  }

  // Remove debug logging now that we have the answer
  function writeTocPath(currentPage: number) {
    const outlineEntry = findActiveOutlineEntry(outlineEntries, currentPage)
    if (outlineEntry) {
      paneNavigation.updateActiveTab({ tocPath: outlineEntry.path })
    } else if (outlineEntries.length === 0) {
      pendingPage = { pageNumber: currentPage }
    } else {
      paneNavigation.updateActiveTab({ tocPath: undefined })
    }
  }

  async function initOutlineAndSync(application: PdfViewerApplication) {
    const pdfDocument = application.pdfDocument
    if (!pdfDocument) return

    outlineEntries = await buildOutlineIndex(pdfDocument)
    pdfOutlineEntries = buildPdfOutlineEntries(outlineEntries)

    if (tabId) {
      bookViewStore.registerPdfBridge(tabId, {
        get outlineEntries() { return pdfOutlineEntries },
        navigateToEntry: navigateToPdfEntry,
      })
    }

    // Apply any page change that arrived while the index was being built.
    const pending = pendingPage
    pendingPage = null
    const pageToSync = pending?.pageNumber ?? application.page
    writeTocPath(pageToSync)
  }

  function attach(contentWindow: Window) {
    detach()
    contentWindowRef = contentWindow
    tabId = paneNavigation.activeTabId

    const application = (contentWindow as unknown as { PDFViewerApplication: PdfViewerApplication })
      .PDFViewerApplication

    if (!application) return
    applicationRef = application

    // pagechanging is only dispatched on the internal eventBus — never on the
    // DOM window. Subscribe via eventBus directly.
    pagechangingHandler = (data: unknown) => {
      const event = data as { pageNumber: number }
      writeTocPath(event.pageNumber)
    }
    application.eventBus._on('pagechanging', pagechangingHandler)

    documentloadedHandler = () => {
      initOutlineAndSync(application)
    }
    application.eventBus._on('documentloaded', documentloadedHandler)

    // Already loaded — init immediately.
    if (application.pdfDocument) {
      initOutlineAndSync(application)
    }
  }

  function detach() {
    if (tabId) {
      bookViewStore.unregisterPdfBridge(tabId)
    }

    if (contentWindowRef) {
      const application = (
        contentWindowRef as unknown as { PDFViewerApplication: PdfViewerApplication }
      ).PDFViewerApplication

      if (application) {
        if (pagechangingHandler) application.eventBus._off('pagechanging', pagechangingHandler)
        if (documentloadedHandler) application.eventBus._off('documentloaded', documentloadedHandler)
      }
    }

    contentWindowRef = null
    applicationRef = null
    tabId = null
    pagechangingHandler = null
    documentloadedHandler = null
    pendingPage = null
    outlineEntries = []
    pdfOutlineEntries = []
  }

  onBeforeUnmount(() => {
    detach()
  })

  return { attach, detach }
}
