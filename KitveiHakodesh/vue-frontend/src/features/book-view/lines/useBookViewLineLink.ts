import type { Ref } from 'vue'
import { useEventListener } from '@vueuse/core'
import type { LineItem } from './useBookViewLinesTable'
import { showToast } from '@/composables/useToast'
import { copyTextToClipboard } from '@/utils/clipboard'
import { buildLineLink } from '@/utils/appDeepLink'

interface LineLinkOptions {
  scrollerEl: Ref<HTMLElement | null>
  lines: () => LineItem[]
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
export function useBookViewLineLink(options: LineLinkOptions): { copyLineLink: () => void } {
  let pressedLineIndex: number | null = null

  function lineIndexFromNode(node: Node | null): number | null {
    const element = node instanceof Element ? node : (node?.parentElement ?? null)
    const row = element?.closest('[data-index]') as HTMLElement | null
    const dataIndex = row?.dataset['index']
    if (dataIndex == null) return null
    // data-index is the position in the lines array; the link wants the source lineIndex.
    return options.lines()[parseInt(dataIndex, 10)]?.lineIndex ?? null
  }

  useEventListener(options.scrollerEl, 'pointerdown', (event: PointerEvent) => {
    pressedLineIndex = lineIndexFromNode(event.target as Node | null)
  })

  function copyLineLink(): void {
    if (pressedLineIndex == null) {
      showToast('לא זוהתה שורה להעתקת קישור', { variant: 'error' })
      return
    }
    copyTextToClipboard(buildLineLink(options.bookId, pressedLineIndex)).then((copied) => {
      if (copied) showToast('הקישור הועתק')
      else showToast('העתקת הקישור נכשלה', { variant: 'error' })
    })
  }

  return { copyLineLink }
}
