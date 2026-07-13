<script setup lang="ts">
/**
 * App-wide fallback right-click menu. Mounted once at the app root, it offers a
 * simple "העתק" (Copy) on any selected text that isn't already served by a more
 * specific menu (book view, commentary, txt, PDF).
 *
 * Deference rule: the specialized menus open via ContextMenu.show(), which calls
 * event.preventDefault(). Because those handlers sit on descendant elements, they
 * run before this document-level (bubble-phase) listener, so we simply bail when
 * the event was already handled (event.defaultPrevented). We also stay out of the
 * way of native editable-field menus (inputs/textarea keep their own cut/copy/paste)
 * by only acting when there is a real page text selection.
 */
import { ref, computed, onMounted, onBeforeUnmount } from 'vue'
import ContextMenu, { type ContextMenuItem } from '@/components/ContextMenu.vue'

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

function onCopy(): void {
  // ContextMenu restores the saved selection before invoking the action, so the
  // browser's native copy picks up exactly what the user had highlighted
  // (preserving both text/plain and text/html).
  try {
    document.execCommand('copy')
  } catch {
    /* ignore — clipboard unavailable */
  }
}

const items = computed<ContextMenuItem[]>(() => [
  { label: 'העתק', action: onCopy, shortcut: 'Ctrl+C' },
])

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
