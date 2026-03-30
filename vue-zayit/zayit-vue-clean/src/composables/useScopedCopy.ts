import { useEventListener } from '@vueuse/core'
import type { Ref } from 'vue'

function toClipboard(event: ClipboardEvent, lines: string[]) {
  const htmlContent = `<!DOCTYPE html><html><head><meta charset="utf-8">
<style>body { direction: rtl; }</style></head><body>
${lines.map((l) => `<div>${l}</div>`).join('\n')}
</body></html>`

  const tempDiv = document.createElement('div')
  const textContent = lines
    .map((l) => {
      tempDiv.innerHTML = l
      return tempDiv.textContent ?? ''
    })
    .join('\n')

  event.clipboardData?.setData('text/html', htmlContent)
  event.clipboardData?.setData('text/plain', textContent)
  event.preventDefault()
}

export function useScopedCopy(
  scrollerEl: Ref<HTMLElement | null>,
  getLines: () => string[],
  isSelectAll: Ref<boolean>,
) {
  useEventListener(scrollerEl, 'copy', (event: ClipboardEvent) => {
    if (!isSelectAll.value) return
    toClipboard(event, getLines())
  })
}
