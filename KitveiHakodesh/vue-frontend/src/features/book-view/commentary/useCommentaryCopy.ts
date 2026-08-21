import { watch } from 'vue'
import type { Ref } from 'vue'
import type { ContextMenuItem } from '@/components/ContextMenu.vue'
import { buildLineLink } from '../lines/useBookViewLineLink'
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
import { escapeHtml, htmlToText } from '@/utils/htmlText'
import { applyCopyExclusivity, type CopyExclusivityToggle } from '../copyFlagExclusivity'
import { useSettingsStore } from '@/stores/settingsStore'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { pasteIntoWord } from '@/webview-host/bridge'
import { triggerCopy } from '@/composables/useLineCopy'

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
  onHighlight: (lineId: number, startOffset: number, endOffset: number, colorArgb: number) => void,
  onClearHighlight: (lineId: number, startOffset: number, endOffset: number) => void,
  onAddNote: (lineId: number, startOffset: number, endOffset: number, quote: string) => void,
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
        result.push({ lineId, startOffset, endOffset: Math.min(endOffset, strippedText.length) })
      }
    }

    return result
  }

  // ── Highlight actions ───────────────────────────────────────────────────────

  function applyHighlightFromSelection(colorArgb: number): void {
    const lines = extractSelectionOnCommentaryLines()
    for (const line of lines) {
      onHighlight(line.lineId, line.startOffset, line.endOffset, colorArgb)
    }
    window.getSelection()?.removeAllRanges()
  }

  function clearHighlightFromSelection(): void {
    const lines = extractSelectionOnCommentaryLines()
    for (const line of lines) {
      onClearHighlight(line.lineId, line.startOffset, line.endOffset)
    }
    window.getSelection()?.removeAllRanges()
  }

  function addNoteFromSelection(): void {
    const lines = extractSelectionOnCommentaryLines()
    if (!lines.length) return
    const firstLine = lines[0]!
    const rawQuote = window.getSelection()?.toString() ?? ''
    const quote = rawQuote.replace(/[\u0591-\u05C7]/g, '').trim()
    window.getSelection()?.removeAllRanges()
    onAddNote(firstLine.lineId, firstLine.startOffset, firstLine.endOffset, quote)
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
      const fragment = range.cloneContents()
      const tmp = document.createElement('div')
      tmp.appendChild(fragment)
      joined = tmp.innerHTML
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
    if (settingsStore.copyWithNotes && exportData) {
      // A failed warm-up costs endnotes, not the copy itself.
      try {
        // Select-all builds from the model, so re-rendering during the await is
        // harmless there; a live selection must not be re-rendered out from under
        // itself, hence the narrower branch. See useCopyExportData.
        if (isSelectAll?.value) await exportData.prepareForLines(exportData.getAllLineIds())
        else await exportData.prepareForRenderedHtml(selectionHtml())
      } catch { /* export degrades; proceed */ }
    }
    triggerCopy(afterCopy)
  }

  // Select-all is the one case whose warm-up can be big, so it runs here — outside
  // any clipboard gesture — rather than inside the copy action. Covers both routes
  // into select-all (Ctrl+A and the menu item), since both set this flag.
  if (isSelectAll) {
    watch(isSelectAll, (on) => {
      if (!on || !settingsStore.copyWithNotes || !exportData) return
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
    paneNavigation.updateActiveTab({ route: '/search', title: `חיפוש: ${query}`, searchQuery: query })
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
    { label: 'בחר הכל', action: selectAllInContainer, shortcut: 'Ctrl+A' },
    { label: 'העתק', action: onCopy, shortcut: 'Ctrl+C' },
    { label: 'העתק לתוך וורד', action: onPasteIntoWord, shortcut: 'Ctrl+V' },
    { label: 'העתק לחיפוש במאגר', action: onSearchInRepository, shortcut: 'Ctrl+Shift+C' },
    { type: 'separator' },
    {
      type: 'submenu',
      label: 'אפשרויות העתקה',
      items: [
        // Independent checkboxes — all can be active simultaneously
        {
          type: 'checkbox',
          label: 'העתק כרצף (ללא מעברי שורה)',
          get checked() { return settingsStore.copyJoinLines },
          onChange: (value: boolean) => { settingsStore.copyJoinLines = value },
        },
        // start/end are a mutually-exclusive pair rendered as two checkboxes (ticking
        // one clears the other). All exclusivity is enforced by toggleCopyFlag →
        // copyFlagExclusivity.ts; do not add ad-hoc clears here.
        {
          type: 'checkbox',
          label: 'העתק עם מקור בהתחלה',
          get checked() { return settingsStore.copySourcePosition === 'start' },
          onChange: (value: boolean) => toggleCopyFlag('sourceStart', value),
        },
        {
          type: 'checkbox',
          label: 'העתק עם מקור בסוף',
          get checked() { return settingsStore.copySourcePosition === 'end' },
          onChange: (value: boolean) => toggleCopyFlag('sourceEnd', value),
        },
        {
          type: 'checkbox',
          label: 'העתק מקור עם ציטוט',
          get checked() { return settingsStore.copyAsSourceWithQuotation },
          onChange: (value: boolean) => toggleCopyFlag('sourceWithQuotation', value),
        },
        {
          type: 'checkbox',
          label: 'העתק עם הערות',
          get checked() { return settingsStore.copyWithNotes },
          onChange: (value: boolean) => toggleCopyFlag('withNotes', value),
        },
        {
          type: 'checkbox',
          label: 'העתק טקסט נקי',
          get checked() { return settingsStore.copyCleanText },
          onChange: (value: boolean) => { settingsStore.copyCleanText = value },
        },
      ],
    },
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
