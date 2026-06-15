import type { Ref } from 'vue'
import type { ContextMenuItem } from '@/components/ContextMenu.vue'
import type { LineItem } from './useBookViewLinesTable'
import type { TocEntry } from '../toc/useBookViewToc'
import type { Note } from './useBookViewNotes'
import type { useTabStore } from '@/stores/tabStore'
import BookViewAnnotationMenuRow from './BookViewAnnotationMenuRow.vue'

type TabStore = ReturnType<typeof useTabStore>

interface CopyMenuOptions {
  scrollerEl: Ref<HTMLElement | null>
  lines: () => LineItem[]
  isSelectAll: Ref<boolean>
  selectAllInContainer: () => void
  bookTitle: string
  tabStore: TabStore
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

/**
 * Replaces user-note-marker superscripts with numbered references and returns
 * the modified HTML plus a list of endnote entries in encounter order.
 *
 * resolveNote receives a noteId and returns the Note (or undefined) by looking
 * it up in the live DOM + in-memory notes map — the caller provides this closure
 * because the DOM structure differs between book view (data-index) and commentary
 * view (data-line-id).
 */
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
  const items = endnotes
    .map(
      (e) =>
        `<li id="note-${e.number}"><a href="#ref-${e.number}" style="color:var(--accent-color,#0078d4);text-decoration:none">${e.number}.</a> ${e.noteText}</li>`,
    )
    .join('\n')
  return `<ol dir="rtl" style="padding-inline-start:1.5em">\n${items}\n</ol>`
}

// ── Shared DOM copy helper ────────────────────────────────────────────────────

function execCopyHtml(html: string): void {
  const container = document.createElement('div')
  container.setAttribute('dir', 'rtl')
  container.style.position = 'fixed'
  container.style.left = '-9999px'
  container.style.top = '-9999px'
  container.innerHTML = html
  document.body.appendChild(container)

  const selection = window.getSelection()
  const range = document.createRange()
  range.selectNodeContents(container)
  selection?.removeAllRanges()
  selection?.addRange(range)

  try {
    document.execCommand('copy')
  } finally {
    selection?.removeAllRanges()
    document.body.removeChild(container)
  }
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
    // When a renderer is provided (copy-with-notes path) use rendered content so
    // note markers are present. Otherwise use raw content (faster, no markers needed).
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

  // Find the lineIndex of the first DOM line element that intersects the selection
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

export function useBookViewLineCopyMenu(options: CopyMenuOptions): ContextMenuItem[] {
  const { scrollerEl, lines, isSelectAll, selectAllInContainer, bookTitle, tabStore } = options

  function buildSource(firstLineIndex: number | null, includeComma: boolean = true): string {
    const separator = includeComma ? ', ' : ' '
    if (firstLineIndex != null && options.getActiveTocEntry && options.getTocPath) {
      const entry = options.getActiveTocEntry(firstLineIndex)
      if (entry) return `${bookTitle}${separator}${options.getTocPath(entry)}`
    }
    // Fall back to the live scroll-position TOC path
    const tocPath = tabStore.activeTab.tocPath
    return tocPath ? `${bookTitle}${separator}${tocPath}` : bookTitle
  }

  function copyAsBlock(): void {
    const result = extractSelection(scrollerEl.value, lines(), isSelectAll.value)
    if (!result) return
    execCopyHtml(stripNoteMarkers(result.joined))
  }

  function copyWithSource(sourceAtEnd: boolean): void {
    const result = extractSelection(scrollerEl.value, lines(), isSelectAll.value)
    if (!result) return
    const { joined, firstLineIndex } = result
    const source = buildSource(firstLineIndex, sourceAtEnd)
    const cleanHtml = stripNoteMarkers(joined)

    const html = sourceAtEnd
      ? `${cleanHtml} (${source})`
      : `<h2 dir="rtl">${source}</h2>${cleanHtml}`

    execCopyHtml(html)
  }

  function copyWithNotes(sourceAtEnd: boolean): void {
    // For select-all: use rendered content so note markers are present in the HTML.
    // For partial selection: the DOM selection already has rendered innerHTML.
    const result = extractSelection(
      scrollerEl.value,
      lines(),
      isSelectAll.value,
      options.getRenderedLineContent,
    )
    if (!result) return
    const { joined, firstLineIndex } = result
    const source = buildSource(firstLineIndex, sourceAtEnd)

    function resolveNote(noteId: number): { noteText: string; quote: string } | undefined {
      if (!options.getNotesForLine) return undefined
      if (isSelectAll.value) {
        // Select-all: scan all lines directly — no need to touch the DOM.
        for (const lineItem of lines()) {
          const found = options.getNotesForLine(lineItem.id).find((n) => n.id === noteId)
          if (found) return { noteText: found.note, quote: found.quote }
        }
        return undefined
      }
      // Partial selection: resolve via the live scroller DOM using [data-index].
      if (!scrollerEl.value) return undefined
      const markerEl = scrollerEl.value.querySelector(
        `[data-note-id="${noteId}"]`,
      ) as HTMLElement | null
      if (!markerEl) return undefined
      const rowEl = markerEl.closest('[data-index]') as HTMLElement | null
      if (!rowEl) return undefined
      const lineItem = lines()[parseInt(rowEl.dataset['index'] ?? '', 10)]
      if (!lineItem) return undefined
      const found = options.getNotesForLine(lineItem.id).find((n) => n.id === noteId)
      return found ? { noteText: found.note, quote: found.quote } : undefined
    }

    const { html: textHtml, endnotes } = extractEndnotes(joined, resolveNote)
    const withSource = sourceAtEnd
      ? `${textHtml} (${source})`
      : `<h2 dir="rtl">${source}</h2>${textHtml}`
    execCopyHtml(withSource + buildEndnotesHtml(endnotes))
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

  return [
    { label: 'העתק', action: () => document.execCommand('copy') },
    { label: 'העתק כבלוק', action: copyAsBlock },
    { label: 'העתק עם מקור בסוף', action: () => copyWithSource(true) },
    { label: 'העתק עם מקור בהתחלה', action: () => copyWithSource(false) },
    { label: 'העתק עם הערות', action: () => copyWithNotes(false) },
    { label: 'בחר הכל', action: selectAllInContainer },
    { type: 'separator' },
    annotationRow,
  ]
}
