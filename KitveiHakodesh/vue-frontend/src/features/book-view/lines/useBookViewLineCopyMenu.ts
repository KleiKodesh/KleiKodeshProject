import type { Ref } from 'vue'
import type { ContextMenuItem } from '@/components/ContextMenu.vue'
import type { LineItem } from './useBookViewLinesTable'
import type { TocEntry } from '../toc/useBookViewToc'
import type { Note } from './useBookViewNotes'
import type { useTabStore } from '@/stores/tabStore'
import type { PaneNavigation } from '@/composables/usePaneNavigation'
import BookViewAnnotationMenuRow from './BookViewAnnotationMenuRow.vue'
import { cleanHebrewText } from '@/utils/hebrewTextCleaning'
import { escapeHtml, htmlToText } from '@/utils/htmlText'
import { applyCopyExclusivity, type CopyExclusivityToggle } from '../copyFlagExclusivity'
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

/**
 * Resolves the source `lineIndex` of the FIRST selected line from a live range.
 *
 * Virtualization-safe: a Range's boundary containers are always live DOM nodes —
 * if a line scrolled out of the virtualized DOM, the browser would have clamped the
 * selection at that boundary. So `range.startContainer` reliably belongs to the true
 * first line of the *current* selection. We walk up from it to the nearest
 * [data-index] row and map that array index to lines[idx].lineIndex.
 *
 * Falls back to the old intersect-scan over rendered .line nodes only if the walk-up
 * fails (e.g. the start boundary sits on the scroller container itself, not a row).
 */
function resolveFirstLineIndex(
  scrollerEl: HTMLElement | null,
  range: Range,
  lines: LineItem[],
): number | null {
  const indexFromNode = (node: Node | null): number | null => {
    const el = node instanceof Element ? node : node?.parentElement ?? null
    const row = el?.closest('[data-index]') as HTMLElement | null
    const dataIndex = row?.dataset['index']
    if (dataIndex == null) return null
    return lines[parseInt(dataIndex, 10)]?.lineIndex ?? null
  }

  const fromStart = indexFromNode(range.startContainer)
  if (fromStart != null) return fromStart

  if (scrollerEl) {
    for (const el of Array.from(scrollerEl.querySelectorAll('.line'))) {
      if (range.intersectsNode(el)) return indexFromNode(el)
    }
  }
  return null
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

  const firstLineIndex = resolveFirstLineIndex(scrollerEl, range, lines)

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
// copyJoinLines — "העתק כרצף (ללא מעבר שורה)" (independent checkbox)
//   Controls whether the selected lines keep a line break between them on paste.
//   ON:  JOIN the selected lines into ONE continuous run of text — the per-line
//        block structure is removed so nothing breaks between lines. Collected via
//        extractSelection (each .line's innerHTML) and joined into a single <div>.
//   OFF: use the raw browser selection HTML directly, which preserves each source
//        line as its own block (one line break per line on paste).
//   Has nothing to do with note markers.
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

export function useBookViewLineCopyMenu(options: CopyMenuOptions): { items: ContextMenuItem[], buildFormattedHtml: () => string | null, onPasteIntoWord: () => void, onSearchInRepository: () => void } {
  const { scrollerEl, lines, isSelectAll, selectAllInContainer, bookTitle, tabStore } = options
  const settingsStore = useSettingsStore()

  /**
   * Applies one copy-flag toggle through the shared exclusivity model and writes
   * the enforced result back to the store. All four exclusivity-governed checkboxes
   * go through here so the rules live in ONE place (copyFlagExclusivity.ts) and the
   * lines + commentary menus can't drift apart. See that file for the rule rationale
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

  function buildSource(firstLineIndex: number | null, includeComma: boolean = true): string {
    const separator = includeComma ? ', ' : ' '
    // TOC paths are stored with " · " between segments (search-UI display format).
    // A מקור reference should read as one continuous title, so collapse the segment
    // separators to a single space. Not a document format used in the sources.
    const flattenTocPath = (path: string): string => path.replace(/\s*·\s*/g, ' ')
    if (firstLineIndex != null && options.getActiveTocEntry && options.getTocPath) {
      const entry = options.getActiveTocEntry(firstLineIndex)
      if (entry) return `${bookTitle}${separator}${flattenTocPath(options.getTocPath(entry))}`
    }
    const tocPath = tabStore.activeTab.tocPath
    return tocPath ? `${bookTitle}${separator}${flattenTocPath(tocPath)}` : bookTitle
  }

  /**
   * Builds the final HTML for the current selection applying all active copy flags.
   * Returns null when there is no selection.
   * See the copy flag semantics block above for a full description of each flag.
   */
  function buildFormattedHtml(): string | null {
    // ── Acquire raw HTML and firstLineIndex ───────────────────────────────────
    // copyJoinLines ON:  use extractSelection (collects .line innerHTML, can use note renderer)
    // copyJoinLines OFF: use raw browser selection HTML directly
    let joined: string
    let firstLineIndex: number | null = null

    if (settingsStore.copyJoinLines) {
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
      // Resolve firstLineIndex from the selection anchor (virtualization-safe).
      firstLineIndex = resolveFirstLineIndex(scrollerEl.value, range, lines())
    }

    // ── Step 1: note markers ─────────────────────────────────────────────────
    let html: string
    let endnotesHtml = ''
    if (settingsStore.copyWithNotes) {
      // Resolve a note by its (globally-unique) id by scanning the lines model, not
      // the DOM. Note markers in `joined` only ever come from rendered lines, but the
      // note *data* lives in the model — so scanning lines() works identically whether
      // the owning row is currently virtualized in or out, and needs no DOM round-trip.
      function resolveNote(noteId: number): { noteText: string; quote: string } | undefined {
        if (!options.getNotesForLine) return undefined
        for (const lineItem of lines()) {
          const found = options.getNotesForLine(lineItem.id).find((n) => n.id === noteId)
          if (found) return { noteText: found.note, quote: found.quote }
        }
        return undefined
      }
      const { html: extracted, endnotes } = extractEndnotes(joined, resolveNote)
      html = extracted
      endnotesHtml = buildEndnotesHtml(endnotes)
    } else {
      html = stripNoteMarkers(joined)
    }

    // ── Step 2: copyJoinLines — flatten to one continuous run ─────────────────
    // Only when join-lines mode is on. extractSelection gave us the selected lines'
    // innerHTML joined with a space, so `html` is already the concatenated inline
    // content. We strip any block-level tags that would force a break between lines
    // (<div>/<p>/<br>, incl. those coming from a note renderer) and wrap the whole
    // thing in a SINGLE <div>, so the lines paste as one uninterrupted block with no
    // line break between them. OFF path keeps the raw per-line blocks (breaks kept).
    if (settingsStore.copyJoinLines) {
      const inline = html
        .replace(/<\/?(?:div|p)[^>]*>/gi, ' ')
        .replace(/<br\s*\/?>/gi, ' ')
        .replace(/\s{2,}/g, ' ')
        .trim()
      html = `<div>${inline}</div>`
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
    // Always collects the full line content and joins it into one inline paragraph
    // regardless of the copyJoinLines setting.
    // Mutually exclusive with copySourcePosition — checked via the onChange handler.
    if (settingsStore.copyAsSourceWithQuotation) {
      const source = buildSource(firstLineIndex, true)
      // Collect full line content. If copyJoinLines already ran, html is a single
      // <div> — strip note markers, div tags, then join. Otherwise re-collect via extractSelection.
      let inlineText: string
      if (settingsStore.copyJoinLines) {
        inlineText = stripNoteMarkers(html).replace(/<\/?div>/gi, ' ').replace(/\s+/g, ' ').trim()
      } else {
        const blobResult = extractSelection(scrollerEl.value, lines(), isSelectAll.value, options.getRenderedLineContent)
        const blobHtml = blobResult ? blobResult.joined : html
        inlineText = stripNoteMarkers(blobHtml).replace(/<[^>]*>/g, ' ').replace(/\s+/g, ' ').trim()
      }
      if (settingsStore.copyCleanText) inlineText = cleanHebrewText(inlineText)
      // Decode any surviving HTML entities to real text, then escape once — the
      // clipboard writer treats the return value as HTML, so bare </>/& must be
      // escaped (and not double-encoded). source is plain text from buildSource.
      return `<div dir="rtl">(${escapeHtml(source)}) "${escapeHtml(htmlToText(inlineText))}"</div>`
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
      { type: 'separator' },
      annotationRow,
    ],
    buildFormattedHtml,
    onPasteIntoWord,
    onSearchInRepository,
  }
}
