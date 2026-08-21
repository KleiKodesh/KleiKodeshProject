/**
 * Warms the data an export needs, which the reading views deliberately load lazily.
 *
 * The views only ever load notes and word-link anchors for the lines currently
 * scrolled into view — that is what keeps opening a book instant. A copy does not
 * respect that boundary: "select all" covers a whole book, most of which was never
 * rendered, so its notes were never fetched and its citations were never spliced
 * into the markup at all. Without a warm-up, copy-with-notes would silently export
 * whatever the user happened to have scrolled past.
 *
 * The copy event itself is synchronous, so the work splits in two:
 *
 *   prepareForLines()      async, awaited by the copy ACTIONS (menu items, Ctrl+C,
 *                          paste-into-Word) before the clipboard event is fired.
 *   resolveWordLinkTarget() synchronous, called while building the HTML.
 *
 * Everything is keyed by line id and cached for the life of the view, so repeating
 * the same copy costs nothing, and a citation whose target never loaded is dropped
 * from the export rather than emitted half-formed (see applyWordLinkExport).
 */
import { onScopeDispose } from 'vue'
import { getLineContents, getTocPathsForLines } from '@/webview-host/seforimApi'
import type { WordLinkAnchor } from '@/webview-host/queries.types'
import type { WordLinkTarget } from './wordLinkAnchors'
import type { WordLinkTargetContent } from './wordLinkExport'

/**
 * Keeps every batched query's bound-parameter list well inside SQLite's limit. The
 * lazy per-viewport loaders never needed this — they ask for a screenful — but a
 * select-all warm-up asks for the whole book at once.
 */
const CHUNK = 400

function chunked(ids: number[]): number[][] {
  const out: number[][] = []
  for (let i = 0; i < ids.length; i += CHUNK) out.push(ids.slice(i, i + CHUNK))
  return out
}

export function useCopyExportData(opts: {
  /** Immediate (un-debounced) loaders from the view's own lazy stores. */
  loadNotes?: (lineIds: number[]) => Promise<void>
  loadWordLinkAnchors?: (lineIds: number[]) => Promise<void>
  getWordLinkAnchorsForLine?: (lineId: number) => WordLinkAnchor[]
  getBookTitle: (bookId: number) => string
}) {
  // Citation target line id → its full content and TOC path.
  const targetsByLineId = new Map<number, { html: string; tocPath: string }>()
  // Target line ids already queried, whether or not a content row came back. Without
  // this, a target with no content row is re-fetched on every single copy for the life
  // of the view, since `targetsByLineId` only records hits.
  const queriedTargets = new Set<number>()
  // In-flight load per target line id, so two overlapping copy actions share one query
  // instead of both fetching the same targets.
  const inFlight = new Map<number, Promise<void>>()
  // A select-all warm-up can walk a whole book's citation targets in sequence; closing the
  // tab must end that rather than let it run on against a view that is gone.
  let disposed = false
  onScopeDispose(() => {
    disposed = true
  })

  /** Point citations only: a range citation exports as a link on its own words. */
  function pointTargets(lineIds: number[]): WordLinkTarget[] {
    const get = opts.getWordLinkAnchorsForLine
    if (!get) return []
    const targets: WordLinkTarget[] = []
    for (const lineId of lineIds) {
      for (const anchor of get(lineId)) {
        if (anchor.charEnd != null && anchor.charEnd > anchor.charStart) continue
        targets.push({
          bookId: anchor.targetBookId,
          lineIndex: anchor.targetLineIndex,
          lineId: anchor.targetLineId,
        })
      }
    }
    return targets
  }

  async function loadTargetContent(targets: WordLinkTarget[]): Promise<void> {
    const wanted = [...new Set(targets.map((t) => t.lineId))].filter((id) => id > 0)
    // Anything another copy is already fetching: wait on its promise rather than
    // issuing the same query again.
    const waits = [...new Set(wanted.filter((id) => inFlight.has(id)).map((id) => inFlight.get(id)!))]
    const missing = wanted.filter((id) => !queriedTargets.has(id))
    for (const id of missing) queriedTargets.add(id)

    const whole = (async () => {
      await Promise.all(waits)
      // Chunks run in sequence so a select-all cannot fire hundreds of concurrent
      // queries; the two queries within a chunk are independent and run together.
      for (const chunk of chunked(missing)) {
        if (disposed) return // the view is gone; stop walking the rest of the book
        try {
          const [contents, tocPaths] = await Promise.all([
            getLineContents(chunk),
            getTocPathsForLines(chunk),
          ])
          const tocByLine = new Map(tocPaths.map((row) => [row.lineId, row.tocPath]))
          for (const row of contents) {
            targetsByLineId.set(row.id, { html: row.content, tocPath: tocByLine.get(row.id) ?? '' })
          }
        } catch {
          // Transport failure, not a real miss — un-mark so a later copy can retry.
          for (const id of chunk) queriedTargets.delete(id)
        }
      }
    })()

    for (const id of missing) inFlight.set(id, whole)
    try {
      await whole
    } finally {
      for (const id of missing) if (inFlight.get(id) === whole) inFlight.delete(id)
    }
  }

  /**
   * Loads everything an export of these lines needs: their notes and citation
   * anchors first (the anchors decide which targets exist at all), then the target
   * lines those citations point at. Failures are swallowed by the underlying
   * loaders, so a warm-up that partly fails degrades the export instead of
   * blocking the copy.
   */
  async function prepareForLines(lineIds: number[]): Promise<void> {
    const ids = lineIds.filter((id) => id > 0)
    if (!ids.length) return
    await Promise.all([
      opts.loadNotes?.(ids) ?? Promise.resolve(),
      opts.loadWordLinkAnchors?.(ids) ?? Promise.resolve(),
    ])
    await loadTargetContent(pointTargets(ids))
  }

  /**
   * Target content for the citations ALREADY present in a rendered fragment — the
   * path a live selection takes.
   *
   * It deliberately loads nothing else. A citation whose anchors have not arrived
   * yet is not in this copy's markup anyway, so fetching it would not add an
   * endnote; it would only re-render the very lines the selection spans, collapsing
   * the selection before the clipboard event could read it. Select-all has no such
   * constraint — it builds from the model, not from the live range — which is why
   * that path uses prepareForLines instead.
   */
  async function prepareForRenderedHtml(html: string): Promise<void> {
    const targets: WordLinkTarget[] = []
    for (const match of html.matchAll(/data-wl="(\d+):(\d+):(\d+)"/g)) {
      targets.push({ bookId: +match[1]!, lineIndex: +match[2]!, lineId: +match[3]! })
    }
    await loadTargetContent(targets)
  }

  function resolveWordLinkTarget(target: WordLinkTarget): WordLinkTargetContent | undefined {
    const row = targetsByLineId.get(target.lineId)
    if (!row) return undefined
    return { html: row.html, bookTitle: opts.getBookTitle(target.bookId), tocPath: row.tocPath }
  }

  return { prepareForLines, prepareForRenderedHtml, resolveWordLinkTarget }
}
