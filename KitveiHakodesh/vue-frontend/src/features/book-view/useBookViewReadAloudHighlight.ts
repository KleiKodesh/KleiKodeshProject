import { computed, nextTick, watch } from 'vue'
import type { BookViewReadAloud } from './useBookViewReadAloud'
import type { WindowedScrollerApi } from '@/components/windowedScrollerTypes'
import type { ReadAloudWordMark } from './lines/readAloudWordMark'

/**
 * The word being read aloud, for the lines pane to mark (the line renderer wraps it, like a
 * search match - see readAloudWordMark), and following along: the line being read is
 * brought into view, and on a line taller than the view, the word itself.
 *
 * The spoken text and the rendered line can differ (markers, notes, vowel points), so a
 * word is given by LETTERS: the n-th letter of the spoken text is the n-th letter of the row.
 */
const LETTER = /[\p{L}\p{N}]/u
/** Where a word that left the view is put back: this far below the viewport's top. */
const WORD_TOP_PX = 48

function lettersBefore(text: string, end: number): number {
  let count = 0
  for (let i = 0; i < end && i < text.length; i++) if (LETTER.test(text[i]!)) count++
  return count
}

export function useBookViewReadAloudHighlight(
  readAloud: BookViewReadAloud,
  scroller: () => WindowedScrollerApi | null,
  /** Resolves true once the line is landed and mounted; false when a newer landing superseded it. */
  scrollToLine: (lineIndex: number) => Promise<boolean>,
) {
  const word = computed<ReadAloudWordMark | null>(() => {
    const pos = readAloud.position.value
    if (!pos || pos.sentenceStart < 0) return null
    const letterStart = lettersBefore(pos.text, pos.sentenceStart)
    const letterCount = lettersBefore(pos.text, pos.sentenceEnd) - letterStart
    if (letterCount <= 0) return null
    return { lineIndex: pos.lineIndex, letterStart, letterCount }
  })

  /** The landing bringing the line being read into view, while it is under way. */
  let landing: Promise<boolean> | null = null

  /** Brings the line into view when it is not on screen. */
  function followLine(lineIndex: number) {
    if (scroller()?.isRowFullyVisible(lineIndex)) return
    // A line taller than the view is never fully visible: when its marked word shows, it is followed by the word.
    if (scroller()?.rowElement(lineIndex)?.querySelector('mark.kh-read-aloud') && wordIsVisible()) return
    const current = scrollToLine(lineIndex)
    landing = current
    void current.finally(() => {
      if (landing === current) landing = null
    })
  }

  function markedWord(): HTMLElement | null {
    const lineIndex = word.value?.lineIndex
    if (lineIndex == null) return null
    const api = scroller()
    if (!api?.isRowRendered(lineIndex)) return null
    return api.rowElement(lineIndex)?.querySelector<HTMLElement>('mark.kh-read-aloud') ?? null
  }

  function wordIsVisible(): boolean {
    const mark = markedWord()
    const viewport = scroller()?.scrollerElement
    if (!mark || !viewport) return true
    const m = mark.getBoundingClientRect()
    const v = viewport.getBoundingClientRect()
    return m.top >= v.top && m.bottom <= v.bottom
  }

  /** Scrolls the marked word back into view when it left it (a line taller than the view). */
  function followWord() {
    const mark = markedWord()
    const api = scroller()
    const viewport = api?.scrollerElement
    if (!mark || !api || !viewport) return
    const m = mark.getBoundingClientRect()
    const v = viewport.getBoundingClientRect()
    if (m.top >= v.top && m.bottom <= v.bottom) return
    api.nudge(Math.round(m.top - v.top - WORD_TOP_PX))
  }

  watch(
    () => readAloud.position.value?.lineIndex,
    (lineIndex, previous) => {
      if (lineIndex != null && lineIndex !== previous) followLine(lineIndex)
    },
  )

  // After the row is re-rendered with the new word marked: follow the word, once any
  // landing of its line has mounted it - the scroller's own landing promise, no timers.
  // nextTick: past the scroller's own update, which holds its anchor row in place - a
  // move made before it would be undone.
  watch(word, async (w) => {
    if (!w) return
    if (landing) {
      const pending = landing
      if (!(await pending) || word.value !== w) return
    }
    await nextTick()
    if (word.value === w) followWord()
  })

  return { word }
}
