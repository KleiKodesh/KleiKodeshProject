import { ref } from 'vue'
import type { Ref } from 'vue'
import type { ContextMenuItem } from '@/components/ContextMenu.vue'
import type { Note } from '../lines/useBookViewNotes'
import BookViewAnnotationMenuRow from '../lines/BookViewAnnotationMenuRow.vue'
import { cleanHebrewText } from '@/utils/hebrewTextCleaning'
import { useSettingsStore } from '@/stores/settingsStore'
import { useTabStore } from '@/stores/tabStore'
import { pasteIntoWord } from '@/webview-host/bridge'
import { execCopyHtmlToClipboard } from '@/composables/useLineCopy'

// ── Copy flag semantics ───────────────────────────────────────────────────────
//
// ALL copy paths (menu העתק, Ctrl+C, paste-to-Word) build HTML via buildFormattedHtml,
// then put the result on the clipboard. The RTL wrapper (dir="rtl") is always applied.
//
// copyAsBlob (independent checkbox)
//   ON:  each selected line is wrapped in <div>...</div>
//   OFF: lines joined as plain text, no div wrappers
//   Note: has nothing to do with note markers.
//
// copySourcePosition (radio pair — at most one active at a time)
//   'start': prepend <h2 dir="rtl">book, toc path</h2> before the text
//   'end':   append (book, toc path) after the text
//   null:    no source decoration
//
// copyWithNotes (independent checkbox)
//   ON:  convert user-note-marker superscripts to numbered endnotes
//   OFF: strip note markers from the HTML
//
// copyCleanText (independent checkbox)
//   ON:  run cleanHebrewText() on the result (strips diacritics/cantillation marks)
//   OFF: leave text as-is
//
// ── Paste-to-Word ─────────────────────────────────────────────────────────────
//
// "העתק לתוך וורד" follows the exact same path as "העתק" (builds HTML, sets clipboard),
// then additionally sends the pasteIntoWord bridge message so C# opens Word (or reuses
// the running instance) and calls Selection.Paste() to paste from the clipboard.
// ─────────────────────────────────────────────────────────────────────────────

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
  const contextMenuRef = ref<any>(null)
  const settingsStore = useSettingsStore()
  const tabStore = useTabStore()

  // ── Source builder ──────────────────────────────────────────────────────────

  function buildCommentarySource(bookTitle: string, tocPath?: string): string {
    const cleanTitle = bookTitle.replace(/\s+מפרשים\s*$/, '').replace(/\s+רשנם\s*$/, '')
    return tocPath ? `${cleanTitle}, ${tocPath}` : cleanTitle
  }

  // ── DOM copy helper ─────────────────────────────────────────────────────────
  // execCopyHtmlToClipboard (imported from useLineCopy) sets _isProgrammaticCopy
  // before calling execCommand so useScopedCopy skips re-processing.

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
    const items = endnotes
      .map((e) => `<li id="note-${e.number}"><a href="#ref-${e.number}" style="color:var(--accent-color,#0078d4);text-decoration:none">${e.number}.</a> ${e.noteText}</li>`)
      .join('\n')
    return `<ol dir="rtl" style="padding-inline-start:1.5em">\n${items}\n</ol>`
  }

  // ── Copy actions ────────────────────────────────────────────────────────────

  /**
   * Builds the final HTML for the current selection applying all active copy flags.
   * Returns null when there is no selection.
   * See the copy flag semantics block at the top of the file for a full description.
   */
  function buildFormattedHtml(): string | null {
    // ── Acquire raw HTML ──────────────────────────────────────────────────────
    // copyAsBlob ON:  use extractSelection (collects .line innerHTML from the scroller)
    // copyAsBlob OFF: use raw browser selection HTML directly
    const sel = window.getSelection()
    if (!sel || sel.rangeCount === 0 || sel.isCollapsed) return null
    const range = sel.getRangeAt(0)
    const fragment = range.cloneContents()
    const tmp = document.createElement('div')
    tmp.appendChild(fragment)
    let joined = tmp.innerHTML
    if (!joined.trim()) return null

    // copyAsBlob ON: also collect joined from .line elements for correct block wrapping
    if (settingsStore.copyAsBlob && scrollerEl.value) {
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

    // ── Step 2: copyAsBlob div-wrapping ──────────────────────────────────────
    // Only when blob mode is on — wrap each line in <div>...</div>
    if (settingsStore.copyAsBlob) {
      html = html
        .split(/\n+/)
        .map((line) => line.trim())
        .filter((line) => line.length > 0)
        .map((line) => `<div>${line}</div>`)
        .join('\n')
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

    return html + endnotesHtml
  }

  function onCopy(): void {
    const html = buildFormattedHtml()
    if (html === null) {
      document.execCommand('copy')
      return
    }
    execCopyHtmlToClipboard(html)
  }

  // Sets clipboard via execCopyHtmlToClipboard, then tells C# to open Word and call Selection.Paste().
  function onPasteIntoWord(): void {
    const html = buildFormattedHtml()
    if (html === null) return
    execCopyHtmlToClipboard(html)
    pasteIntoWord().catch(() => {})
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
    tabStore.updateActiveTab({ route: '/search', title: `חיפוש: ${query}`, searchQuery: query })
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
    { label: 'העתק', action: onCopy },
    { label: 'העתק לתוך וורד', action: onPasteIntoWord },
    { label: 'העתק לחיפוש במאגר', action: onSearchInRepository },
    { label: 'בחר הכל', action: selectAllInContainer },
    { type: 'separator' },
    // Independent checkboxes — all can be active simultaneously
    {
      type: 'checkbox',
      label: 'העתק כבלוק',
      get checked() { return settingsStore.copyAsBlob },
      onChange: (value: boolean) => { settingsStore.copyAsBlob = value },
    },
    // Radio pair — checking one unchecks the other (both off is valid)
    {
      type: 'checkbox',
      label: 'העתק עם מקור בהתחלה',
      get checked() { return settingsStore.copySourcePosition === 'start' },
      onChange: (value: boolean) => { settingsStore.copySourcePosition = value ? 'start' : null },
    },
    {
      type: 'checkbox',
      label: 'העתק עם מקור בסוף',
      get checked() { return settingsStore.copySourcePosition === 'end' },
      onChange: (value: boolean) => { settingsStore.copySourcePosition = value ? 'end' : null },
    },
    // Independent checkboxes
    {
      type: 'checkbox',
      label: 'העתק עם הערות',
      get checked() { return settingsStore.copyWithNotes },
      onChange: (value: boolean) => { settingsStore.copyWithNotes = value },
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
    contextMenuRef,
    contextMenuItems,
    buildFormattedHtml,
    // Kept for external callers (CommentaryView uses these directly)
    copyAsBlob: () => { const html = buildFormattedHtml(); if (html) execCopyHtmlToClipboard(html) },
    copyWithSource: (sourceAtEnd: boolean) => {
      const prev = settingsStore.copySourcePosition
      settingsStore.copySourcePosition = sourceAtEnd ? 'end' : 'start'
      const html = buildFormattedHtml()
      settingsStore.copySourcePosition = prev
      if (html) execCopyHtmlToClipboard(html)
    },
  }
}
