<script setup lang="ts">
/**
 * App-wide fallback right-click menu. Mounted once at the app root, it offers
 * "העתק" (Copy), "העתק לתוך וורד" (Copy into Word) and the persistent
 * "העתק טקסט נקי" toggle (shared settingsStore.copyCleanText, same as the book/txt/FTS
 * menus) on any selected text that isn't already served by a more specific menu
 * (book view, commentary, txt, PDF).
 *
 * Deference rule: the specialized menus open via ContextMenu.show(), which calls
 * event.preventDefault(). Because those handlers sit on descendant elements, they
 * run before this document-level (bubble-phase) listener, so we simply bail when
 * the event was already handled (event.defaultPrevented). We also stay out of the
 * way of native editable-field menus (inputs/textarea keep their own cut/copy/paste)
 * by only acting when there is a real page text selection.
 */
import { ref, computed, onMounted, onBeforeUnmount } from 'vue'
import { useEventListener } from '@vueuse/core'
import ContextMenu, { type ContextMenuItem } from '@/components/ContextMenu.vue'
import { pasteIntoWord } from '@/webview-host/bridge'
import { useSettingsStore } from '@/stores/settingsStore'
import { cleanHebrewText } from '@/utils/hebrewTextCleaning'
import { wrapRtlHtml, htmlToPlainText } from '@/composables/useLineCopy'

const settingsStore = useSettingsStore()

const menuRef = ref<InstanceType<typeof ContextMenu> | null>(null)

function hasTextSelection(): boolean {
  const sel = window.getSelection()
  return (
    !!sel &&
    sel.rangeCount > 0 &&
    !sel.isCollapsed &&
    sel.toString().trim().length > 0
  )
}

function onCopy(): boolean {
  // ContextMenu restores the saved selection before invoking the action, so the
  // browser's native copy picks up exactly what the user had highlighted
  // (preserving both text/plain and text/html).
  try {
    return document.execCommand('copy')
  } catch {
    return false // clipboard unavailable
  }
}

function onPasteIntoWord(): void {
  // The copy event dispatch is synchronous — whether the native copy runs or the
  // clean-text listener below rewrites the payload, the clipboard is committed by
  // the time onCopy returns. Only then ask C# to open Word and paste, so Word can
  // never paste a stale clipboard.
  if (onCopy()) pasteIntoWord().catch(() => {})
}

const items = computed<ContextMenuItem[]>(() => [
  { label: 'העתק', action: onCopy, shortcut: 'Ctrl+C' },
  { label: 'העתק לתוך וורד', action: onPasteIntoWord },
  { type: 'separator' },
  {
    type: 'checkbox',
    label: 'העתק טקסט נקי',
    get checked() { return settingsStore.copyCleanText },
    onChange: (value: boolean) => { settingsStore.copyCleanText = value },
  },
])

// Honors the clean-text toggle for generic copies (menu העתק, Ctrl+C). Document-level
// bubble listener, so every specialized scoped-copy handler (book view, commentary,
// txt, FTS — element-level, they preventDefault once they commit a payload) runs first
// and wins; editable fields keep their native copy untouched.
useEventListener(document, 'copy', (event: ClipboardEvent) => {
  if (event.defaultPrevented) return // a scoped copy handler already wrote the payload
  if (!settingsStore.copyCleanText) return // native copy is fine as-is

  const target = event.target as HTMLElement | null
  if (target?.closest('input, textarea, [contenteditable]')) return

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

function onContextMenu(event: MouseEvent): void {
  if (event.defaultPrevented) return // a more specific menu already handled it
  if (!hasTextSelection()) return // no selection → leave the native menu alone
  menuRef.value?.show(event)
}

onMounted(() => document.addEventListener('contextmenu', onContextMenu))
onBeforeUnmount(() => document.removeEventListener('contextmenu', onContextMenu))
</script>

<template>
  <ContextMenu ref="menuRef" :items="items" />
</template>
