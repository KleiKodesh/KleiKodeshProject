import { t } from '@/i18n'
import type { Ref } from 'vue'
import { useEventListener } from '@vueuse/core'
import type { LinesTable } from './linesTable'
import { showToast } from '@/composables/useToast'
import { copyTextToClipboard } from '@/utils/clipboard'
import { buildLineLink } from '@/utils/appDeepLink'

interface LineLinkOptions {
  scrollerEl: Ref<HTMLElement | null>
  lines: () => LinesTable
  bookId: number
}

/**
 * "העתק קישור לקטע זה" — copies a deep link (see @/utils/appDeepLink) to the line the
 * user pressed to open the context menu.
 *
 * The target line is recorded on `pointerdown` rather than resolved at action time:
 * both menu-opening gestures (right-click and touch long-press) begin with a
 * pointerdown on the line, while at action time the menu overlays the point and the
 * originating event is long gone. Clicks inside the menu itself never reach this
 * listener — the menu is teleported to <body>, outside the scroller.
 */
export function useBookViewLineLink(options: LineLinkOptions): {
  copyLineLink: () => void
  /**
   * The line the menu was opened on, for any menu action that needs a target line
   * even when nothing is selected (the error report). Read at action time; null when
   * the gesture did not start on a line.
   */
  pressedLineIndex: () => number | null
} {
  let pressedLineIndex: number | null = null

  function lineIndexFromNode(node: Node | null): number | null {
    const element = node instanceof Element ? node : (node?.parentElement ?? null)
    const row = element?.closest('[data-index]') as HTMLElement | null
    const dataIndex = row?.dataset['index']
    if (dataIndex == null) return null
    // data-index is the position in the lines table; the link wants the source lineIndex.
    return options.lines().at(parseInt(dataIndex, 10))?.lineIndex ?? null
  }

  useEventListener(options.scrollerEl, 'pointerdown', (event: PointerEvent) => {
    pressedLineIndex = lineIndexFromNode(event.target as Node | null)
  })

  function copyLineLink(): void {
    if (pressedLineIndex == null) {
      showToast(t('bookView.link.noLine'), { variant: 'error' })
      return
    }
    copyTextToClipboard(buildLineLink(options.bookId, pressedLineIndex)).then((copied) => {
      if (copied) showToast(t('bookView.link.copied'))
      else showToast(t('bookView.link.failed'), { variant: 'error' })
    })
  }

  return { copyLineLink, pressedLineIndex: () => pressedLineIndex }
}
