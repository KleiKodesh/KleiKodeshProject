import { useEventListener } from '@vueuse/core'
import type { Ref } from 'vue'
import type { ContextMenuItem } from '@/components/ContextMenu.vue'
import { useSettingsStore } from '@/stores/settingsStore'
import { cleanHebrewText } from '@/utils/hebrewTextCleaning'
// Shared with the book-view copy path — see useLineCopy for why a single inline run
// must NOT be wrapped in a document (Word's CF_HTML fragment markers, not the tags,
// decide whether a paste terminates the paragraph).
import { wrapRtlHtml, htmlToPlainText } from '@/composables/useLineCopy'

interface TxtViewCopyMenuOptions {
  scrollerEl: Ref<HTMLElement | null>
}

export function useTxtViewCopyMenu(options: TxtViewCopyMenuOptions): {
  items: ContextMenuItem[]
  buildFormattedHtml: () => string | null
} {
  const { scrollerEl } = options
  const settingsStore = useSettingsStore()

  /**
   * Builds the final clipboard HTML for the current selection.
   *
   * copyJoinLines ON:  merge all selected .txt-line divs into one continuous prose
   *   run (space-joined, no per-line breaks). h2 headers are preserved as <h2>
   *   block elements and act as the only structural separators. The file's
   *   line-per-line display structure is collapsed away so the pasted text reads
   *   as flowing prose rather than a list of separate lines.
   * copyJoinLines OFF: use the raw browser selection HTML directly, which preserves
   *   whatever block structure the browser captured (one element per line).
   *
   * copyCleanText is applied after either path.
   *
   * Returns null when there is no selection.
   */
  function buildFormattedHtml(): string | null {
    const sel = window.getSelection()
    if (!sel || sel.rangeCount === 0 || sel.isCollapsed) return null

    const range = sel.getRangeAt(0)
    let html: string

    if (settingsStore.copyJoinLines && scrollerEl.value) {
      // Blob mode: collapse the file's per-line display structure into flowing prose.
      // Consecutive .txt-line divs are merged into a single space-joined run with no
      // line breaks between them. h2 headers flush the current run and are kept as
      // <h2> block elements — they are the only structural separators in the output.
      const blocks = Array.from(scrollerEl.value.children).filter((el) =>
        range.intersectsNode(el),
      )
      if (blocks.length === 0) {
        // Fallback: nothing matched as a block child — use raw fragment
        const fragment = range.cloneContents()
        const tmp = document.createElement('div')
        tmp.appendChild(fragment)
        html = tmp.innerHTML
      } else {
        const parts: string[] = []
        let pendingLines: string[] = []

        // Each merged run is wrapped in a <span> rather than left as a bare string
        // or wrapped in a <div>: a <div> IS a paragraph to Word's HTML importer and
        // would contribute the very line break join-lines mode exists to avoid,
        // while a bare string leaves a loose text node next to the <h2> blocks.
        function flushLines() {
          if (pendingLines.length > 0) {
            parts.push(`<span>${pendingLines.join(' ')}</span>`)
            pendingLines = []
          }
        }

        for (const el of blocks) {
          if (el.tagName === 'H2') {
            flushLines()
            parts.push(`<h2>${(el as HTMLElement).innerHTML}</h2>`)
          } else {
            pendingLines.push((el as HTMLElement).innerHTML)
          }
        }
        flushLines()
        html = parts.join('\n')
      }
    } else {
      const fragment = range.cloneContents()
      const tmp = document.createElement('div')
      tmp.appendChild(fragment)
      html = tmp.innerHTML
    }

    if (!html.trim()) return null

    if (settingsStore.copyCleanText) {
      html = cleanHebrewText(html)
    }

    return html
  }

  function onCopy(): void {
    // Fire the native copy event — useTxtViewScopedCopy intercepts it and
    // applies all active flags before writing to the clipboard.
    document.execCommand('copy')
  }

  function onSelectAll(): void {
    const container = scrollerEl.value
    if (!container) return
    const sel = window.getSelection()
    if (!sel) return
    const range = document.createRange()
    range.selectNodeContents(container)
    sel.removeAllRanges()
    sel.addRange(range)
  }

  const items: ContextMenuItem[] = [
    { label: 'העתק', action: onCopy },
    { label: 'בחר הכל', action: onSelectAll },
    { type: 'separator' },
    {
      type: 'checkbox',
      label: 'העתק כרצף (ללא מעבר שורה)',
      get checked() {
        return settingsStore.copyJoinLines
      },
      onChange: (value: boolean) => {
        settingsStore.copyJoinLines = value
      },
    },
    {
      type: 'checkbox',
      label: 'העתק טקסט נקי',
      get checked() {
        return settingsStore.copyCleanText
      },
      onChange: (value: boolean) => {
        settingsStore.copyCleanText = value
      },
    },
  ]

  return { items, buildFormattedHtml }
}

/**
 * Intercepts the native browser copy event on the txt-view scroller and writes
 * the formatted HTML (with active copy flags applied) to the clipboard.
 * Also handles dragstart so dragged text carries the same formatting.
 *
 * Must be called inside setup(). Cleans up automatically via VueUse.
 */
export function useTxtViewScopedCopy(
  scrollerEl: Ref<HTMLElement | null>,
  buildFormattedHtml: () => string | null,
): void {
  useEventListener(scrollerEl, 'copy', (event: ClipboardEvent) => {
    const formatted = buildFormattedHtml()
    if (formatted === null) return
    event.clipboardData?.setData('text/html', wrapRtlHtml(formatted))
    event.clipboardData?.setData('text/plain', htmlToPlainText(formatted))
    event.preventDefault()
  })

  useEventListener(scrollerEl, 'dragstart', (event: DragEvent) => {
    const formatted = buildFormattedHtml()
    if (formatted === null) return
    event.dataTransfer?.setData('text/html', wrapRtlHtml(formatted))
    event.dataTransfer?.setData('text/plain', htmlToPlainText(formatted))
  })
}
