<script setup lang="ts">
import { ref } from 'vue'
import { useEventListener } from '@vueuse/core'
import { IconComment20Regular, IconHighlight20Regular } from '@iconify-prerendered/vue-fluent'
import type { EventBus } from 'pdfjs-dist/web/pdf_viewer.mjs'
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'

/**
 * The floating "highlight" button under a text selection in the pages - our own instead of
 * PDF.js's (enableHighlightFloatingButton), which reads the selection from the global
 * `document` and so never sees one in a detached window. Its buttons run PDF.js's own
 * 'highlightSelection' / 'commentSelection' editing actions, with the selection read from this window.
 *
 * Highlight mode (the editing bar's highlight tool) in a detached window: the engine waits for the
 * selection on the main window only, so the drag never becomes a highlight there. Here the end of
 * the drag runs the same 'highlightSelection' action (in the main window the engine does it itself).
 */
const props = defineProps<{
  /** The pages' scroll container (PDFViewer's). */
  container: HTMLElement | null
  /** The element the button is positioned in (position: relative). */
  host: HTMLElement | null
  eventBus: EventBus
  /** No button while an editor, OCR or the hand tool is active. */
  blocked: boolean
  /** The highlight editor is on: a selection ends as a highlight (detached windows: done here). */
  highlightMode: boolean
}>()

const hostWindow = useHostWindow()
const pos = ref<{ left: number; top: number } | null>(null)

/** The selection's range when it lies in a text layer of these pages, else null. */
function pageSelection(): Range | null {
  const sel = hostWindow.getSelection()
  if (!sel || sel.rangeCount === 0 || sel.isCollapsed || !props.container) return null
  const range = sel.getRangeAt(0)
  const node = range.commonAncestorContainer
  const el = node.nodeType === Node.TEXT_NODE ? node.parentElement : (node as Element)
  if (!el || !props.container.contains(el)) return null
  const startEl = range.startContainer.nodeType === Node.TEXT_NODE ? range.startContainer.parentElement : (range.startContainer as Element)
  return startEl?.closest('.textLayer') ? range : null
}

/** In a detached window the engine never sees this window's selection end. */
const detached = () => globalThis.document !== hostWindow.document
function onSelectEnd() {
  if (props.highlightMode) {
    if (detached() && pageSelection()) runAction('highlightSelection')
    return
  }
  place()
}

function place() {
  const range = props.blocked ? null : pageSelection()
  const rects = range ? Array.from(range.getClientRects()).filter((r) => r.width > 0) : []
  const last = rects.at(-1)
  if (!last || !props.host) return void (pos.value = null)
  const box = props.host.getBoundingClientRect()
  pos.value = { left: last.left + last.width / 2 - box.left, top: last.bottom - box.top + 4 }
}
const hide = () => (pos.value = null)

// Shown when the selecting ends (as PDF.js's: pointer up, or Shift released); hidden as soon as it goes.
useEventListener(hostWindow, 'pointerup', (e: PointerEvent) => { if (e.button === 0) hostWindow.requestAnimationFrame(onSelectEnd) })
useEventListener(hostWindow, 'keyup', (e: KeyboardEvent) => { if (e.key === 'Shift') onSelectEnd() })
useEventListener(hostWindow.document, 'selectionchange', () => { if (pos.value && !pageSelection()) hide() })
useEventListener(() => props.container, 'scroll', () => { if (pos.value) place() }, { passive: true })

/**
 * Runs `fn` with the engine's global `document.getSelection` reading this window's selection.
 * PDF.js's highlightSelection reads it synchronously, so the swap lasts for the call only.
 */
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

/** PDF.js's highlight (or highlight + note: its comment manager's dialog, PdfEditDialogs) of the selection. */
function runAction(name: 'highlightSelection' | 'commentSelection') {
  hide()
  withHostSelection(() => props.eventBus.dispatch('editingaction', { source: null, name }))
}
</script>

<template>
  <div
    v-if="pos"
    class="pdf-highlight-bar"
    role="toolbar"
    :style="{ left: `${pos.left}px`, top: `${pos.top}px` }"
    @pointerdown.prevent.stop
    @pointerup.stop
  >
    <button type="button" :title="$t('pdfReader.edit.highlight')" :aria-label="$t('pdfReader.edit.highlight')" @click="runAction('highlightSelection')">
      <IconHighlight20Regular />
    </button>
    <button type="button" :title="$t('pdfReader.edit.comment.title')" :aria-label="$t('pdfReader.edit.comment.title')" @click="runAction('commentSelection')">
      <IconComment20Regular />
    </button>
  </div>
</template>

<style scoped>
.pdf-highlight-bar {
  position: absolute;
  z-index: 20;
  transform: translateX(-50%);
  display: flex;
  gap: 2px;
  padding: 2px;
  border: 1px solid var(--border-color);
  border-radius: 6px;
  background: var(--bg-secondary);
  box-shadow: 0 2px 6px rgba(0, 0, 0, 0.2);
}
.pdf-highlight-bar button {
  display: grid;
  place-items: center;
  width: 30px;
  height: 30px;
  padding: 0;
  border: none;
  border-radius: 4px;
  background: transparent;
  color: var(--text-primary);
}
.pdf-highlight-bar button:hover {
  background: var(--control-bg-hover);
}
</style>
