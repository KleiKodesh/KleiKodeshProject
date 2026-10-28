import { t } from '@/i18n'
import { ref, type Ref } from 'vue'
import { showToast } from '@/composables/useToast'
import { htmlToText } from '@/utils/htmlText'
import type { LinesTable } from '../lines/linesTable'
import { resolveErrorReportSource, type ErrorReportSource } from './errorReportSource'

/**
 * Collects what BookViewErrorReportDialog needs, at the moment the menu item is
 * clicked — because by the time the dialog is open the selection is gone and the
 * pressed line is forgotten.
 *
 * Lives beside the dialog rather than inside the copy menu: this is about WHICH text a
 * report is about, and the copy menu is about formatting text for the clipboard. The
 * two share nothing but the word "selection".
 */

export interface ErrorReportTarget {
  selectedText: string
  originalLine: string
  selectionOffset: number
  lineIndex: number
  /**
   * Resolved once, here, rather than inside the dialog: the dialog rebuilds its
   * payload on every keystroke and this involves database reads. null while the reads
   * are still in flight — the dialog opens immediately and fills this in after.
   */
  source: ErrorReportSource | null
}

interface ErrorReportOptions {
  lines: () => LinesTable
  /** The line the context-menu gesture started on — see useBookViewLineLink. */
  pressedLineIndex: () => number | null
  scrollerEl: Ref<HTMLElement | null>
  bookId: number | null
  /** The alternate edition on screen, if any — it invalidates the location read. */
  activeVersionId: () => number | null
}

/** The selected text as PLAIN text, or '' when nothing in this pane is selected. */
function readSelectedText(scrollerEl: HTMLElement | null): string {
  const selection = window.getSelection()
  if (!selection || selection.rangeCount === 0 || selection.isCollapsed) return ''
  // A selection somewhere else on the page (another pane, a sidebar) is not this
  // book's text and must not be reported as if it were.
  if (scrollerEl && !scrollerEl.contains(selection.anchorNode)) return ''
  return selection.toString().trim()
}

export function useBookViewErrorReport(options: ErrorReportOptions): {
  target: Ref<ErrorReportTarget | null>
  openErrorReport: () => void
  closeErrorReport: () => void
  awaitSourceResolution: () => Promise<unknown>
} {
  const target = ref<ErrorReportTarget | null>(null)
  let sourceResolution: Promise<unknown> | null = null

  function openErrorReport(): void {
    const lineIndex = options.pressedLineIndex()
    const line = lineIndex == null ? undefined : options.lines().at(lineIndex)
    // The line's stored content is HTML; a report is about the text a reader sees, and
    // a server matching it against its own copy would choke on our render markup.
    const originalLine = htmlToText(line?.content ?? '').trim()

    // Otzaria's rule: report the selection when there is one, otherwise the whole
    // pressed line with its HTML stripped.
    const selectedText = readSelectedText(options.scrollerEl.value) || originalLine
    if (!selectedText) {
      showToast(t('bookView.report.noText'), { variant: 'error' })
      return
    }

    const resolvedLine = originalLine || selectedText
    target.value = {
      selectedText,
      originalLine: resolvedLine,
      // -1 rather than 0 when the selection cannot be located in the line: 0 would
      // claim it starts at the beginning, and a server would re-anchor on that lie.
      selectionOffset: originalLine ? originalLine.indexOf(selectedText) : -1,
      lineIndex: lineIndex ?? -1,
      source: null,
    }

    // The dialog is open and usable immediately; the database reads land after. A
    // stale resolution (the user closed and reopened on another line meanwhile) is
    // discarded by the identity check rather than overwriting the newer target.
    //
    // The promise is kept so a send that beats the reads can await it — otherwise a
    // fast typist's correction would be dropped as unverifiable when the line was
    // perfectly verifiable.
    const opened = target.value
    sourceResolution = resolveErrorReportSource({
      bookId: options.bookId,
      lineIndex: lineIndex ?? -1,
      displayedLine: resolvedLine,
      activeVersionId: options.activeVersionId(),
    }).then((source) => {
      if (target.value === opened) target.value = { ...opened, source }
      return source
    })
  }

  /** Resolves once the provenance reads have landed — see openErrorReport. */
  function awaitSourceResolution(): Promise<unknown> {
    return sourceResolution ?? Promise.resolve()
  }

  function closeErrorReport(): void {
    target.value = null
    sourceResolution = null
  }

  return { target, openErrorReport, closeErrorReport, awaitSourceResolution }
}
