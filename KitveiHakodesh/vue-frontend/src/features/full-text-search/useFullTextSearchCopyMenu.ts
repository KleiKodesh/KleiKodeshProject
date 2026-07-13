import { useEventListener } from '@vueuse/core'
import type { Ref } from 'vue'
import type { ContextMenuItem } from '@/components/ContextMenu.vue'
import { useSettingsStore } from '@/stores/settingsStore'
import { cleanHebrewText } from '@/utils/hebrewTextCleaning'

// Mirrors the book/txt view copy menus: a plain "העתק" plus a persistent
// "העתק טקסט נקי" toggle (shared settingsStore.copyCleanText) that strips
// nikud/te'amim from copied search-result text.

function htmlToPlainText(html: string): string {
  const tempDiv = document.createElement('div')
  tempDiv.innerHTML = html
  return tempDiv.textContent ?? ''
}

function wrapRtlHtml(innerHtml: string): string {
  return `<!DOCTYPE html><html><head><meta charset="utf-8">
<style>body { direction: rtl; }</style></head><body>
${innerHtml}
</body></html>`
}

export function useFullTextSearchCopyMenu(): { items: ContextMenuItem[] } {
  const settingsStore = useSettingsStore()

  function onCopy(): void {
    // Fire the native copy event — useFullTextSearchScopedCopy intercepts it when
    // clean-text copy is enabled and rewrites the clipboard payload.
    document.execCommand('copy')
  }

  const items: ContextMenuItem[] = [
    { label: 'העתק', action: onCopy, shortcut: 'Ctrl+C' },
    { type: 'separator' },
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

  return { items }
}

/**
 * Intercepts the native copy event on the results scroller. When "העתק טקסט נקי"
 * is on, it cleans the selected text (cleanHebrewText) before writing it to the
 * clipboard; otherwise it leaves the browser's native copy untouched.
 *
 * Must be called inside setup(). Cleans up automatically via VueUse.
 */
export function useFullTextSearchScopedCopy(scrollerEl: Ref<HTMLElement | null>): void {
  const settingsStore = useSettingsStore()

  useEventListener(scrollerEl, 'copy', (event: ClipboardEvent) => {
    if (!settingsStore.copyCleanText) return // native copy is fine as-is

    const sel = window.getSelection()
    if (!sel || sel.rangeCount === 0 || sel.isCollapsed) return

    const fragment = sel.getRangeAt(0).cloneContents()
    const tmp = document.createElement('div')
    tmp.appendChild(fragment)

    const html = cleanHebrewText(tmp.innerHTML)
    if (!html.trim()) return

    event.clipboardData?.setData('text/html', wrapRtlHtml(html))
    event.clipboardData?.setData('text/plain', htmlToPlainText(html))
    event.preventDefault()
  })
}
