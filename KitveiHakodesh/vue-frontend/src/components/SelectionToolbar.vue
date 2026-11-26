<script setup lang="ts">
/**
 * A small floating bar of icon buttons above the text selection (a "bubble menu").
 * It offers the SAME items as the place's right-click ContextMenu: the first icon items become
 * buttons, and the trailing "more" button opens that ContextMenu with the full list.
 * Logic: useSelectionToolbar. Look: ContextMenu's tokens (background, border, radius, shadow, hover).
 */
import { ref, computed } from 'vue'
import { IconMoreHorizontal20Regular } from '@iconify-prerendered/vue-fluent'
import { useI18n } from 'vue-i18n'
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'
import { useSelectionToolbar, selectionToolbarButtons } from '@/composables/useSelectionToolbar'
import type ContextMenu from '@/components/ContextMenu.vue'
import type { ContextMenuItem, ContextMenuTextItem } from '@/components/ContextMenu.vue'

const props = defineProps<{
  /** The element whose selections this bar serves. */
  container?: HTMLElement | null
  /** The app-wide fallback: any selection outside the containers that have their own bar. */
  fallback?: boolean
  /** The place's right-click menu items. */
  items: ContextMenuItem[]
  /** The place's right-click menu, opened by "more". */
  menu: InstanceType<typeof ContextMenu> | null
  isBlocked?: () => boolean
  prepare?: () => void
}>()

const { t } = useI18n()
const hostWindow = useHostWindow()
const toolbarRef = ref<HTMLElement | null>(null)

const toolbar = useSelectionToolbar({
  container: props.fallback ? null : computed(() => props.container ?? null),
  toolbar: toolbarRef,
  isBlocked: () => props.isBlocked?.() ?? false,
  prepare: () => props.prepare?.(),
})

const buttons = computed(() => selectionToolbarButtons(props.items))

function tooltip(item: ContextMenuTextItem): string {
  return item.shortcut ? `${item.label} (${item.shortcut})` : item.label
}

function run(item: ContextMenuTextItem): void {
  toolbar.restoreSelection()
  item.action()
  toolbar.hide()
}

function openMore(event: MouseEvent): void {
  const button = event.currentTarget as HTMLElement
  const rect = button.getBoundingClientRect()
  toolbar.restoreSelection()
  toolbar.hide()
  void props.menu?.showAtPosition(rect.left, rect.bottom + 2)
}

defineExpose({ hide: toolbar.hide })
</script>

<template>
  <Teleport :to="hostWindow.document.body">
    <div
      v-if="toolbar.visible.value && (buttons.length > 0 || menu)"
      ref="toolbarRef"
      class="selection-toolbar"
      role="toolbar"
      :aria-label="t('components.selectionToolbar.label')"
      :style="toolbar.style.value"
      @mousedown.prevent
      @click.stop
    >
      <button
        v-for="(item, index) in buttons"
        :key="index"
        type="button"
        class="selection-toolbar-button"
        :title="tooltip(item)"
        :aria-label="item.label"
        @click="run(item)"
      >
        <component :is="item.icon" />
      </button>
      <button
        v-if="menu"
        type="button"
        class="selection-toolbar-button"
        :title="t('components.selectionToolbar.more')"
        :aria-label="t('components.selectionToolbar.more')"
        @click="openMore"
      >
        <IconMoreHorizontal20Regular />
      </button>
    </div>
  </Teleport>
</template>

<style scoped>
/* ContextMenu's chrome: same background, border, radius and shadow. */
.selection-toolbar {
  position: fixed;
  z-index: 9998;
  display: flex;
  align-items: center;
  gap: 1px;
  padding: 2px;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  box-shadow:
    0 2px 8px rgba(0, 0, 0, 0.12),
    0 8px 24px rgba(0, 0, 0, 0.08);
  user-select: none;
}
.selection-toolbar-button {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 26px;
  padding: 0;
  border: none;
  border-radius: 3px;
  background: transparent;
  color: var(--text-primary);
  cursor: pointer;
}
.selection-toolbar-button svg {
  width: 16px;
  height: 16px;
}
/* ContextMenu's row hover/press tints. */
.selection-toolbar-button:hover {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}
.selection-toolbar-button:active {
  background: color-mix(in srgb, var(--text-primary) 13%, transparent);
}
</style>
