import type { Ref } from 'vue'
import { useEventListener } from '@vueuse/core'
import type { LineItem } from './useBookViewLinesTable'
import { showToast } from '@/composables/useToast'
import { copyTextToClipboard } from '@/utils/clipboard'

// Deep-link format for a specific line of a book:
//   seforimapp://book/<bookId>?index=<lineIndex>
//
// Mirrors the otzaria:// links this app already parses (see HostLink.cs:
// `otzaria://open/book/<bookId>?index=<lineIndex>`) — same noun/id path with the
// locator as an `index` query parameter, and `index` means the same thing in both: a
// 0-based POSITIONAL line index, not a database row id. Keeping the shapes aligned
// means a future handler can treat them as one family, and leaves room to grow the
// same way Otzaria did (it adds `&mark` / `&m=<text>` for highlighting).
//
// Query parameter rather than a path segment or a `label:value` pair, deliberately:
//   - `book:<id>` inside the authority is parsed as a PORT, so the id lands in
//     url.port and any id above 65535 makes the URL throw outright.
//   - a bare `:` in a path segment parses, but some link detectors in chat and mail
//     clients end an auto-linked URL at the punctuation and truncate it.
//
// The app does NOT register itself as a handler for the seforimapp:// scheme, so
// clicking such a link does nothing today. The format exists so links copied now keep
// working if a handler is ever registered: it is URL-parseable (protocol
// 'seforimapp:', host 'book') and carries exactly the two values openBookTarget needs
// — bookId and openTocLineIndex.
export function buildLineLink(bookId: number, lineIndex: number): string {
  return `seforimapp://book/${bookId}?index=${lineIndex}`
}

interface LineLinkOptions {
  scrollerEl: Ref<HTMLElement | null>
  lines: () => LineItem[]
  bookId: number
}

/**
 * "העתק קישור לקטע זה" — copies a seforimapp:// deep link to the line the user
 * pressed to open the context menu.
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
