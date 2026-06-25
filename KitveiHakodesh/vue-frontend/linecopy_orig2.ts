import { useEventListener } from '@vueuse/core'
import type { Ref } from 'vue'
import { cleanHebrewText } from '@/utils/hebrewTextCleaning'
import { useSettingsStore } from '@/stores/settingsStore'

function wrapRtlHtml(innerHtml: string): string {
  return `<!DOCTYPE html><html><head><meta charset="utf-8">
<style>body { direction: rtl; }</style></head><body>
${innerHtml}
</body></html>`
}

function htmlToPlainText(html: string): string {
  const tempDiv = document.createElement('div')
  tempDiv.innerHTML = html
  return tempDiv.textContent ?? ''
}

function linesToHtml(lines: string[]): string {
  return lines.map((l) => `<div>${l}</div>`).join('\n')
}

function selectedHtml(): string {
  const selection = window.getSelection()
  if (!selection || selection.rangeCount === 0) return ''
  const range = selection.getRangeAt(0)
  const fragment = range.cloneContents()
  const container = document.createElement('div')
  container.appendChild(fragment)
  return container.innerHTML
}

function stripNoteMarkers(html: string): string {
  return html.replace(/<sup[^>]*class="user-note-marker"[^>]*>.*?<\/sup>/gs, '')
}

export function useScopedCopy(
  scrollerEl: Ref<HTMLElement | null>,
  getLines: () => string[],
  isSelectAll: Ref<boolean>,
) {
  const settingsStore = useSettingsStore()

  useEventListener(scrollerEl, 'copy', (event: ClipboardEvent) => {
    const raw = isSelectAll.value ? linesToHtml(getLines()) : selectedHtml()
    if (!raw.trim()) return
    let innerHtml = stripNoteMarkers(raw)
    if (settingsStore.copyCleanText) innerHtml = cleanHebrewText(innerHtml)

    const htmlContent = wrapRtlHtml(innerHtml)
    const plainText = htmlToPlainText(innerHtml)

    event.clipboardData?.setData('text/html', htmlContent)
    event.clipboardData?.setData('text/plain', plainText)
    event.preventDefault()
  })

  useEventListener(scrollerEl, 'dragstart', (event: DragEvent) => {
    const raw = isSelectAll.value ? linesToHtml(getLines()) : selectedHtml()
    if (!raw.trim()) return
    let innerHtml = stripNoteMarkers(raw)
    if (settingsStore.copyCleanText) innerHtml = cleanHebrewText(innerHtml)

    const htmlContent = wrapRtlHtml(innerHtml)
    const plainText = htmlToPlainText(innerHtml)

    event.dataTransfer?.setData('text/html', htmlContent)
    event.dataTransfer?.setData('text/plain', plainText)
  })
}
