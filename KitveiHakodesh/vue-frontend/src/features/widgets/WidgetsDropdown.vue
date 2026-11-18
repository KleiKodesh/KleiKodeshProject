<script setup lang="ts">
import { ref, computed, onMounted, nextTick } from 'vue'
import { useEventListener } from '@vueuse/core'
import { IconAdd20Regular, IconDismiss16Regular } from '@iconify-prerendered/vue-fluent'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { useListKeys } from '@/composables/useListKeyNav'
import { pickLocalFilePath } from '@/webview-host/bridge'
import { t } from '@/i18n'
import { allWidgetItems } from './widgetItems'
import { useWidgetWindowStore, type CustomWidgetId } from './widgetWindowStore'

const emit = defineEmits<{ close: [] }>()
const props = defineProps<{ toggleButtonEl?: HTMLElement | null }>()

const store = useWidgetWindowStore()
const menuRef = ref<HTMLElement | null>(null)
const items = computed(() => allWidgetItems())

useDropdownClose(menuRef, () => emit('close'), {
  toggleButton: computed(() => props.toggleButtonEl ?? null),
})

// The widgets, then the Add row.
const { focusedIndex } = useListKeys(menuRef, () => items.value.length + 1, (index) => {
  if (index < items.value.length) onTap(index)
  else void onAdd()
})

useEventListener(menuRef, 'keydown', (e: KeyboardEvent) => {
  if (e.code === 'Escape') {
    e.preventDefault()
    e.stopPropagation()
    emit('close')
  }
})

onMounted(() => nextTick(() => menuRef.value?.focus()))

function onTap(index: number) {
  store.open(items.value[index]!.id)
  emit('close')
}

// Any local HTML page - an Otzaria add-in is a folder whose entry point is index.html.
async function onAdd() {
  const picked = await pickLocalFilePath()
  if (!picked) return
  if (!/\.html?$/i.test(picked.filePath)) {
    window.alert(t('layout.widgets.htmlOnly'))
    return
  }
  store.open(store.addCustom(picked.filePath).id)
  emit('close')
}

function onRemove(id: CustomWidgetId) {
  store.removeCustom(id)
}

</script>

<template>
  <div ref="menuRef" class="widgets-dropdown" tabindex="0" @click.stop>
    <button
      v-for="(item, index) in items"
      :key="item.id"
      class="widgets-row"
      :class="{ 'widgets-row--focused': focusedIndex === index, 'widgets-row--open': store.isOpen(item.id) }"
      data-nav-item
      :title="`${item.text}${item.shortcut ? ` (${item.shortcut})` : ''}\n${item.description}`"
      @click="onTap(index)"
    >
      <span class="widgets-icon"><component :is="item.icon" :style="item.color ? { color: item.color } : {}" /></span>
      <span class="widgets-label">{{ item.text }}</span>
      <span v-if="item.shortcut" class="widgets-shortcut">{{ item.shortcut }}</span>
      <span
        v-if="item.custom"
        class="widgets-remove"
        role="button"
        :title="$t('layout.widgets.remove')"
        @click.stop="onRemove(item.id as CustomWidgetId)"
      ><IconDismiss16Regular /></span>
    </button>
    <hr class="widgets-divider" />
    <button
      class="widgets-row"
      :class="{ 'widgets-row--focused': focusedIndex === items.length }"
      data-nav-item
      :title="$t('layout.widgets.addTitle')"
      @click="onAdd"
    >
      <span class="widgets-icon"><IconAdd20Regular /></span>
      <span class="widgets-label">{{ $t('layout.widgets.add') }}</span>
    </button>
  </div>
</template>

<style scoped>
.widgets-dropdown {
  position: absolute;
  top: calc(100% + 3px);
  inset-inline-end: 0;
  z-index: 200;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.18);
  min-width: 200px;
  max-height: calc(100vh - 60px);
  overflow-y: auto;
  outline: none;
}
.widgets-row {
  display: flex;
  align-items: center;
  gap: 10px;
  width: 100%;
  height: 32px;
  padding: 0 10px;
  background: none;
  border: none;
  cursor: pointer;
  text-align: start;
}
.widgets-row:hover,
.widgets-row--focused { background: color-mix(in srgb, var(--text-primary) 6%, transparent); }
.widgets-row--open { border-inline-start: 3px solid var(--accent-color, #3478f6); padding-inline-start: 7px; }
.widgets-icon { display: flex; align-items: center; color: var(--text-primary); }
.widgets-icon svg { width: 18px; height: 18px; }
.widgets-label { flex: 1; font-size: 13px; color: var(--text-primary); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.widgets-shortcut { font-size: 11px; color: var(--text-secondary); direction: ltr; }
.widgets-remove {
  display: flex;
  padding: 3px;
  border-radius: 3px;
  color: var(--text-secondary);
  opacity: 0;
}
.widgets-row:hover .widgets-remove { opacity: 1; }
.widgets-remove:hover { background: color-mix(in srgb, var(--text-primary) 10%, transparent); color: var(--text-primary); }
.widgets-divider { border: none; border-top: 1px solid var(--border-color); margin: 0; }
</style>
