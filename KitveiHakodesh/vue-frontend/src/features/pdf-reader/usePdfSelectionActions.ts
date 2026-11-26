import { type Ref } from 'vue'
import { useEventListener } from '@vueuse/core'
import type { EventBus } from 'pdfjs-dist/web/pdf_viewer.mjs'
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'

/**
 * Highlight the text selected in the pages - PDF.js's own 'highlightSelection' editing action.
 *
 * PDF.js reads the selection from the global `document`, so in a detached window it would never
 * see this window's selection: for the one synchronous call, the engine's `document.getSelection`
 * reads this window's selection instead. In the main window nothing is swapped.
 *
 * Highlight mode (the editing bar's highlight tool) in a detached window: the engine waits for the
 * selection on the main window only, so here the end of a drag runs the same action
 * (in the main window the engine does it itself - nothing is doubled).
 */
export function usePdfSelectionActions(options: {
  container: Ref<HTMLElement | null>
  eventBus: EventBus
  highlightMode: () => boolean
}) {
  const hostWindow = useHostWindow()

  /** The selection's range when it lies in a text layer of these pages, else null. */
  function pageSelection(): Range | null {
    const sel = hostWindow.getSelection()
    const container = options.container.value
    if (!sel || sel.rangeCount === 0 || sel.isCollapsed || !container) return null
    const range = sel.getRangeAt(0)
    const node = range.commonAncestorContainer
    const el = node.nodeType === Node.TEXT_NODE ? node.parentElement : (node as Element)
    if (!el || !container.contains(el)) return null
    const startEl = range.startContainer.nodeType === Node.TEXT_NODE ? range.startContainer.parentElement : (range.startContainer as Element)
    return startEl?.closest('.textLayer') ? range : null
  }

  function withHostSelection(fn: () => void) {
    // The MAIN window's document on purpose: that is the global the engine reads.
    const engineDocument = globalThis.document
    if (engineDocument === hostWindow.document) return fn()
    const own = Object.getOwnPropertyDescriptor(engineDocument, 'getSelection')
    Object.defineProperty(engineDocument, 'getSelection', { configurable: true, writable: true, value: () => hostWindow.getSelection() })
    try {
      fn()
    } finally {
      if (own) Object.defineProperty(engineDocument, 'getSelection', own)
      else delete (engineDocument as { getSelection?: unknown }).getSelection
    }
  }

  function run(name: 'highlightSelection') {
    if (!pageSelection()) return
    withHostSelection(() => options.eventBus.dispatch('editingaction', { source: null, name }))
  }

  // Highlight mode in a detached window (see above): the drag's end becomes the highlight.
  const detached = () => globalThis.document !== hostWindow.document
  function onSelectEnd() {
    if (options.highlightMode() && detached() && pageSelection()) run('highlightSelection')
  }
  useEventListener(hostWindow, 'pointerup', (e: PointerEvent) => { if (e.button === 0) hostWindow.requestAnimationFrame(onSelectEnd) })
  useEventListener(hostWindow, 'keyup', (e: KeyboardEvent) => { if (e.key === 'Shift') onSelectEnd() })

  return {
    hasPageSelection: () => !!pageSelection(),
    highlightSelection: () => run('highlightSelection'),
  }
}
