import { ref, type Ref } from 'vue'
import { useEventListener } from '@vueuse/core'
import { showToast } from '@/composables/useToast'
import { htmlToText } from '@/utils/htmlText'
import { resolveErrorReportSource } from './errorReportSource'
import type { ErrorReportTarget } from './useBookViewErrorReport'

/**
 * "דיווח על טעות בספר" for the commentary pane.
 *
 * Separate from the book-view composable because the two panes identify a line
 * differently: the book view resolves a row index into its own lines array, while a
 * commentary line is addressed by `data-line-id` and belongs to whichever commentary
 * book the group came from — NOT to the book the main pane is showing. Reporting a
 * commentary mistake against the base book's id is exactly the bug this split avoids.
 */

/** The one commentary line a report can be about. */
export interface CommentaryReportLine {
  lineId: number
  lineIndex: number
  /** The stored markup; the report carries its text. */
  content: string
}

/** A book-view report target plus which commentary book it actually belongs to. */
export type CommentaryErrorReportTarget = ErrorReportTarget & {
  bookId: number
  bookTitle: string
  reference: string
}

interface CommentaryErrorReportOptions {
  scrollerEl: Ref<HTMLElement | null>
  /** The commentary book the pressed line belongs to, and its line, by line id. */
  findLine: (lineId: number) => {
    bookId: number
    bookTitle: string
    /** Book + section, as the citation reads. */
    reference: string
    line: CommentaryReportLine
  } | null
}

/** The selected text as PLAIN text, or '' when nothing in this pane is selected. */
function readSelectedText(scrollerEl: HTMLElement | null): string {
  const selection = window.getSelection()
  if (!selection || selection.rangeCount === 0 || selection.isCollapsed) return ''
  if (scrollerEl && !scrollerEl.contains(selection.anchorNode)) return ''
  return selection.toString().trim()
}

export function useCommentaryErrorReport(options: CommentaryErrorReportOptions): {
  target: Ref<CommentaryErrorReportTarget | null>
  openErrorReport: () => void
  closeErrorReport: () => void
  awaitSourceResolution: () => Promise<unknown>
} {
  const target = ref<CommentaryErrorReportTarget | null>(null)
  let sourceResolution: Promise<unknown> | null = null
  let pressedLineId: number | null = null

  function lineIdFromNode(node: Node | null): number | null {
    const element = node instanceof Element ? node : (node?.parentElement ?? null)
    const lineEl = element?.closest('[data-line-id]') as HTMLElement | null
    const raw = lineEl?.dataset['lineId']
    if (raw == null) return null
    const parsed = parseInt(raw, 10)
    return Number.isNaN(parsed) ? null : parsed
  }

  // Recorded on pointerdown for the same reason as the book view's tracker: at action
  // time the teleported menu overlays the point and the gesture is long gone.
  useEventListener(options.scrollerEl, 'pointerdown', (event: PointerEvent) => {
    pressedLineId = lineIdFromNode(event.target as Node | null)
  })

  function openErrorReport(): void {
    // A negative id is the synthetic "this commentator has no text for this line" row
    // (see the placeholder group in useCommentary). It carries a REAL bookId but its
    // text is app chrome, so reporting it would send a sentence that exists nowhere in
    // the corpus as if it were that book's wording.
    const isRealLine = pressedLineId != null && pressedLineId >= 0
    const found = isRealLine ? options.findLine(pressedLineId!) : null
    if (!found || found.line.lineIndex < 0) {
      showToast('לא זוהתה שורה לדיווח', { variant: 'error' })
      return
    }

    const originalLine = htmlToText(found.line.content).trim()
    const selectedText = readSelectedText(options.scrollerEl.value) || originalLine
    if (!selectedText) {
      showToast('לא זוהה טקסט לדיווח', { variant: 'error' })
      return
    }

    const resolvedLine = originalLine || selectedText
    target.value = {
      selectedText,
      originalLine: resolvedLine,
      selectionOffset: originalLine ? originalLine.indexOf(selectedText) : -1,
      lineIndex: found.line.lineIndex,
      source: null,
      bookId: found.bookId,
      bookTitle: found.bookTitle,
      reference: found.reference,
    }

    const opened = target.value
    sourceResolution = resolveErrorReportSource({
      bookId: found.bookId,
      lineIndex: found.line.lineIndex,
      displayedLine: resolvedLine,
      // The commentary pane renders a book's own text; it has no version overlay of
      // its own, so there is no alternate edition to disqualify the location read.
      activeVersionId: null,
    }).then((source) => {
      if (target.value === opened) target.value = { ...opened, source }
      return source
    })
  }

  function awaitSourceResolution(): Promise<unknown> {
    return sourceResolution ?? Promise.resolve()
  }

  function closeErrorReport(): void {
    target.value = null
    sourceResolution = null
  }

  return { target, openErrorReport, closeErrorReport, awaitSourceResolution }
}
