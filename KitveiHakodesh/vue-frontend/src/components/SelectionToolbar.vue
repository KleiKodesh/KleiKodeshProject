<script setup lang="ts">
/**
 * A small floating bar of icon buttons above the text selection (a "bubble menu").
 * It offers the SAME items as the place's right-click ContextMenu: the first icon items become
 * buttons, and the trailing "more" button opens that ContextMenu with the full list.
 * Logic: useSelectionToolbar. Look: ContextMenu's tokens (background, border, radius, shadow, hover).
 */
import { ref, computed, watch } from 'vue'
import { IconMoreHorizontal20Regular } from '@iconify-prerendered/vue-fluent'
import { useI18n } from 'vue-i18n'
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'
import { useSelectionToolbar, selectionToolbarButtons, type SelectionToolbarButton } from '@/composables/useSelectionToolbar'
import type ContextMenu from '@/components/ContextMenu.vue'
import type { ContextMenuItem, ContextMenuTextItem, ContextMenuComponentItem } from '@/components/ContextMenu.vue'

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
  /** How many menu items become buttons (default 4); the rest stay under "more". */
  maxButtons?: number
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

const buttons = computed(() => selectionToolbarButtons(props.items, props.maxButtons))

// A component row's button opens that same row in a small panel under the bar (one at a time).
const openPanelIndex = ref<number | null>(null)
watch(toolbar.visible, (visible) => { if (!visible) openPanelIndex.value = null })

function buttonLabel(item: SelectionToolbarButton): string {
  return item.type === 'component' ? item.toolbar.label : item.label
}

function buttonIcon(item: SelectionToolbarButton) {
  return item.type === 'component' ? item.toolbar.icon : item.icon
}

function press(item: SelectionToolbarButton, index: number): void {
  if (item.type === 'component') {
    toolbar.restoreSelection()
    if (item.toolbar.action) {
      item.toolbar.action()
      toolbar.hide()
      return
    }
    openPanelIndex.value = openPanelIndex.value === index ? null : index
    return
  }
  run(item)
}

function closePanel(): void {
  openPanelIndex.value = null
  toolbar.hide()
}

const openPanelItem = computed(() => {
  const item = openPanelIndex.value == null ? null : buttons.value[openPanelIndex.value]
  return item?.type === 'component' ? (item as ContextMenuComponentItem) : null
})

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
        :class="{ open: openPanelIndex === index }"
        :title="item.type === 'component' ? buttonLabel(item) : tooltip(item)"
        :aria-label="buttonLabel(item)"
        @click="press(item, index)"
      >
        <component :is="buttonIcon(item)" />
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
      <div v-if="openPanelItem" class="selection-toolbar-panel">
        <component :is="openPanelItem.component" v-bind="openPanelItem.props ?? {}" @close="closePanel" />
      </div>
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
.selection-toolbar-button.open {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}
/* A component row opened from its button: ContextMenu's chrome and row metrics, under the bar. */
.selection-toolbar-panel {
  --context-menu-row-height: 27px;
  --context-menu-row-padding-block: 2px;
  --context-menu-row-padding-inline: 10px;
  position: absolute;
  top: calc(100% + 4px);
  inset-inline-start: 0;
  white-space: nowrap;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  box-shadow:
    0 2px 8px rgba(0, 0, 0, 0.12),
    0 8px 24px rgba(0, 0, 0, 0.08);
}
.selection-toolbar-button:active {
  background: color-mix(in srgb, var(--text-primary) 13%, transparent);
}
</style>
