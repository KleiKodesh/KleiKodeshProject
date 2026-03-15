<script setup lang="ts">
import { ref, watch } from 'vue'
import { useDraggable } from '@vueuse/core'
import { useBookViewStore } from '@/stores/bookViewStore'
import {
  IconChevronUp20Regular,
  IconChevronDown20Regular,
  IconDismiss20Regular,
  IconReOrderDotsVertical20Regular,
} from '@iconify-prerendered/vue-fluent'

const props = defineProps<{ visible: boolean; toolbarVisible: boolean }>()
defineEmits<{ close: [] }>()

const bookViewStore = useBookViewStore()

const barRef = ref<HTMLElement | null>(null)
const handleRef = ref<HTMLElement | null>(null)

const BAR_WIDTH = 240
const APP_TITLE_BAR = 40
const BOOK_TOOLBAR = 32

function defaultPosition() {
  const topOffset = APP_TITLE_BAR + (props.toolbarVisible ? BOOK_TOOLBAR : 0) + 4
  return { x: window.innerWidth / 2 - BAR_WIDTH / 2, y: topOffset }
}

const { x, y, style } = useDraggable(barRef, {
  initialValue: bookViewStore.searchBarPos ?? defaultPosition(),
  handle: handleRef,
})

// Persist position globally whenever it changes
watch([x, y], ([nx, ny]) => {
  bookViewStore.setSearchBarPos({ x: nx, y: ny })
})
</script>

<template>
  <Transition name="search-bar">
    <div v-if="visible" ref="barRef" class="search-bar" :style="style">
      <span ref="handleRef" class="drag-handle">
        <IconReOrderDotsVertical20Regular />
      </span>
      <div class="search-inner">
        <input
          type="search"
          class="search-input"
          placeholder="חיפוש..."
          autofocus
        />
      </div>
      <span class="match-count">0 / 0</span>
      <button class="nav-btn"><IconChevronUp20Regular /></button>
      <button class="nav-btn"><IconChevronDown20Regular /></button>
      <button class="close-btn" @click="$emit('close')"><IconDismiss20Regular /></button>
    </div>
  </Transition>
</template>

<style scoped>
.search-bar {
  position: fixed;
  z-index: 9999;
  display: flex;
  align-items: center;
  gap: 4px;
  width: 240px;
  padding: 4px 6px;
  background: color-mix(in srgb, var(--bg-secondary) 96%, transparent);
  backdrop-filter: blur(12px);
  border: 1px solid var(--border-color);
  border-radius: 10px;
  box-sizing: border-box;
  user-select: none;
  touch-action: none;
}

.drag-handle {
  color: var(--text-secondary);
  flex-shrink: 0;
  cursor: grab;
  opacity: 0.5;
  display: flex;
  align-items: center;
  touch-action: none;
}
.drag-handle:active { cursor: grabbing; }

.search-inner {
  flex: 1;
  min-width: 0;
  display: flex;
  align-items: center;
  gap: 6px;
  background: color-mix(in srgb, var(--text-secondary) 12%, transparent);
  border-radius: 8px;
  padding: 4px 8px;
}

.search-icon {
  color: var(--text-secondary);
  flex-shrink: 0;
}

.search-input {
  flex: 1;
  min-width: 0;
  border: none;
  background: none;
  outline: none;
  font-size: 13px;
  color: var(--text-primary);
}
.search-input::placeholder { color: var(--text-secondary); }
.search-input::-webkit-search-cancel-button { filter: grayscale(1) opacity(0.4); }

.match-count {
  font-size: 11px;
  color: var(--text-secondary);
  white-space: nowrap;
  flex-shrink: 0;
}

.nav-btn,
.close-btn {
  display: flex;
  align-items: center;
  flex-shrink: 0;
  padding: 2px;
}

.search-bar-enter-active,
.search-bar-leave-active {
  transition: opacity 150ms ease, transform 150ms ease;
}
.search-bar-enter-from,
.search-bar-leave-to {
  opacity: 0;
  transform: translateY(-6px);
}
</style>
