import type { Ref } from 'vue'
import type { ContextMenuItem } from '@/components/ContextMenu.vue'
import type { Note } from '../lines/useBookViewNotes'
import BookViewAnnotationMenuRow from '../lines/BookViewAnnotationMenuRow.vue'
import { cleanHebrewText } from '@/utils/hebrewTextCleaning'
import { useSettingsStore } from '@/stores/settingsStore'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { pasteIntoWord } from '@/webview-host/bridge'
import { triggerCopy } from '@/composables/useLineCopy'

export function useCommentaryCopy(
  getActiveGroup: () => { bookTitle: string; bookId: number } | null,
  getTocPath: (bookId: number) => string | undefined,
  selectAllInContainer: () => void,
  scrollerEl: Ref<HTMLElement | null>,
  onHighlight: (lineId: number, startOffset: number, endOffset: number, colorArgb: number) => void,
  onClearHighlight: (lineId: number, startOffset: number, endOffset: number) => void,
  onAddNote: (lineId: number, startOffset: number, endOffset: number, quote: string) => void,
  getNotesForLine?: (lineId: number) => Note[],
) {
  const settingsStore = useSettingsStore()
  const paneNavigation = usePaneNavigation()

  // ── Source builder ──────────────────────────────────────────────────────────

  function buildCommentarySource(bookTitle: string, tocPath?: string): string {
    const cleanTitle = bookTitle.replace(/\s+מפרשים\s*$/, '').replace(/\s+רשנם\s*$/, '')
    // TOC paths are stored with " / " between segments (search-UI display format).
    // A מקור reference should read as one continuous title, so collapse the segment
    // separators to a single space. Not a document format used in the sources.
    const flatPath = tocPath?.replace(/\s*\/\s*/g, ' ')
    return flatPath ? `${cleanTitle}, ${flatPath}` : cleanTitle
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
      (_match: string, noteIdStr: string) => {
        const noteId = parseInt(noteIdStr, 10)
        if (!getNotesForLine) return ''
        const scroller = scrollerEl.value
        if (!scroller) return ''
        const markerEl = scroller.querySelector(`[data-note-id="${noteId}"]`) as HTMLElement | null
        const lineEl = markerEl?.closest('[data-line-id]') as HTMLElement | null
        const lineId = lineEl ? parseInt(lineEl.dataset['lineId'] ?? '', 10) : NaN
        if (isNaN(lineId)) return ''
        const foundNote = getNotesForLine(lineId).find((n) => n.id === noteId)
        if (!foundNote) return ''
        counter++
        endnotes.push({ number: counter, noteText: foundNote.note })
        return `<sup><a href="#note-${counter}" id="ref-${counter}" style="color:var(--accent-color,#0078d4);text-decoration:none">${counter}</a></sup>`
      },
    )
    return { html: replaced, endnotes }
  }

  function buildEndnotesHtml(endnotes: EndnoteEntry[]): string {
    if (!endnotes.length) return ''
    const separator = '<hr dir="rtl" style="border:none;border-top:1px solid #ccc;margin:8pt 0"/>'
    const items = endnotes
      .map((e) => `<div dir="rtl" id="note-${e.number}"><a href="#ref-${e.number}" style="color:var(--accent-color,#0078d4);text-decoration:none">${e.number}.</a> ${e.noteText}</div>`)
      .join('\n')
    return `\n${separator}\n${items}`
  }

  // ── Copy actions ────────────────────────────────────────────────────────────

  /**
   * Builds the final HTML for the current selection applying all active copy flags.
   * Returns null when there is no selection.
   * See the copy flag semantics block at the top of the file for a full description.
   */
  function buildFormattedHtml(): string | null {
    // ── Acquire raw HTML ──────────────────────────────────────────────────────
    // copyJoinLines ON:  collect each intersected .line's innerHTML (joined below)
    // copyJoinLines OFF: use raw browser selection HTML directly (per-line blocks kept)
    const sel = window.getSelection()
    if (!sel || sel.rangeCount === 0 || sel.isCollapsed) return null
    const range = sel.getRangeAt(0)
    const fragment = range.cloneContents()
    const tmp = document.createElement('div')
    tmp.appendChild(fragment)
    let joined = tmp.innerHTML
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

    // ── Step 1: note markers ─────────────────────────────────────────────────
    let html: string
    let endnotesHtml = ''
    if (settingsStore.copyWithNotes) {
      const { html: extracted, endnotes } = extractEndnotes(joined)
      html = extracted
      endnotesHtml = buildEndnotesHtml(endnotes)
    } else {
      html = stripNoteMarkers(joined)
    }

    // ── Step 2: copyJoinLines — flatten to one continuous run ─────────────────
    // Only when join-lines mode is on. `joined` above already concatenated the
    // intersected lines' innerHTML with a space, so `html` is the combined inline
    // content. Strip any block-level tags that would force a break between lines
    // (<div>/<p>/<br>) and wrap the whole thing in a SINGLE <div>, so the lines
    // paste as one uninterrupted block with no line break between them. The OFF
    // path keeps the raw per-line blocks (one break per line).
    if (settingsStore.copyJoinLines) {
      const inline = html
        .replace(/<\/?(?:div|p)[^>]*>/gi, ' ')
        .replace(/<br\s*\/?>/gi, ' ')
        .replace(/\s{2,}/g, ' ')
        .trim()
      html = `<div>${inline}</div>`
    }

    // ── Step 3: copyCleanText ────────────────────────────────────────────────
    if (settingsStore.copyCleanText) {
      html = cleanHebrewText(html)
    }

    // ── Step 4: copySourcePosition (radio) ───────────────────────────────────
    const position = settingsStore.copySourcePosition
    if (position === 'start' || position === 'end') {
      const activeGroup = getActiveGroup()
      if (activeGroup) {
        const tocPath = getTocPath(activeGroup.bookId)
        const source = buildCommentarySource(activeGroup.bookTitle, tocPath)
        if (position === 'start') {
          html = `<h2 dir="rtl">${source}</h2>${html}`
        } else {
          html = `${html} (${source})`
        }
      }
    }

    // ── Step 5: copyAsSourceWithQuotation ─────────────────────────────────────
    // Produces a single line: (מקור) "ציטוט"
    // Always collects the full line content and joins it into one inline paragraph
    // regardless of the copyJoinLines setting.
    // Mutually exclusive with copySourcePosition — checked via the onChange handler.
    if (settingsStore.copyAsSourceWithQuotation) {
      const activeGroup = getActiveGroup()
      const source = activeGroup
        ? buildCommentarySource(activeGroup.bookTitle, getTocPath(activeGroup.bookId))
        : ''
      // Collect full line content. If copyJoinLines already ran, html is a single
      // <div> — strip note markers, div tags, then join. Otherwise re-collect from the DOM.
      let inlineText: string
      if (settingsStore.copyJoinLines) {
        inlineText = stripNoteMarkers(html).replace(/<\/?div>/gi, ' ').replace(/\s+/g, ' ').trim()
      } else {
        // Re-collect from blob lines for complete line content
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
      return `(${source}) "${inlineText}"`
    }

    return html + endnotesHtml
  }

  function onCopy(): void {
    // Fire the native copy event — useScopedCopy intercepts it and applies all
    // active flags (copyJoinLines, copySourcePosition, copyWithNotes, copyCleanText).
    triggerCopy()
  }

  // Uses the same copy path as onCopy — triggerCopy fires the copy event so useScopedCopy
  // writes the formatted HTML to the clipboard — then calls pasteIntoWord() from inside
  // the copy event handler, after the clipboard write is guaranteed complete.
  function onPasteIntoWord(): void {
    triggerCopy(() => pasteIntoWord().catch(() => {}))
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
    { label: 'העתק', action: onCopy, shortcut: 'Ctrl+C' },
    { label: 'העתק לתוך וורד', action: onPasteIntoWord, shortcut: 'Ctrl+V' },
    { label: 'העתק לחיפוש במאגר', action: onSearchInRepository, shortcut: 'Ctrl+Shift+C' },
    { label: 'בחר הכל', action: selectAllInContainer },
    { type: 'separator' },
    // Independent checkboxes — all can be active simultaneously
    {
      type: 'checkbox',
      label: 'העתק כרצף (ללא מעבר שורה)',
      get checked() { return settingsStore.copyJoinLines },
      onChange: (value: boolean) => { settingsStore.copyJoinLines = value },
    },
    // Radio pair — checking one unchecks the other (both off is valid)
    {
      type: 'checkbox',
      label: 'העתק עם מקור בהתחלה',
      get checked() { return settingsStore.copySourcePosition === 'start' },
      onChange: (value: boolean) => {
        settingsStore.copySourcePosition = value ? 'start' : null
        if (value) settingsStore.copyAsSourceWithQuotation = false
      },
    },
    {
      type: 'checkbox',
      label: 'העתק עם מקור בסוף',
      get checked() { return settingsStore.copySourcePosition === 'end' },
      onChange: (value: boolean) => {
        settingsStore.copySourcePosition = value ? 'end' : null
        if (value) settingsStore.copyWithNotes = false
        if (value) settingsStore.copyAsSourceWithQuotation = false
      },
    },
    {
      type: 'checkbox',
      label: 'העתק מקור עם ציטוט',
      get checked() { return settingsStore.copyAsSourceWithQuotation },
      onChange: (value: boolean) => {
        settingsStore.copyAsSourceWithQuotation = value
        if (value) settingsStore.copySourcePosition = null
        if (value) settingsStore.copyWithNotes = false
      },
    },
    // Independent checkboxes
    {
      type: 'checkbox',
      label: 'העתק עם הערות',
      get checked() { return settingsStore.copyWithNotes },
      onChange: (value: boolean) => {
        settingsStore.copyWithNotes = value
        if (value && settingsStore.copySourcePosition === 'end') settingsStore.copySourcePosition = null
        if (value) settingsStore.copyAsSourceWithQuotation = false
      },
    },
    {
      type: 'checkbox',
      label: 'העתק טקסט נקי',
      get checked() { return settingsStore.copyCleanText },
      onChange: (value: boolean) => { settingsStore.copyCleanText = value },
    },
    { type: 'separator' },
    annotationRow,
  ]

  return {
    contextMenuItems,
    buildFormattedHtml,
    onPasteIntoWord,
    onSearchInRepository,
  }
}
