import { onScopeDispose, watch } from 'vue'
import type { BookViewReadAloud, ReadAloudPosition } from './useBookViewReadAloud'
import type { WindowedScrollerApi } from '@/components/windowedScrollerTypes'

/**
 * Marks the word being read aloud in the lines pane, and always keeps the line being read in view.
 *
 * The mark is a CSS Custom Highlight (`::highlight(kh-read-aloud)`), so the rendered line's
 * DOM is never touched. The spoken text and the rendered line can differ (markers, notes,
 * vowel points), so a word is found by counting LETTERS, like bookmark points are: the n-th
 * letter of the spoken text is the n-th letter of the row.
 *
 * Above the marked word a small label shows the word as it is spoken - with the nikkud
 * read aloud adds, or an abbreviation in full - when that differs from what the row shows.
 * The label floats over the page (fixed position); the row itself is still not touched.
 */
const HIGHLIGHT_NAME = 'kh-read-aloud'
const LETTER = /[\p{L}\p{N}]/u

function lettersBefore(text: string, end: number): number {
  let count = 0
  for (let i = 0; i < end && i < text.length; i++) if (LETTER.test(text[i]!)) count++
  return count
}

/** The (node, offset) just before the row's `letterIndex`-th letter, or its end. */
function pointAtLetter(row: HTMLElement, letterIndex: number): { node: Text; offset: number } | null {
  const walker = document.createTreeWalker(row, NodeFilter.SHOW_TEXT)
  let seen = 0
  let last: Text | null = null
  for (let node = walker.nextNode() as Text | null; node; node = walker.nextNode() as Text | null) {
    last = node
    const data = node.data
    for (let i = 0; i < data.length; i++) {
      if (!LETTER.test(data[i]!)) continue
      if (seen === letterIndex) return { node, offset: i }
      seen++
    }
  }
  return last ? { node: last, offset: last.data.length } : null
}

export function useBookViewReadAloudHighlight(
  readAloud: BookViewReadAloud,
  scroller: () => WindowedScrollerApi | null,
  /** Resolves true once the line is landed and mounted; false when a newer landing superseded it. */
  scrollToLine: (lineIndex: number) => Promise<boolean>,
) {
  const supported = typeof CSS !== 'undefined' && 'highlights' in CSS

  let label: HTMLElement | null = null

  function hideLabel() {
    label?.remove()
    label = null
  }

  /** Shows the spoken form of the word just above the marked range. */
  function showLabel(range: Range, spokenWord: string) {
    const word = spokenWord.trim()
    const shown = range.toString().trim()
    const rect = range.getBoundingClientRect()
    if (!word || word === shown || rect.width === 0) {
      hideLabel()
      return
    }
    if (!label) {
      label = document.createElement('div')
      label.className = 'kh-read-aloud-word'
      label.dir = 'rtl'
      document.body.appendChild(label)
    }
    label.textContent = word
    const style = getComputedStyle(range.startContainer.parentElement ?? document.body)
    label.style.fontFamily = style.fontFamily
    label.style.fontSize = style.fontSize
    const height = label.offsetHeight
    label.style.top = `${Math.max(0, rect.top - height - 4)}px`
    label.style.left = `${rect.left + rect.width / 2}px`
  }

  function clear() {
    if (supported) CSS.highlights.delete(HIGHLIGHT_NAME)
    hideLabel()
  }

  function rowFor(lineIndex: number): HTMLElement | null {
    const api = scroller()
    return api?.isRowRendered(lineIndex) ? api.rowElement(lineIndex) : null
  }

  /** Brings the line into view when it is not on screen. Null when it already shows. */
  function follow(lineIndex: number): Promise<boolean> | null {
    if (scroller()?.isRowFullyVisible(lineIndex)) return null
    return scrollToLine(lineIndex)
  }

  /** Marks the word at `pos` in its drawn row. */
  function markWord(pos: ReadAloudPosition) {
    const row = rowFor(pos.lineIndex)
    if (!row) return
    const firstLetter = lettersBefore(pos.text, pos.wordStart)
    const letterCount = lettersBefore(pos.text, pos.wordEnd) - firstLetter
    if (letterCount <= 0) return
    const start = pointAtLetter(row, firstLetter)
    const end = pointAtLetter(row, firstLetter + letterCount - 1)
    if (!start || !end) return
    const range = document.createRange()
    range.setStart(start.node, start.offset)
    // Past the last letter and any vowel points riding on it.
    let endOffset = end.offset + 1
    while (endOffset < end.node.data.length && /\p{M}/u.test(end.node.data[endOffset]!)) endOffset++
    range.setEnd(end.node, endOffset)
    CSS.highlights.set(HIGHLIGHT_NAME, new Highlight(range))
    showLabel(range, pos.spokenWord)
  }

  watch(
    readAloud.position,
    async (pos, previous) => {
      if (!pos) {
        clear()
        return
      }
      const landing = pos.lineIndex !== previous?.lineIndex ? follow(pos.lineIndex) : null
      if (!supported || !readAloud.highlightWords.value || pos.wordStart < 0) {
        clear()
        return
      }
      if (rowFor(pos.lineIndex)) {
        markWord(pos)
        return
      }
      clear()
      // The line is being landed and is not mounted yet: mark it once the landing has
      // mounted it - unless a newer landing or a newer word took over meanwhile.
      if (landing && (await landing) && readAloud.position.value === pos && rowFor(pos.lineIndex)) markWord(pos)
    },
  )

  watch(readAloud.highlightWords, (on) => {
    if (!on) clear()
  })

  onScopeDispose(clear)
}
