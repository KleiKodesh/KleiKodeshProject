<script setup lang="ts">
import { ref, computed, nextTick } from 'vue'
import { useDropdownClose } from '@/composables/useDropdownClose'

export interface ContextMenuTextItem {
  type?: 'text'
  label: string
  action: () => void
}

export interface ContextMenuSeparatorItem {
  type: 'separator'
}

export interface ContextMenuCheckboxItem {
  type: 'checkbox'
  label: string
  checked: boolean
  onChange: (checked: boolean) => void
}

export interface ContextMenuComponentItem {
  type: 'component'
  component: import('vue').Component
  props?: Record<string, unknown>
}

export type ContextMenuItem =
  | ContextMenuTextItem
  | ContextMenuSeparatorItem
  | ContextMenuCheckboxItem
  | ContextMenuComponentItem

const props = defineProps<{ items: ContextMenuItem[] }>()

const visible = ref(false)
const x = ref(0)
const y = ref(0)
const menuRef = ref<HTMLElement>()
const menuStyle = computed(() => ({ left: `${x.value}px`, top: `${y.value}px` }))

// Saved selection range at the time the menu opened — restored before executing
// any action so that copy operations can read the user's original text selection.
let _savedRange: Range | null = null

function saveSelection(): void {
  const sel = window.getSelection()
  if (sel && sel.rangeCount > 0 && !sel.isCollapsed) {
    _savedRange = sel.getRangeAt(0).cloneRange()
  } else {
    _savedRange = null
  }
}

function restoreSelection(): void {
  if (!_savedRange) return
  const sel = window.getSelection()
  if (!sel) return
  sel.removeAllRanges()
  sel.addRange(_savedRange)
}

useDropdownClose(menuRef, () => {
  visible.value = false
})

async function show(event: MouseEvent) {
  event.preventDefault()
  saveSelection()
  await showAtPosition(event.clientX, event.clientY)
}

async function showAtPosition(clientX: number, clientY: number) {
  saveSelection()
  x.value = clientX
  y.value = clientY
  visible.value = true
  await nextTick()
  if (menuRef.value) {
    const rect = menuRef.value.getBoundingClientRect()
    if (x.value + rect.width > window.innerWidth) x.value = window.innerWidth - rect.width - 4
    if (y.value + rect.height > window.innerHeight) y.value = window.innerHeight - rect.height - 4
  }
}

function hide() {
  visible.value = false
}

function runItem(item: ContextMenuTextItem) {
  restoreSelection()
  item.action()
  hide()
}

function toggleCheckbox(item: ContextMenuCheckboxItem) {
  item.onChange(!item.checked)
  // Intentionally does NOT close the menu — checkbox is a persistent toggle
}

defineExpose({ show, showAtPosition, hide })
</script>

<template>
  <Teleport to="body">
    <div v-if="visible" ref="menuRef" class="context-menu" :style="menuStyle" @click.stop @mousedown.prevent>
      <template v-for="(item, index) in items" :key="index">
        <div v-if="item.type === 'separator'" class="context-menu-separator" />
        <component
          :is="item.component"
          v-else-if="item.type === 'component'"
          v-bind="item.props ?? {}"
          @close="hide"
        />
        <div
          v-else-if="item.type === 'checkbox'"
          class="context-menu-item context-menu-checkbox"
          @click="toggleCheckbox(item as ContextMenuCheckboxItem)"
        >
          <span class="checkbox-mark">{{ (item as ContextMenuCheckboxItem).checked ? '✓' : '' }}</span>
          <span>{{ (item as ContextMenuCheckboxItem).label }}</span>
        </div>
        <div v-else class="context-menu-item" @click="runItem(item as ContextMenuTextItem)">
          {{ (item as ContextMenuTextItem).label }}
        </div>
      </template>
    </div>
  </Teleport>
</template>

<style scoped>
.context-menu {
  position: fixed;
  z-index: 9999;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  box-shadow:
    0 2px 8px rgba(0, 0, 0, 0.12),
    0 8px 24px rgba(0, 0, 0, 0.08);
  min-width: 140px;
  direction: rtl;
  border-radius: 4px;
  padding-block: 2px;
}
.context-menu-separator {
  height: 1px;
  background: var(--border-color);
  margin-block: 2px;
}
.context-menu-item {
  padding: 0 12px;
  height: 26px;
  display: flex;
  align-items: center;
  cursor: pointer;
  font-size: 12px;
  line-height: 1;
  text-align: right;
}
.context-menu-item:hover {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}
.context-menu-item:active {
  background: color-mix(in srgb, var(--text-primary) 13%, transparent);
}
.context-menu-checkbox {
  gap: 6px;
}
.checkbox-mark {
  display: inline-block;
  width: 12px;
  text-align: center;
  font-size: 11px;
  color: var(--accent-color);
  flex-shrink: 0;
}
</style>
