import { t } from '@/i18n'
import { nextTick, watch } from 'vue'
import type { Ref } from 'vue'
import type { ContextMenuItem } from '@/components/ContextMenu.vue'
import { buildLineLink } from '@/utils/appDeepLink'
import {
  appLinkHtml,
  applyWordLinkExport,
  buildWordLinkEndnotesHtml,
  stripWordLinkMarkers,
  ENDNOTES_SEPARATOR,
  type WordLinkEndnote,
  type WordLinkTargetContent,
} from '../lines/wordLinkExport'
import type { WordLinkTarget } from '../lines/wordLinkAnchors'
import type { Note } from '../lines/useBookViewNotes'
import BookViewAnnotationMenuRow from '../lines/BookViewAnnotationMenuRow.vue'
import { cleanHebrewText } from '@/utils/hebrewTextCleaning'
import { escapeHtml, htmlToText, serializeRangeBalanced } from '@/utils/htmlText'
import { applyCopyExclusivity, type CopyExclusivityToggle } from '../copyFlagExclusivity'
import { useSettingsStore } from '@/stores/settingsStore'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { pasteIntoWord } from '@/webview-host/bridge'
import { triggerCopy } from '@/composables/useLineCopy'
import { awaitWithinActivationWindow } from '@/utils/clipboard'

/** The bits of a CommentaryGroup this composable needs: a title to cite, plus
 *  enough identity to look its TOC path up. */
type CommentaryGroupRef = {
  bookTitle: string
  bookId: number
  sectionLabel?: string
  subSectionLabel?: string
  /** Positional index of the group's first line — the target its מקור link points at. */
  lines?: ReadonlyArray<{ lineIndex: number }>
}

export function useCommentaryCopy(
  getActiveGroup: () => CommentaryGroupRef | null,
  /** Takes the group, not its bookId — one book can span several groups with
   *  different TOC paths (see commentaryGroupKey). */
  getTocPath: (group: CommentaryGroupRef) => string | undefined,
  selectAllInContainer: () => void,
  scrollerEl: Ref<HTMLElement | null>,
  // These persist to the settings database and only then mutate the reactive state
  // that re-renders the lines, so they return a promise the selection restore waits
  // on — see restoreSelectionAfterAnnotation.
  onHighlight: (
    lineId: number,
    startOffset: number,
    endOffset: number,
    colorArgb: number,
  ) => void | Promise<void>,
  onClearHighlight: (
    lineId: number,
    startOffset: number,
    endOffset: number,
  ) => void | Promise<void>,
  onAddNote: (
    lineId: number,
    startOffset: number,
    endOffset: number,
    quote: string,
  ) => void | Promise<void>,
  getNotesForLine?: (lineId: number) => Note[],
  // Select-all/copy-all support. isSelectAll reflects the container-wide "בחר הכל"
  // state; getAllContentHtml returns the ENTIRE (filtered) commentary document as
  // <div class="line">-wrapped, rendered lines (note markers included — Step 1 strips
  // them when copyWithNotes is off), built from the model so it is virtualization-
  // independent (the DOM only holds the lines currently scrolled into view).
  isSelectAll?: Ref<boolean>,
  getAllContentHtml?: () => string,
  // What copy-with-notes needs beyond the rendered markup: the notes and citations
  // of lines that were never scrolled into view, and the target lines the citations
  // point at. See useCopyExportData for why this splits into an async warm-up and a
  // synchronous resolve.
  exportData?: {
    /** Every line id in the (filtered) commentary document — the select-all scope. */
    getAllLineIds: () => number[]
    prepareForLines: (lineIds: number[]) => Promise<void>
    /** Resolves only the citations already rendered in a fragment — the selection path. */
    prepareForRenderedHtml: (html: string) => Promise<void>
    resolveWordLinkTarget: (target: WordLinkTarget) => WordLinkTargetContent | undefined
  },
  // Reaches the selected rows that scrolled out of the virtualized window mid-drag.
  // The two rows holding the selection's ends stay rendered (they are pinned by
  // useVirtualizedTextSelection), so their partial text still comes from the DOM
  // and only the rows BETWEEN them are rebuilt from the model.
  virtualizedSelection?: {
    /** Row index span of the current selection, or null when there is none here. */
    selectedRowIndexRange: () => { firstRowIndex: number; lastRowIndex: number } | null
    /** The rendered markup of the flat items in an inclusive row-index span. */
    buildContentHtmlForRange: (firstIndex: number, lastIndex: number) => string
    /** Re-creates the selection after an annotation re-rendered the lines it hit. */
    restoreSelectionFromPinnedRows: () => void
    /**
     * The annotatable line at a flat row index — its id and how many annotation
     * offsets it spans — or null for a header row or a row index out of range. Lets
     * an annotation reach the selected lines the virtualizer never mounted.
     */
    lineAtRowIndex: (rowIndex: number) => { lineId: number; offsetLength: number } | null
    /**
     * Fetches the text of the flat rows in an inclusive span: the model holds text
     * only for the lines the scroller has shown, and the rows between a selection's
     * ends may never have been.
     */
    ensureContentsForRange: (firstIndex: number, lastIndex: number) => Promise<void>
    /** Fetches the text of every line in the document — the select-all copy. */
    ensureAllContents: () => Promise<void>
  },
  /** Opens the "report a mistake" dialog for the pressed commentary line. */
  onReportError?: () => void,
) {
  const resolveWordLinkTarget = exportData?.resolveWordLinkTarget
  const settingsStore = useSettingsStore()
  const paneNavigation = usePaneNavigation()

  /**
   * Applies one copy-flag toggle through the shared exclusivity model and writes
   * the enforced result back to the store. Kept identical to the lines menu so the
   * two copy menus can't drift; the rule rationale lives in copyFlagExclusivity.ts
   * (notably: notes ⊗ END-source is intentional, notes + start-source is allowed).
   */
  function toggleCopyFlag(toggle: CopyExclusivityToggle, value: boolean): void {
    const next = applyCopyExclusivity(
      {
        copySourcePosition: settingsStore.copySourcePosition,
        copyWithNotes: settingsStore.copyWithNotes,
        copyAsSourceWithQuotation: settingsStore.copyAsSourceWithQuotation,
      },
      toggle,
      value,
    )
    settingsStore.copySourcePosition = next.copySourcePosition
    settingsStore.copyWithNotes = next.copyWithNotes
    settingsStore.copyAsSourceWithQuotation = next.copyAsSourceWithQuotation
  }

  // ── Source builder ──────────────────────────────────────────────────────────

  function buildCommentarySource(bookTitle: string, tocPath?: string): string {
    const cleanTitle = bookTitle.replace(/\s+מפרשים\s*$/, '').replace(/\s+רשנם\s*$/, '')
    // TOC paths are stored with " / " between segments (search-UI display format).
    // A מקור reference should read as one continuous title, so collapse the segment
    // separators to a single space. Not a document format used in the sources.
    const flatPath = tocPath?.replace(/\s*\/\s*/g, ' ')
    return flatPath ? `${cleanTitle}, ${flatPath}` : cleanTitle
  }

  /**
   * The same reference as HTML, linked back into the app — the reader of a pasted
   * quote can reach its place, and it still reads as plain text where a link means
   * nothing. The target is the group's first line, the same place the app itself
   * opens when you pick that commentary, which is exactly what the reference names.
   * Falls back to escaped text when the group carries no line to point at.
   */
  function buildCommentarySourceHtml(group: CommentaryGroupRef, tocPath?: string): string {
    const source = buildCommentarySource(group.bookTitle, tocPath)
    const lineIndex = group.lines?.[0]?.lineIndex
    // lineIndex < 0 is real: a group with no text for the current line carries a -1
    // placeholder line, which would otherwise link to "?index=-1".
    if (group.bookId <= 0 || lineIndex == null || lineIndex < 0) return escapeHtml(source)
    return appLinkHtml(source, buildLineLink(group.bookId, lineIndex))
  }

  // ── Selection extraction (for highlight/note offset tracking) ───────────────

  interface SelectionOnCommentaryLine {
    lineId: number
    startOffset: number
    endOffset: number
    /**
     * The line's flat row index — its position in the document, used to keep the
     * collected lines in reading order once the unmounted ones are added to the
     * rendered ones. Not part of what an annotation stores.
     */
    rowIndex: number
  }

  function extractSelectionOnCommentaryLines(): SelectionOnCommentaryLine[] {
    const scroller = scrollerEl.value
    if (!scroller) return []
    const sel = window.getSelection()
    if (!sel || sel.rangeCount === 0 || sel.isCollapsed) return []
    const range = sel.getRangeAt(0)

    const lineEls = Array.from(scroller.querySelectorAll('.line'))
    const intersected = lineEls.filter((el) => range.intersectsNode(el))
    if (!intersected.length) return []

    const result: SelectionOnCommentaryLine[] = []

    for (let i = 0; i < intersected.length; i++) {
      const lineEl = intersected[i] as HTMLElement
      const vItemEl = lineEl.closest('[data-index]') as HTMLElement | null
      if (!vItemEl) continue

      const lineIdStr = lineEl.dataset['lineId']
      if (!lineIdStr) continue
      const lineId = parseInt(lineIdStr, 10)
      if (isNaN(lineId) || lineId === -1) continue

      const strippedText = (lineEl.textContent ?? '').replace(/[\u0591-\u05C7]/g, '')

      function countStrippedOffset(node: Node, offsetInNode: number): number {
        const walker = document.createTreeWalker(lineEl, NodeFilter.SHOW_TEXT)
        let stripped = 0
        let current: Text | null
        while ((current = walker.nextNode() as Text | null)) {
          if (current === node) {
            const slice = current.textContent?.slice(0, offsetInNode) ?? ''
            stripped += slice.replace(/[\u0591-\u05C7]/g, '').length
            return stripped
          }
          stripped += (current.textContent ?? '').replace(/[\u0591-\u05C7]/g, '').length
        }
        return stripped
      }

      const isFirstLine = i === 0
      const isLastLine = i === intersected.length - 1

      let startOffset = 0
      let endOffset = strippedText.length

      if (isFirstLine) startOffset = countStrippedOffset(range.startContainer, range.startOffset)
      if (isLastLine) endOffset = countStrippedOffset(range.endContainer, range.endOffset)

      if (startOffset < endOffset) {
        result.push({
          lineId,
          startOffset,
          endOffset: Math.min(endOffset, strippedText.length),
          rowIndex: parseInt(vItemEl.dataset['index'] ?? '', 10),
        })
      }
    }

    appendUnmountedSelectedLines(result)

    return result
  }

  /**
   * Adds the selected lines that are not in the DOM — the ones that scrolled out of
   * the virtualized window while the selection was being dragged over them.
   *
   * The loop above can only see rendered lines, so without this an annotation applied
   * to a selection spanning more than a screen would silently land on the handful of
   * rendered rows and skip every line between them. Those middle lines are selected in
   * full by definition (only the two ends can be partial, and both are always rendered
   * because they are pinned), so each takes a whole-line offset span measured from the
   * model. Header rows carry no annotatable line and are skipped.
   */
  function appendUnmountedSelectedLines(selectionLines: SelectionOnCommentaryLine[]) {
    const rowIndexRange = virtualizedSelection?.selectedRowIndexRange()
    if (!rowIndexRange || !virtualizedSelection) return
    const { firstRowIndex, lastRowIndex } = rowIndexRange

    const alreadyCollectedLineIds = new Set(selectionLines.map((line) => line.lineId))

    for (let rowIndex = firstRowIndex + 1; rowIndex < lastRowIndex; rowIndex++) {
      const line = virtualizedSelection.lineAtRowIndex(rowIndex)
      if (!line || line.offsetLength === 0) continue
      if (alreadyCollectedLineIds.has(line.lineId)) continue
      selectionLines.push({
        lineId: line.lineId,
        startOffset: 0,
        endOffset: line.offsetLength,
        rowIndex,
      })
    }

    // Sorted, not spliced into place: the rendered rows the DOM loop collected are a
    // contiguous block around the viewport, so the unmounted rows belong AFTER some of
    // them and BEFORE others. Only a real sort puts every line in reading order — and
    // addNoteFromSelection depends on it, anchoring the note to lines[0].
    selectionLines.sort((first, second) => first.rowIndex - second.rowIndex)
  }

  // ── Highlight actions ───────────────────────────────────────────────────────

  function applyHighlightFromSelection(colorArgb: number): void {
    const lines = extractSelectionOnCommentaryLines()
    restoreSelectionAfterAnnotation(
      lines.map((line) => onHighlight(line.lineId, line.startOffset, line.endOffset, colorArgb)),
    )
  }

  function clearHighlightFromSelection(): void {
    const lines = extractSelectionOnCommentaryLines()
    restoreSelectionAfterAnnotation(
      lines.map((line) => onClearHighlight(line.lineId, line.startOffset, line.endOffset)),
    )
  }

  function addNoteFromSelection(): void {
    const lines = extractSelectionOnCommentaryLines()
    if (!lines.length) return
    const firstLine = lines[0]!
    const rawQuote = window.getSelection()?.toString() ?? ''
    const quote = rawQuote.replace(/[\u0591-\u05C7]/g, '').trim()
    restoreSelectionAfterAnnotation([
      onAddNote(firstLine.lineId, firstLine.startOffset, firstLine.endOffset, quote),
    ])
  }

  /**
   * Puts the selection back after an annotation replaced the markup of the lines it
   * landed on \u2014 which destroys the text nodes the selection's boundaries pointed at.
   *
   * Waits on the annotation writes first: they reach the settings database before
   * they mutate the reactive state that re-renders the lines, so restoring on a bare
   * nextTick would run against the pre-render DOM and be thrown away by the render
   * that follows. A no-op when the pane has no pinned-row tracker wired up.
   */
  function restoreSelectionAfterAnnotation(writes: Array<void | Promise<void>>): void {
    const restore = virtualizedSelection?.restoreSelectionFromPinnedRows
    if (!restore) return
    void Promise.all(writes).then(() => nextTick(restore))
  }

  // ── Note marker helpers ─────────────────────────────────────────────────────

  function stripNoteMarkers(html: string): string {
    return html.replace(/<sup[^>]*class="user-note-marker"[^>]*>.*?<\/sup>/gs, '')
  }

  interface EndnoteEntry { number: number; noteText: string }

  function extractEndnotes(html: string): { html: string; endnotes: EndnoteEntry[] } {
    const endnotes: EndnoteEntry[] = []
    let counter = 0
    const replaced = html.replace(
      /<sup[^>]*class="user-note-marker"[^>]*data-note-id="(\d+)"[^>]*>.*?<\/sup>/gs,
      (match: string, noteIdStr: string) => {
        const noteId = parseInt(noteIdStr, 10)
        // The note text lives in the marker's own data-note-text attribute (set by
        // the renderer). Read it straight from the matched HTML — this is
        // virtualization-safe, so select-all copy resolves notes on off-screen
        // lines too. Fall back to a DOM lookup only if the attribute is missing.
        let noteText = decodeNoteTextAttr(match)
        if (noteText == null) {
          if (!getNotesForLine) return ''
          const scroller = scrollerEl.value
          if (!scroller) return ''
          const markerEl = scroller.querySelector(`[data-note-id="${noteId}"]`) as HTMLElement | null
          const lineEl = markerEl?.closest('[data-line-id]') as HTMLElement | null
          const lineId = lineEl ? parseInt(lineEl.dataset['lineId'] ?? '', 10) : NaN
          if (isNaN(lineId)) return ''
          const foundNote = getNotesForLine(lineId).find((n) => n.id === noteId)
          if (!foundNote) return ''
          noteText = foundNote.note
        }
        counter++
        endnotes.push({ number: counter, noteText })
        return `<sup><a href="#note-${counter}" id="ref-${counter}" style="color:var(--accent-color,#0078d4);text-decoration:none">${counter}</a></sup>`
      },
    )
    return { html: replaced, endnotes }
  }

  /** Extracts and decodes the data-note-text="…" of a note marker <sup>, or null. */
  function decodeNoteTextAttr(supHtml: string): string | null {
    const m = supHtml.match(/\bdata-note-text="([^"]*)"/)
    if (!m) return null
    // The renderer HTML-escaped the note text into the attribute; decode it back.
    return htmlToText(m[1]!)
  }

  /**
   * Both endnote sequences under one rule: the user's notes first, then the DB's own
   * citations. The two are numbered independently on purpose — each keeps the mark it
   * carries in the line itself, which is what a reader matches them up by.
   */
  function buildEndnotesHtml(endnotes: EndnoteEntry[], wordLinks: WordLinkEndnote[] = []): string {
    const items = endnotes
      .map((e) => `<div dir="rtl" id="note-${e.number}"><a href="#ref-${e.number}" style="color:var(--accent-color,#0078d4);text-decoration:none">${e.number}.</a> ${e.noteText}</div>`)
      .join('\n')
    const blocks = [items, buildWordLinkEndnotesHtml(wordLinks)].filter((block) => block !== '')
    if (!blocks.length) return ''
    return `\n${ENDNOTES_SEPARATOR}\n${blocks.join('\n')}`
  }

  // ── Copy actions ────────────────────────────────────────────────────────────

  /**
   * The selected rows' HTML including the ones not currently in the DOM, or null when
   * this selection needs no stitching (it fits in the rendered window, or the pinned
   * boundary rows are unavailable) and the caller should use its DOM-only path.
   *
   * The rows between the two ends are rebuilt whole from the model, headers included.
   * The two END rows follow whatever the non-stitched path would have done with them,
   * so the same selection copies identically however far it happens to be scrolled:
   * with copyJoinLines OFF they keep only their selected part, and with it ON they are
   * taken whole, matching that path's re-collect of every intersected line.
   */
  function extractSelectionWithUnmountedLines(range: Range): string | null {
    const scroller = scrollerEl.value
    const rowIndexRange = virtualizedSelection?.selectedRowIndexRange()
    if (!scroller || !rowIndexRange) return null
    const { firstRowIndex, lastRowIndex } = rowIndexRange
    // A selection inside a single row is entirely rendered: nothing to stitch.
    if (firstRowIndex >= lastRowIndex) return null

    const firstRowElement = scroller.querySelector(`[data-index="${firstRowIndex}"]`)
    const lastRowElement = scroller.querySelector(`[data-index="${lastRowIndex}"]`)
    if (!firstRowElement || !lastRowElement) return null

    /** One end row: its selected part, or the whole line when joining. */
    function endRowHtml(rowElement: Element, clip: (clipped: Range) => void): string {
      if (settingsStore.copyJoinLines) {
        const lineElement = rowElement.querySelector('.line') as HTMLElement | null
        return lineElement ? lineElement.innerHTML : ''
      }
      const clipped = range.cloneRange()
      clip(clipped)
      return serializeRangeBalanced(clipped, scroller)
    }

    const middleHtml =
      lastRowIndex - firstRowIndex > 1
        ? virtualizedSelection!.buildContentHtmlForRange(firstRowIndex + 1, lastRowIndex - 1)
        : ''

    const joined = [
      endRowHtml(firstRowElement, (clipped) => clipped.setEndAfter(firstRowElement)),
      middleHtml,
      endRowHtml(lastRowElement, (clipped) => clipped.setStartBefore(lastRowElement)),
    ]
      .filter((part) => part.trim())
      .join('')
    return joined.trim() ? joined : null
  }

  /**
   * Builds the final HTML for the current selection applying all active copy flags.
   * Returns null when there is no selection.
   * See the copy flag semantics block at the top of the file for a full description.
   */
  function buildFormattedHtml(): string | null {
    // ── Acquire raw HTML ──────────────────────────────────────────────────────
    // TWO INDEPENDENT decisions — do not conflate them (mirrors the lines menu):
    //   WHICH content  → isSelectAll (select-all/copy-all): the whole (filtered)
    //                    commentary document, built from the model so it is NOT
    //                    truncated to the virtualized-in lines. Honoured regardless
    //                    of copyJoinLines.
    //   HOW to format  → copyJoinLines (Step 2 below): flatten to one run vs. keep
    //                    per-line breaks. Purely formatting; does not pick content.
    let joined: string

    if (isSelectAll?.value && getAllContentHtml) {
      // Copy-all: gather every visible line from the model (virtualization-independent).
      joined = getAllContentHtml()
      if (!joined.trim()) return null
    } else {
      const sel = window.getSelection()
      if (!sel || sel.rangeCount === 0 || sel.isCollapsed) return null
      const range = sel.getRangeAt(0)

      // A selection long enough to have scrolled its middle rows out of the DOM is
      // stitched from the model; a selection living entirely inside the rendered
      // window falls through to reading the DOM alone.
      const stitched = extractSelectionWithUnmountedLines(range)
      if (stitched !== null) {
        joined = stitched
      } else {
        // Balanced serialization, not tmp.innerHTML: a selection starting mid-heading
        // clones without its opening tag and innerHTML repairs it by running the heading
        // over every following line (see serializeRangeBalanced).
        joined = serializeRangeBalanced(range, scrollerEl.value)
        if (!joined.trim()) return null

        // copyJoinLines ON: re-collect the full innerHTML of each intersected .line so the
        // flatten step (Step 2) works on complete line content, not just the DOM range.
        if (settingsStore.copyJoinLines && scrollerEl.value) {
          const intersectedLines = Array.from(scrollerEl.value.querySelectorAll('.line'))
            .filter((el) => range.intersectsNode(el))
          if (intersectedLines.length) {
            joined = intersectedLines.map((el) => (el as HTMLElement).innerHTML).join(' ')
          }
        }
      }
    }

    // ── Step 1: note markers ─────────────────────────────────────────────────
    let html: string
    let endnotesHtml = ''
    if (settingsStore.copyWithNotes) {
      const { html: extracted, endnotes } = extractEndnotes(joined)
      // The DB's own citations travel with the user's notes: a range citation becomes
      // a real link on the cited words, a point citation becomes an endnote in its own
      // sequence. Anything whose target data never loaded is dropped rather than
      // exported half-formed (see applyWordLinkExport).
      const linked = resolveWordLinkTarget
        ? applyWordLinkExport(extracted, resolveWordLinkTarget)
        : { html: extracted, endnotes: [] as WordLinkEndnote[] }
      html = linked.html
      endnotesHtml = buildEndnotesHtml(endnotes, linked.endnotes)
    } else {
      html = stripWordLinkMarkers(stripNoteMarkers(joined))
    }

    // ── Step 2: copyJoinLines — flatten to one continuous run ─────────────────
    // Only when join-lines mode is on. `joined` above already concatenated the
    // intersected lines' innerHTML with a space, so `html` is the combined inline
    // content. Strip any block-level tags that would force a break between lines
    // (<div>/<p>/<br>) and wrap the whole thing in a SINGLE <span>, so the lines
    // paste as one uninterrupted run with no line break between them. The OFF
    // path keeps the raw per-line blocks (one break per line).
    //
    // <span>, NOT <div>: a <div> IS a paragraph to Word's HTML importer, so it
    // contributes its own trailing paragraph mark — which is exactly the break
    // join-lines mode exists to avoid. A <span> is inline content and merges into
    // the paragraph at the paste caret instead of opening a new one.
    if (settingsStore.copyJoinLines) {
      const inline = html
        .replace(/<\/?(?:div|p)[^>]*>/gi, ' ')
        .replace(/<br\s*\/?>/gi, ' ')
        .replace(/\s{2,}/g, ' ')
        .trim()
      html = `<span>${inline}</span>`
    }

    // ── Step 3: copyCleanText ────────────────────────────────────────────────
    // Clean BOTH the body and the endnotes so a single copy has one consistent
    // cleaning state. The source decoration (Step 4) is added AFTER cleaning on
    // purpose — a מקור reference is not book text and must not be stripped.
    if (settingsStore.copyCleanText) {
      html = cleanHebrewText(html)
      if (endnotesHtml) endnotesHtml = cleanHebrewText(endnotesHtml)
    }

    // ── Step 4: copySourcePosition (radio) ───────────────────────────────────
    // Skipped when quotation is on: quotation OWNS the whole output (Step 5) and
    // uses the same position to lay out its own parenthesised מקור, so decorating
    // html here would be discarded (and would double-count in Step 5's re-collect).
    const position = settingsStore.copySourcePosition
    if (!settingsStore.copyAsSourceWithQuotation && (position === 'start' || position === 'end')) {
      const activeGroup = getActiveGroup()
      if (activeGroup) {
        const source = buildCommentarySourceHtml(activeGroup, getTocPath(activeGroup))
        if (position === 'start') {
          html = `<h2 dir="rtl">${source}</h2>${html}`
        } else {
          html = `${html} (${source})`
        }
      }
    }

    // ── Step 5: copyAsSourceWithQuotation ─────────────────────────────────────
    // Produces a single inline line. The source position decides the layout:
    //   start → (מקור) "ציטוט"      end → "ציטוט" (מקור)
    // (quotation always has a position — enforced by copyFlagExclusivity). Always
    // collects the full line content into one inline paragraph regardless of the
    // copyJoinLines setting.
    if (settingsStore.copyAsSourceWithQuotation) {
      const activeGroup = getActiveGroup()
      // Already escaped (and linked) — unlike the quote, which is escaped below.
      const source = activeGroup ? buildCommentarySourceHtml(activeGroup, getTocPath(activeGroup)) : ''
      // Collect full line content into one inline run. On the select-all path (or
      // when copyJoinLines already ran), `html` already holds the complete content
      // from the model — use it directly rather than re-collecting from the DOM,
      // which would re-truncate a copy-all to the virtualized-in lines.
      let inlineText: string
      if (settingsStore.copyJoinLines || (isSelectAll?.value && getAllContentHtml)) {
        inlineText = stripNoteMarkers(html).replace(/<[^>]*>/g, ' ').replace(/\s+/g, ' ').trim()
      } else {
        // Normal selection: re-collect from the intersected DOM lines for complete
        // line content (the range may cut a line short, but the whole line is wanted).
        const scroller = scrollerEl.value
        const sel = window.getSelection()
        if (scroller && sel && sel.rangeCount > 0) {
          const range = sel.getRangeAt(0)
          const blobLines = Array.from(scroller.querySelectorAll('.line'))
            .filter((el) => range.intersectsNode(el))
            .map((el) => (el as HTMLElement).innerHTML)
          inlineText = stripNoteMarkers(blobLines.join(' ')).replace(/<[^>]*>/g, ' ').replace(/\s+/g, ' ').trim()
        } else {
          inlineText = stripNoteMarkers(html).replace(/<[^>]*>/g, ' ').replace(/\s+/g, ' ').trim()
        }
      }
      if (settingsStore.copyCleanText) inlineText = cleanHebrewText(inlineText)
      // Decode any surviving HTML entities to real text, then escape once — the
      // clipboard writer treats the return value as HTML, so bare </>/& must be
      // escaped (and not double-encoded). The source arrives already escaped from
      // buildCommentarySourceHtml, which also links it.
      const src = source
      const quote = escapeHtml(htmlToText(inlineText))
      const body = position === 'end' ? `"${quote}" (${src})` : `(${src}) "${quote}"`
      // <span>, not <div>: quotation mode is a single inline run by design, so it
      // must not contribute a paragraph mark of its own on paste into Word.
      return `<span dir="rtl">${body}</span>`
    }

    return html + endnotesHtml
  }

  /** The current selection's rendered markup, for resolving the citations inside it. */
  function selectionHtml(): string {
    const sel = window.getSelection()
    if (!sel || sel.rangeCount === 0) return ''
    const holder = document.createElement('div')
    holder.appendChild(sel.getRangeAt(0).cloneContents())
    return holder.innerHTML
  }

  /**
   * Loads what the export needs, then fires the native copy event — useScopedCopy
   * intercepts it and applies all active flags (copyJoinLines, copySourcePosition,
   * copyWithNotes, copyCleanText).
   *
   * The await is what makes copy-with-notes independent of what happens to be
   * scrolled into view. It stays inside the browser's user-activation window because
   * each branch is bounded: a live selection only resolves the citations already in
   * its markup, and the whole-document case was already warmed when the select-all
   * happened (watch below). Were the await to outlast that window,
   * execCommand('copy') would quietly do nothing.
   */
  async function prepareAndCopy(afterCopy?: () => void): Promise<void> {
    // The text itself first: a select-all or a selection that spans unmounted rows
    // is built from the model, and the model has text only for lines the scroller
    // fetched. Same activation budget as the notes warm-up below.
    if (virtualizedSelection) {
      try {
        if (isSelectAll?.value) {
          await awaitWithinActivationWindow(virtualizedSelection.ensureAllContents())
        } else {
          const rowIndexRange = virtualizedSelection.selectedRowIndexRange()
          if (rowIndexRange && rowIndexRange.lastRowIndex - rowIndexRange.firstRowIndex > 1) {
            await awaitWithinActivationWindow(
              virtualizedSelection.ensureContentsForRange(rowIndexRange.firstRowIndex + 1, rowIndexRange.lastRowIndex - 1),
            )
          }
        }
      } catch { /* the copy goes ahead with the text that arrived */ }
    }
    if (settingsStore.copyWithNotes && exportData) {
      // A failed warm-up costs endnotes, not the copy itself.
      try {
        // Select-all builds from the model, so re-rendering during the await is
        // harmless there; a live selection must not be re-rendered out from under
        // itself, hence the narrower branch. See useCopyExportData.
        // Bounded by the clipboard's activation window, not just by "each branch is
        // small": a cold service or a hosted bridge can make either branch slow enough
        // that execCommand('copy') is a no-op by the time it runs, and the Ctrl+C keydown
        // was already preventDefault()ed - so a slow warm-up meant copying NOTHING. Past
        // the budget the copy goes ahead with whatever has arrived; the load keeps running
        // and the next copy has it all.
        const prepared = isSelectAll?.value
          ? exportData.prepareForLines(exportData.getAllLineIds())
          : exportData.prepareForRenderedHtml(selectionHtml())
        await awaitWithinActivationWindow(prepared)
      } catch { /* export degrades; proceed */ }
    }
    triggerCopy(afterCopy)
  }

  // Select-all is the one case whose warm-up can be big, so it runs here — outside
  // any clipboard gesture — rather than inside the copy action. Covers both routes
  // into select-all (Ctrl+A and the menu item), since both set this flag.
  if (isSelectAll) {
    watch(isSelectAll, (on) => {
      if (!on) return
      // The document's text, which the model holds only for the lines shown so far.
      void virtualizedSelection?.ensureAllContents().catch(() => {})
      if (!settingsStore.copyWithNotes || !exportData) return
      void exportData.prepareForLines(exportData.getAllLineIds())
    })
  }

  function onCopy(): void {
    void prepareAndCopy()
  }

  // Uses the same copy path as onCopy — triggerCopy fires the copy event so useScopedCopy
  // writes the formatted HTML to the clipboard — then calls pasteIntoWord() from inside
  // the copy event handler, after the clipboard write is guaranteed complete.
  function onPasteIntoWord(): void {
    void prepareAndCopy(() => pasteIntoWord().catch(() => {}))
  }

  function onSearchInRepository(): void {
    const sel = window.getSelection()
    if (!sel || sel.rangeCount === 0) return
    const range = sel.getRangeAt(0)
    const fragment = range.cloneContents()
    const tmp = document.createElement('div')
    tmp.appendChild(fragment)
    const rawText = tmp.innerHTML.replace(/<[^>]*>/g, ' ')
    const query = rawText.replace(/[^א-ת\s]/g, '').replace(/\s+/g, ' ').trim()
    if (!query) return
    paneNavigation.updateActiveTab({ route: '/search', title: t('bookView.copy.searchTabTitle', { query }), searchQuery: query })
  }

  // ── Context menu ────────────────────────────────────────────────────────────

  const annotationRow: ContextMenuItem = {
    type: 'component',
    component: BookViewAnnotationMenuRow,
    props: {
      onHighlight: applyHighlightFromSelection,
      onClearHighlight: clearHighlightFromSelection,
      onAddNote: addNoteFromSelection,
    },
  }

  const contextMenuItems: ContextMenuItem[] = [
    { label: t('bookView.selectAll'), action: selectAllInContainer, shortcut: 'Ctrl+A' },
    { label: t('bookView.copy.copy'), action: onCopy, shortcut: 'Ctrl+C' },
    { label: t('bookView.copy.intoWord'), action: onPasteIntoWord, shortcut: 'Ctrl+V' },
    { label: t('bookView.copy.toSearch'), action: onSearchInRepository, shortcut: 'Ctrl+Shift+C' },
    { type: 'separator' },
    {
      type: 'submenu',
      label: t('bookView.copy.options'),
      items: [
        // Independent checkboxes — all can be active simultaneously
        {
          type: 'checkbox',
          label: t('bookView.copy.joinLines'),
          get checked() { return settingsStore.copyJoinLines },
          onChange: (value: boolean) => { settingsStore.copyJoinLines = value },
        },
        // start/end are a mutually-exclusive pair rendered as two checkboxes (ticking
        // one clears the other). All exclusivity is enforced by toggleCopyFlag →
        // copyFlagExclusivity.ts; do not add ad-hoc clears here.
        {
          type: 'checkbox',
          label: t('bookView.copy.sourceStart'),
          get checked() { return settingsStore.copySourcePosition === 'start' },
          onChange: (value: boolean) => toggleCopyFlag('sourceStart', value),
        },
        {
          type: 'checkbox',
          label: t('bookView.copy.sourceEnd'),
          get checked() { return settingsStore.copySourcePosition === 'end' },
          onChange: (value: boolean) => toggleCopyFlag('sourceEnd', value),
        },
        {
          type: 'checkbox',
          label: t('bookView.copy.quote'),
          get checked() { return settingsStore.copyAsSourceWithQuotation },
          onChange: (value: boolean) => toggleCopyFlag('sourceWithQuotation', value),
        },
        {
          type: 'checkbox',
          label: t('bookView.copy.withNotes'),
          get checked() { return settingsStore.copyWithNotes },
          onChange: (value: boolean) => toggleCopyFlag('withNotes', value),
        },
        {
          type: 'checkbox',
          label: t('bookView.copy.plain'),
          get checked() { return settingsStore.copyCleanText },
          onChange: (value: boolean) => { settingsStore.copyCleanText = value },
        },
      ],
    },
    { type: 'separator' },
    { label: t('bookView.report.title'), action: onReportError ?? (() => {}) },
    { type: 'separator' },
    annotationRow,
  ]

  return {
    contextMenuItems,
    buildFormattedHtml,
    onCopy,
    onPasteIntoWord,
    onSearchInRepository,
  }
}
