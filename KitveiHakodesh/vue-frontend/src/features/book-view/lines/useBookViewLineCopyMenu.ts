import type { Ref } from 'vue'
import type { ContextMenuItem } from '@/components/ContextMenu.vue'
import type { LineItem } from './useBookViewLinesTable'
import type { TocEntry } from '../toc/useBookViewToc'
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
  onHighlight?: (colorArgb: number) => void
  onClearHighlight?: () => void
  onAddNote?: () => void
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
): SelectionResult | null {
  if (isSelectAll) {
    const joined = lines.map((l) => l.content).filter(Boolean).join(' ')
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
    execCopyHtml(result.joined)
  }

  function copyWithSource(sourceAtEnd: boolean): void {
    const result = extractSelection(scrollerEl.value, lines(), isSelectAll.value)
    if (!result) return
    const { joined, firstLineIndex } = result
    const source = buildSource(firstLineIndex, sourceAtEnd)

    // sourceAtEnd: append "(source)" inline after the text
    // sourceAtStart: <h2> is a block element — no <br> needed, it creates its own break
    const html = sourceAtEnd
      ? `${joined} (${source})`
      : `<h2 dir="rtl">${source}</h2>${joined}`

    execCopyHtml(html)
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
    { label: 'בחר הכל', action: selectAllInContainer },
    { type: 'separator' },
    annotationRow,
  ]
}
