import { ref } from 'vue'
import type { Ref } from 'vue'
import type { ContextMenuItem } from '@/components/ContextMenu.vue'
import BookViewAnnotationMenuRow from '../lines/BookViewAnnotationMenuRow.vue'

/**
 * Manages copy and annotation operations for commentary content: block copy,
 * copy with source, highlight/clear highlight, and context menu items.
 */
export function useCommentaryCopy(
  getActiveGroup: () => { bookTitle: string; bookId: number } | null,
  getTocPath: (bookId: number) => string | undefined,
  selectAllInContainer: () => void,
  scrollerEl: Ref<HTMLElement | null>,
  onHighlight: (lineId: number, startOffset: number, endOffset: number, colorArgb: number) => void,
  onClearHighlight: (lineId: number, startOffset: number, endOffset: number) => void,
) {
  const contextMenuRef = ref<any>(null)

  // ── Source builder ──────────────────────────────────────────────────────────

  function buildCommentarySource(bookTitle: string, tocPath?: string): string {
    let cleanTitle = bookTitle.replace(/\s+מפרשים\s*$/, '').replace(/\s+רשנם\s*$/, '')
    return tocPath ? `${cleanTitle}, ${tocPath}` : cleanTitle
  }

  // ── DOM copy helper ─────────────────────────────────────────────────────────

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

  // ── Selection extraction ────────────────────────────────────────────────────

  /**
   * Extracts the selected text range in terms of lineId + stripped character offsets.
   * Supports multi-line selections — returns one entry per .line element intersected.
   * Uses the same diacritic-stripped offset space as the highlight storage layer.
   */
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

      // lineId is stored on the element via data-line-id (set in the template)
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

  // ── Copy actions ────────────────────────────────────────────────────────────

  function copyAsBlock(): void {
    const sel = window.getSelection()
    if (!sel || sel.rangeCount === 0) return
    const range = sel.getRangeAt(0)
    const fragment = range.cloneContents()
    const tmp = document.createElement('div')
    tmp.appendChild(fragment)
    const joined = tmp.innerHTML
    if (!joined.trim()) return
    execCopyHtml(joined)
  }

  function copyWithSource(sourceAtEnd: boolean): void {
    const sel = window.getSelection()
    if (!sel || sel.rangeCount === 0) return
    const range = sel.getRangeAt(0)
    const fragment = range.cloneContents()
    const tmp = document.createElement('div')
    tmp.appendChild(fragment)
    const joined = tmp.innerHTML
    if (!joined.trim()) return

    const activeGroup = getActiveGroup()
    if (!activeGroup) return

    const tocPath = getTocPath(activeGroup.bookId)
    const source = buildCommentarySource(activeGroup.bookTitle, tocPath)

    const html = sourceAtEnd
      ? `${joined} (${source})`
      : `<h2 dir="rtl">${source}</h2>${joined}`

    execCopyHtml(html)
  }

  // ── Context menu items ──────────────────────────────────────────────────────

  const annotationRow: ContextMenuItem = {
    type: 'component',
    component: BookViewAnnotationMenuRow,
    props: {
      onHighlight: applyHighlightFromSelection,
      onClearHighlight: clearHighlightFromSelection,
      onAddNote: () => {},
    },
  }

  const contextMenuItems: ContextMenuItem[] = [
    { label: 'העתק', action: () => document.execCommand('copy') },
    { label: 'העתק כבלוק', action: copyAsBlock },
    { label: 'העתק עם מקור בסוף', action: () => copyWithSource(true) },
    { label: 'בחר הכל', action: selectAllInContainer },
    { type: 'separator' },
    annotationRow,
  ]

  return {
    contextMenuRef,
    contextMenuItems,
    copyAsBlock,
    copyWithSource,
  }
}
