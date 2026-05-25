import { ref } from 'vue'
import type { ContextMenuItem } from '@/components/ContextMenu.vue'

/**
 * Manages copy operations for commentary content: block copy, copy with source,
 * and context menu items.
 */
export function useCommentaryCopy(
  getActiveGroup: () => { bookTitle: string; bookId: number } | null,
  getTocPath: (bookId: number) => string | undefined,
  selectAllInContainer: () => void,
) {
  const contextMenuRef = ref<any>(null)

  function buildCommentarySource(bookTitle: string, tocPath?: string): string {
    let cleanTitle = bookTitle.replace(/\s+מפרשים\s*$/, '').replace(/\s+רשנם\s*$/, '')
    return tocPath ? `${cleanTitle}, ${tocPath}` : cleanTitle
  }

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

    const bookTitle = activeGroup.bookTitle
    const tocPath = getTocPath(activeGroup.bookId)
    const source = buildCommentarySource(bookTitle, tocPath)

    const html = sourceAtEnd
      ? `${joined} (${source})`
      : `<h2 dir="rtl">${source}</h2>${joined}`

    execCopyHtml(html)
  }

  const contextMenuItems: ContextMenuItem[] = [
    { label: 'העתק', action: () => document.execCommand('copy') },
    { label: 'העתק כבלוק', action: copyAsBlock },
    { label: 'העתק עם מקור בסוף', action: () => copyWithSource(true) },
    { label: 'בחר הכל', action: selectAllInContainer },
  ]

  return {
    contextMenuRef,
    contextMenuItems,
    copyAsBlock,
    copyWithSource,
  }
}
