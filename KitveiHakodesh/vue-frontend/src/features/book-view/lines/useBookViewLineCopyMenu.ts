import type { Ref } from 'vue'
import type { ContextMenuItem } from '@/components/ContextMenu.vue'
import type { LineItem } from './useBookViewLinesTable'
import type { TocEntry } from '../toc/useBookViewToc'
import type { Note } from './useBookViewNotes'
import type { useTabStore } from '@/stores/tabStore'
import type { PaneNavigation } from '@/composables/usePaneNavigation'
import BookViewAnnotationMenuRow from './BookViewAnnotationMenuRow.vue'
import { cleanHebrewText } from '@/utils/hebrewTextCleaning'
import { useSettingsStore } from '@/stores/settingsStore'
import { pasteIntoWord } from '@/webview-host/bridge'
import { triggerCopy } from '@/composables/useLineCopy'

type TabStore = ReturnType<typeof useTabStore>

interface CopyMenuOptions {
  scrollerEl: Ref<HTMLElement | null>
  lines: () => LineItem[]
  isSelectAll: Ref<boolean>
  selectAllInContainer: () => void
  bookTitle: string
  tabStore: TabStore
  paneNavigation?: PaneNavigation
  getActiveTocEntry?: (lineIndex: number) => TocEntry | null
  getTocPath?: (entry: TocEntry) => string
  getNotesForLine?: (lineId: number) => Note[]
  getRenderedLineContent?: (raw: string, lineIndex: number, lineId: number) => string
  onHighlight?: (colorArgb: number) => void
  onClearHighlight?: () => void
  onAddNote?: () => void
}

// ── Note marker helpers ───────────────────────────────────────────────────────

/** Strips all user-note-marker superscripts from an HTML string. */
function stripNoteMarkers(html: string): string {
  return html.replace(/<sup[^>]*class="user-note-marker"[^>]*>.*?<\/sup>/gs, '')
}

interface EndnoteEntry {
  number: number
  noteText: string
  quote: string
}

function extractEndnotes(
  html: string,
  resolveNote: (noteId: number) => { noteText: string; quote: string } | undefined,
): { html: string; endnotes: EndnoteEntry[] } {
  const endnotes: EndnoteEntry[] = []
  let counter = 0
  const replaced = html.replace(
    /<sup[^>]*class="user-note-marker"[^>]*data-note-id="(\d+)"[^>]*>.*?<\/sup>/gs,
    (_match: string, noteIdStr: string) => {
      const noteId = parseInt(noteIdStr, 10)
      const note = resolveNote(noteId)
      if (!note) return ''
      counter++
      endnotes.push({ number: counter, noteText: note.noteText, quote: note.quote })
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

// ── Hebrew search query extraction ───────────────────────────────────────────

/** Strips everything that isn't a Hebrew letter or whitespace, preserving word integrity. */
function toHebrewSearchQuery(html: string): string {
  const text = html.replace(/<[^>]*>/g, ' ')
  const stripped = text.replace(/[^א-ת\s]/g, '')
  return stripped.replace(/\s+/g, ' ').trim()
}

// ── Selection extraction ──────────────────────────────────────────────────────

interface SelectionResult {
  joined: string
  firstLineIndex: number | null
}

function extractSelection(
  scrollerEl: HTMLElement | null,
  lines: LineItem[],
  isSelectAll: boolean,
  renderLine?: (raw: string, lineIndex: number, lineId: number) => string,
): SelectionResult | null {
  if (isSelectAll) {
    const joined = lines
      .filter((l) => l.content != null)
      .map((l) => (renderLine ? renderLine(l.content!, l.lineIndex, l.id) : l.content!))
      .join(' ')
    const firstLineIndex = lines.find((l) => l.content != null)?.lineIndex ?? null
    return { joined, firstLineIndex }
  }

  const sel = window.getSelection()
  if (!sel || sel.rangeCount === 0) return null
  const range = sel.getRangeAt(0)
  const fragment = range.cloneContents()
  const tmp = document.createElement('div')
  tmp.appendChild(fragment)

  let joined = Array.from(tmp.querySelectorAll('.line'))
    .map((el) => el.innerHTML)
    .join(' ')
  if (!joined) joined = tmp.innerHTML
  if (!joined.trim()) return null

  let firstLineIndex: number | null = null
  if (scrollerEl) {
    for (const el of Array.from(scrollerEl.querySelectorAll('.line'))) {
      if (range.intersectsNode(el)) {
        const dataIndex = (el.closest('[data-index]') as HTMLElement | null)?.dataset['index']
        if (dataIndex != null) {
          firstLineIndex = lines[parseInt(dataIndex, 10)]?.lineIndex ?? null
        }
        break
      }
    }
  }

  return { joined, firstLineIndex }
}

// ── Composable ────────────────────────────────────────────────────────────────

/**
 * Builds the full RTL HTML for exporting a book to Word.
 */
export function buildBookExportHtml(
  lines: LineItem[],
  bookTitle: string,
  renderLine: (raw: string, lineIndex: number, lineId: number) => string,
  getNotesForLine: (lineId: number) => Note[],
): string {
  function resolveNote(noteId: number): { noteText: string; quote: string } | undefined {
    for (const lineItem of lines) {
      const found = getNotesForLine(lineItem.id).find((n) => n.id === noteId)
      if (found) return { noteText: found.note, quote: found.quote }
    }
    return undefined
  }

  const renderedLines = lines
    .filter((l) => l.content != null)
    .map((l) => `<div class="book-line">${renderLine(l.content!, l.lineIndex, l.id)}</div>`)
    .join('\n')

  const { html: bodyHtml, endnotes } = extractEndnotes(renderedLines, resolveNote)
  const endnotesHtml = buildEndnotesHtml(endnotes)

  return (
    `<!DOCTYPE html><html dir="rtl" lang="he"><head><meta charset="utf-8">` +
    `<title>${bookTitle}</title>` +
    `<style>body{direction:rtl;font-family:"David","Times New Roman",serif;font-size:14pt;line-height:1.8}` +
    `.book-line{margin-bottom:4pt}a{color:#0078d4}sup{font-size:0.7em}ol{margin-top:12pt}li{margin-bottom:4pt}` +
    `</style></head><body>` +
    `<h1>${bookTitle}</h1>` +
    bodyHtml +
    endnotesHtml +
    `</body></html>`
  )
}

// ── Copy flag semantics ───────────────────────────────────────────────────────
//
// ALL copy paths (menu העתק, Ctrl+C) go through useScopedCopy's copy event handler
// which calls buildFormattedHtml and writes to event.clipboardData.
// onCopy fires document.execCommand('copy') to trigger that event.
// onPasteIntoWord calls buildFormattedHtml directly, sets clipboard via
// execCopyHtmlToClipboard, then sends the pasteIntoWord bridge message so C# opens
// Word and calls Selection.Paste().
//
// copyAsBlob (independent checkbox)
//   ON:  use extractSelection to collect .line element innerHTML, wrap each line in
//        <div>...</div>. Has nothing to do with note markers.
//   OFF: use raw browser selection HTML directly.
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
//   ON:  run cleanHebrewText() (strips diacritics/cantillation marks)
//   OFF: leave text as-is
// ─────────────────────────────────────────────────────────────────────────────

export function useBookViewLineCopyMenu(options: CopyMenuOptions): { items: ContextMenuItem[], buildFormattedHtml: () => string | null } {
  const { scrollerEl, lines, isSelectAll, selectAllInContainer, bookTitle, tabStore } = options
  const settingsStore = useSettingsStore()

  function buildSource(firstLineIndex: number | null, includeComma: boolean = true): string {
    const separator = includeComma ? ', ' : ' '
    if (firstLineIndex != null && options.getActiveTocEntry && options.getTocPath) {
      const entry = options.getActiveTocEntry(firstLineIndex)
      if (entry) return `${bookTitle}${separator}${options.getTocPath(entry)}`
    }
    const tocPath = tabStore.activeTab.tocPath
    return tocPath ? `${bookTitle}${separator}${tocPath}` : bookTitle
  }

  /**
   * Builds the final HTML for the current selection applying all active copy flags.
   * Returns null when there is no selection.
   * See the copy flag semantics block above for a full description of each flag.
   */
  function buildFormattedHtml(): string | null {
    // ── Acquire raw HTML and firstLineIndex ───────────────────────────────────
    // copyAsBlob ON:  use extractSelection (collects .line innerHTML, can use note renderer)
    // copyAsBlob OFF: use raw browser selection HTML directly
    let joined: string
    let firstLineIndex: number | null = null

    if (settingsStore.copyAsBlob) {
      const renderLine = settingsStore.copyWithNotes ? options.getRenderedLineContent : undefined
      const result = extractSelection(scrollerEl.value, lines(), isSelectAll.value, renderLine)
      if (!result) return null
      joined = result.joined
      firstLineIndex = result.firstLineIndex
    } else {
      const sel = window.getSelection()
      if (!sel || sel.rangeCount === 0 || sel.isCollapsed) return null
      const range = sel.getRangeAt(0)
      const fragment = range.cloneContents()
      const tmp = document.createElement('div')
      tmp.appendChild(fragment)
      joined = tmp.innerHTML
      if (!joined.trim()) return null
      // Resolve firstLineIndex from the DOM for source building
      if (scrollerEl.value) {
        for (const el of Array.from(scrollerEl.value.querySelectorAll('.line'))) {
          if (range.intersectsNode(el)) {
            const dataIndex = (el.closest('[data-index]') as HTMLElement | null)?.dataset['index']
            if (dataIndex != null) {
              firstLineIndex = lines()[parseInt(dataIndex, 10)]?.lineIndex ?? null
            }
            break
          }
        }
      }
    }

    // ── Step 1: note markers ─────────────────────────────────────────────────
    let html: string
    let endnotesHtml = ''
    if (settingsStore.copyWithNotes) {
      function resolveNote(noteId: number): { noteText: string; quote: string } | undefined {
        if (!options.getNotesForLine) return undefined
        if (isSelectAll.value) {
          for (const lineItem of lines()) {
            const found = options.getNotesForLine(lineItem.id).find((n) => n.id === noteId)
            if (found) return { noteText: found.note, quote: found.quote }
          }
          return undefined
        }
        if (!scrollerEl.value) return undefined
        const markerEl = scrollerEl.value.querySelector(`[data-note-id="${noteId}"]`) as HTMLElement | null
        if (!markerEl) return undefined
        const rowEl = markerEl.closest('[data-index]') as HTMLElement | null
        if (!rowEl) return undefined
        const lineItem = lines()[parseInt(rowEl.dataset['index'] ?? '', 10)]
        if (!lineItem) return undefined
        const found = options.getNotesForLine(lineItem.id).find((n) => n.id === noteId)
        return found ? { noteText: found.note, quote: found.quote } : undefined
      }
      const { html: extracted, endnotes } = extractEndnotes(joined, resolveNote)
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
    if (position === 'start') {
      const source = buildSource(firstLineIndex, false)
      html = `<h2 dir="rtl">${source}</h2>${html}`
    } else if (position === 'end') {
      const source = buildSource(firstLineIndex, true)
      html = `${html} (${source})`
    }

    // ── Step 5: copyAsSourceWithQuotation ─────────────────────────────────────
    // Produces a single line: (מקור) "ציטוט"
    // Always collects full line blocks (blob-style) and joins them into one inline
    // paragraph regardless of the copyAsBlob setting.
    // Mutually exclusive with copySourcePosition — checked via the onChange handler.
    if (settingsStore.copyAsSourceWithQuotation) {
      const source = buildSource(firstLineIndex, true)
      // Collect full line content. If copyAsBlob already ran, html has block-wrapped
      // lines — strip note markers, div tags, then join. Otherwise re-collect via extractSelection.
      let inlineText: string
      if (settingsStore.copyAsBlob) {
        inlineText = stripNoteMarkers(html).replace(/<\/?div>/gi, ' ').replace(/\s+/g, ' ').trim()
      } else {
        const blobResult = extractSelection(scrollerEl.value, lines(), isSelectAll.value, options.getRenderedLineContent)
        const blobHtml = blobResult ? blobResult.joined : html
        inlineText = stripNoteMarkers(blobHtml).replace(/<[^>]*>/g, ' ').replace(/\s+/g, ' ').trim()
      }
      if (settingsStore.copyCleanText) inlineText = cleanHebrewText(inlineText)
      return `(${source}) "${inlineText}"`
    }

    return html + endnotesHtml
  }

  function onCopy(): void {
    // Fire the native copy event — useScopedCopy intercepts it and applies all
    // active flags (copyAsBlob, copySourcePosition, copyWithNotes, copyCleanText).
    triggerCopy()
  }

  // Uses the same copy path as onCopy — triggerCopy fires the copy event so useScopedCopy
  // writes the formatted HTML to the clipboard — then calls pasteIntoWord() from inside
  // the copy event handler, after the clipboard write is guaranteed complete.
  function onPasteIntoWord(): void {
    triggerCopy(() => pasteIntoWord().catch(() => {}))
  }

  function onSearchInRepository(): void {
    const result = extractSelection(scrollerEl.value, lines(), isSelectAll.value)
    if (!result) return
    const query = toHebrewSearchQuery(result.joined)
    if (!query) return
    const nav: PaneNavigation | undefined = options.paneNavigation
    if (nav) {
      nav.updateActiveTab({ route: '/search', title: `חיפוש: ${query}`, searchQuery: query })
    } else {
      tabStore.updateActiveTab({ route: '/search', title: `חיפוש: ${query}`, searchQuery: query })
    }
  }

  const annotationRow: ContextMenuItem = {
    type: 'component',
    component: BookViewAnnotationMenuRow,
    props: {
      onHighlight: options.onHighlight ?? (() => {}),
      onClearHighlight: options.onClearHighlight ?? (() => {}),
      onAddNote: options.onAddNote ?? (() => {}),
    },
  }

  return {
    items: [
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
          // מקור בסוף is incompatible with הערות — clear notes when end-source is enabled
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
          // הערות is incompatible with מקור בסוף — clear end-source when notes are enabled
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
    ],
    buildFormattedHtml,
  }
}
