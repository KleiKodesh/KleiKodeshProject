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
import { writePdfTail } from '@/webview-host/bridge'
import type { PdfOutlineEntry, PdfOutlineEditEntry, PdfBridge } from '@/stores/bookViewStore'

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
  /** The bytes the viewer loaded. */
  getData(): Promise<Uint8Array>
  /** Loaded bytes + one incremental section (annotations + the edited outline). */
  saveDocument(): Promise<Uint8Array>
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
  /** Dirty check — wrapped by outline-search.js to include outline edits. */
  _hasChanges?: () => boolean
  /** Save routing: save() when dirty (incl. outline edits), download() otherwise. */
  downloadOrSave?: () => Promise<void>
}

/** The outline editor's host API, exposed by outline-search.js on the iframe window. */
interface KhOutlineEditor {
  getState(): { dirty: boolean; outline: PdfOutlineEditEntry[] }
  setState(state: { outline: PdfOutlineEditEntry[] }): void
  isDirty(): boolean
}

interface KhViewerWindow extends Window {
  __khOutlineHostNotify?: (state: { dirty: boolean; outline: PdfOutlineEditEntry[] }) => void
  __khOutlineEditor?: KhOutlineEditor
  __khSuppressUnloadPrompt?: boolean
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

/**
 * Start of the trailing outline-only section written by a TOC autosave, read from
 * its "%KH-TOC start=<S> prev=<P> [sha=<H>]" marker (patched pdf.worker.mjs
 * SaveDocument). Only a hint: the worker does the full proof (khReadTocMarker); when it
 * rejects the marker its save keeps every loaded byte, so writing saved[S..] at S
 * still reproduces the full file + appended section. Null when the file has none.
 */
function tocMarkerStart(bytes: Uint8Array): number | null {
  const tail = String.fromCharCode(...bytes.subarray(Math.max(0, bytes.length - 256)))
  const m = /%KH-TOC start=(\d+) prev=(\d+)(?: sha=[0-9a-f]{64})?\s*$/.exec(tail)
  if (!m) return null
  const start = Number(m[1])
  const prev = Number(m[2])
  return start > 0 && start < bytes.length && prev >= 0 && prev < start ? start : null
}

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
  // Bumped per attach — stale async work (the rehydrate poll) checks it.
  let attachGeneration = 0
  // The bridge object this instance registered — detach only unregisters its
  // OWN bridge, so a late detach (cross-pane moves, load-event ordering) can't
  // delete a bridge another pane just registered for the same tab.
  let registeredBridge: PdfBridge | null = null
  let pagechangingHandler: ((data: unknown) => void) | null = null
  let documentloadedHandler: ((data: unknown) => void) | null = null
  let applicationRef: PdfViewerApplication | null = null

  // ── TOC autosave: every outline edit is written straight into the PDF file ──
  // Only for a real local .pdf (not a Word file rendered to PDF, not a HebrewBooks
  // download, which have no writable source path). PDF.js's saveDocument() returns
  // the LOADED bytes + one incremental section; only that section is sent and it is
  // written at the loaded length, so each autosave of a session replaces the
  // previous one instead of appending (rules: Core PdfIncrementalSaveWriter).
  const AUTOSAVE_DELAY_MS = 600
  const ANCHOR_BYTES = 1024
  let autosavePath: string | null = null
  let autosaveTimer: ReturnType<typeof setTimeout> | null = null
  let autosaveRunning = false
  let autosaveAgain = false
  // Bumped on every dirty notify — a save only clears the dirty state when no
  // edit landed while it ran.
  let editGeneration = 0
  // Loaded length + its last bytes, read once per document (getData copies the file).
  let loadedTail: { offset: number; anchor: Uint8Array } | null = null
  let keydownHandler: ((e: KeyboardEvent) => void) | null = null

  function scheduleAutosave() {
    if (!autosavePath) return
    if (autosaveTimer) clearTimeout(autosaveTimer)
    autosaveTimer = setTimeout(() => {
      autosaveTimer = null
      void runAutosave()
    }, AUTOSAVE_DELAY_MS)
  }

  /** Write the current edits into the file now. True when written. */
  async function runAutosave(): Promise<boolean> {
    if (autosaveTimer) {
      clearTimeout(autosaveTimer)
      autosaveTimer = null
    }
    if (autosaveRunning) {
      autosaveAgain = true
      return false
    }
    const app = applicationRef
    const win = contentWindowRef
    const path = autosavePath
    const pdfDocument = app?.pdfDocument
    if (!app || !win || !path || !pdfDocument) return false
    autosaveRunning = true
    const startedGeneration = editGeneration
    try {
      if (!loadedTail) {
        const loaded = await pdfDocument.getData()
        // A file ending in our own outline-only section: the worker rebuilds the
        // save from the bytes before it, so the tail is written over it.
        const offset = tocMarkerStart(loaded) ?? loaded.length
        if (offset < ANCHOR_BYTES) return false
        loadedTail = { offset, anchor: loaded.slice(offset - ANCHOR_BYTES, offset) }
      }
      const { offset, anchor } = loadedTail
      const saved = await pdfDocument.saveDocument()
      // Sanity: the save must be the loaded bytes plus a tail.
      if (saved.length <= offset) return false
      const savedAnchor = saved.subarray(offset - ANCHOR_BYTES, offset)
      if (!savedAnchor.every((b, i) => b === anchor[i])) return false
      const error = await writePdfTail(path, offset, saved.subarray(offset), anchor)
      if (error) {
        console.warn('[pdf] TOC autosave failed:', error)
        return false
      }
      // Clears the viewer's dirty flag and the parked snapshot (notify dirty:false).
      if (app === applicationRef && startedGeneration === editGeneration) {
        win.document.dispatchEvent(new CustomEvent('kh-save-complete'))
      }
      return true
    } catch (e) {
      console.warn('[pdf] TOC autosave failed:', e)
      return false
    } finally {
      autosaveRunning = false
      if (autosaveAgain) {
        autosaveAgain = false
        scheduleAutosave()
      }
    }
  }

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
      registeredBridge = {
        get outlineEntries() { return pdfOutlineEntries },
        navigateToEntry: navigateToPdfEntry,
        // Live dirty state, read straight from the viewer. Covers annotation
        // edits AND outline edits (the viewer wraps _hasChanges to include
        // both). Only callable while this iframe is alive — the close guard
        // falls back to the parked snapshot otherwise.
        hasUnsavedChanges: () => {
          try {
            return applicationRef?._hasChanges?.() === true
          } catch {
            return false
          }
        },
        // Save-as for the close-guard dialog. Resolves true only when the
        // viewer signals the file was actually written (kh-save-complete —
        // dispatched by the patched _triggerDownload only for bytes that came
        // out of a successful saveDocument()); a cancelled picker emits
        // nothing and falls to the timeout.
        // A writable local PDF saves in place (the autosave, flushed now).
        saveAs: () =>
          autosavePath ? runAutosave() : new Promise<boolean>((resolve) => {
            const win = contentWindowRef
            const app = applicationRef
            if (!win || !app?.downloadOrSave) {
              resolve(false)
              return
            }
            const doc = win.document
            let settled = false
            const finish = (ok: boolean) => {
              if (settled) return
              settled = true
              clearTimeout(timer)
              doc.removeEventListener('kh-save-complete', onComplete)
              resolve(ok)
            }
            const onComplete = () => finish(true)
            // Generous: the File System Access picker has no cancel signal, so
            // a cancel simply times out as "not saved".
            const timer = setTimeout(() => finish(false), 120_000)
            doc.addEventListener('kh-save-complete', onComplete)
            void app.downloadOrSave().catch(() => finish(false))
          }),
      }
      bookViewStore.registerPdfBridge(tabId, registeredBridge)
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

    // ── Unsaved outline edits: eager snapshot + rehydrate ─────────────────────
    // The viewer pushes {dirty, outline} after EVERY edit (there is no reliable
    // teardown moment — tab switches navigate or destroy this iframe). Snapshots
    // are keyed by tab and file so a tab that navigated elsewhere and back only
    // rehydrates into the same document.
    const khWindow = contentWindow as KhViewerWindow
    const notifyTabId = tabId
    // Snapshot identity: HebrewBooks tabs have no localFilePath (only a
    // virtual URL + book id), so keying on path alone would collapse to
    // tab-only for them — a different HB book downloaded into the same tab
    // would rehydrate the previous book's TOC edits into it.
    const activeTab = paneNavigation.activeTab
    const notifyFilePath =
      activeTab?.localFilePath ??
      (activeTab?.localFileHbBookId ? `hb:${activeTab.localFileHbBookId}` : '')
    const sourcePath = activeTab?.localFilePath
    autosavePath = sourcePath && /\.pdf$/i.test(sourcePath) && !activeTab?.localFileHbBookId ? sourcePath : null
    loadedTail = null
    khWindow.__khOutlineHostNotify = (state) => {
      if (!notifyTabId) return
      bookViewStore.setPdfEditState(notifyTabId, {
        filePath: notifyFilePath,
        dirty: state.dirty,
        outline: state.outline,
      })
      if (state.dirty) {
        editGeneration++
        scheduleAutosave()
      }
    }

    // Ctrl+S on a writable PDF saves in place (no save-as picker). Capture phase
    // on the iframe window runs before PDF.js's own Ctrl+S handler.
    if (autosavePath) {
      keydownHandler = (e: KeyboardEvent) => {
        if (!(e.ctrlKey || e.metaKey) || e.shiftKey || e.altKey || e.code !== 'KeyS') return
        e.preventDefault()
        e.stopImmediatePropagation()
        if (applicationRef?._hasChanges?.()) void runAutosave()
      }
      contentWindow.addEventListener('keydown', keydownHandler, true)
    }

    // Rehydrate parked edits once the viewer's outline processing settles.
    // outline-search.js flags readiness via data-outline-loaded on its search
    // bar (button state is racy — see CUSTOMIZATIONS.md); poll briefly for it.
    const parked = notifyTabId ? bookViewStore.getPdfEditState(notifyTabId) : null
    if (parked && parked.dirty && parked.filePath === notifyFilePath) {
      const myGeneration = ++attachGeneration
      const tryRehydrate = (attempt: number) => {
        // Bail if a newer attach superseded this one. Comparing contentWindow
        // identity is NOT enough: a reused iframe keeps the same WindowProxy
        // across navigations, so a poll started for document A would otherwise
        // keep running after the iframe navigated to document B and inject A's
        // outline into B once B's readiness flag appears.
        if (myGeneration !== attachGeneration || contentWindowRef !== contentWindow) return
        const doc = contentWindow.document
        const settled =
          doc.getElementById('outlineSearchBar')?.dataset.outlineLoaded === '1' &&
          khWindow.__khOutlineEditor != null
        if (settled) {
          khWindow.__khOutlineEditor!.setState({ outline: parked.outline })
          // The parked edits never reached the file (the tab left mid-debounce) — write them now.
          editGeneration++
          scheduleAutosave()
          return
        }
        if (attempt < 100) setTimeout(() => tryRehydrate(attempt + 1), 100)
      }
      tryRehydrate(0)
    }

    // Already loaded — init immediately.
    if (application.pdfDocument) {
      initOutlineAndSync(application)
    }
  }

  function detach() {
    // Vue-initiated teardown: the snapshot already holds the edits (pushed on
    // every edit), so silence the iframe's own beforeunload prompt for the
    // upcoming navigation/removal. Without this, switching between two PDF tabs
    // pops the browser's native dialog — and cancelling it desyncs the iframe
    // from the already-switched tab state.
    if (contentWindowRef) {
      try {
        ;(contentWindowRef as KhViewerWindow).__khSuppressUnloadPrompt = true
      } catch {
        // contentWindow may already be gone — nothing to silence.
      }
    }
    attachGeneration++ // invalidate any in-flight rehydrate poll
    if (tabId && registeredBridge && bookViewStore.getPdfBridge(tabId) === registeredBridge) {
      bookViewStore.unregisterPdfBridge(tabId)
    }
    registeredBridge = null

    if (contentWindowRef) {
      const application = (
        contentWindowRef as unknown as { PDFViewerApplication: PdfViewerApplication }
      ).PDFViewerApplication

      if (application) {
        if (pagechangingHandler) application.eventBus._off('pagechanging', pagechangingHandler)
        if (documentloadedHandler) application.eventBus._off('documentloaded', documentloadedHandler)
      }
      if (keydownHandler) {
        try {
          contentWindowRef.removeEventListener('keydown', keydownHandler, true)
        } catch {
          // window already gone
        }
      }
    }
    // A pending debounce cannot finish once the iframe goes — the parked snapshot
    // (still dirty) carries those edits back and the next attach writes them.
    if (autosaveTimer) clearTimeout(autosaveTimer)
    autosaveTimer = null
    autosaveAgain = false
    autosavePath = null
    loadedTail = null
    keydownHandler = null

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
