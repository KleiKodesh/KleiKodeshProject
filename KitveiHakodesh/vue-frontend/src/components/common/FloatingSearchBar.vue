<script setup lang="ts">
import { ref, watch, nextTick, computed } from 'vue'
import {
  IconChevronUp20Regular,
  IconChevronDown20Regular,
  IconDismiss20Regular,
} from '@iconify-prerendered/vue-fluent'
import { useFloatingPanel, type FloatingPanelPosition } from '@/composables/useFloatingPanel'

const props = defineProps<{
  visible: boolean
  query: string
  matchCount: number | null
  matchIndex: number | null
  notFound: boolean
  placeholder?: string
  initialPosition: FloatingPanelPosition
  savedPosition?: FloatingPanelPosition | null
}>()

const emit = defineEmits<{
  'update:query': [string]
  next: []
  previous: []
  close: []
  positionChange: [FloatingPanelPosition]
}>()

const inputRef = ref<HTMLInputElement | null>(null)

const { panelRef, panelStyle } = useFloatingPanel({
  initialPosition: props.savedPosition ?? props.initialPosition,
})

watch(
  () => props.visible,
  (visible) => { if (visible) nextTick(() => inputRef.value?.focus()) },
)

const matchLabel = computed(() => {
  if (props.notFound) return 'לא נמצא'
  if (props.matchCount !== null && props.matchCount > 0) return `${props.matchIndex} / ${props.matchCount}`
  return ''
})

function onKeydown(event: KeyboardEvent) {
  if (event.key === 'Enter') event.shiftKey ? emit('previous') : emit('next')
  else if (event.key === 'Escape') emit('close')
}

defineExpose({ focus: () => inputRef.value?.focus() })
</script>

<template>
  <Transition name="search-bar">
    <div v-if="visible" ref="panelRef" class="search-bar" :style="panelStyle">
      <div class="search-inner">
        <input
          ref="inputRef"
          :value="query"
          type="search"
          class="search-input"
          :placeholder="placeholder ?? 'חיפוש...'"
          spellcheck="true"
          autocomplete="on"
          @input="emit('update:query', ($event.target as HTMLInputElement).value)"
          @keydown="onKeydown"
        />
        <span class="match-count" :class="{ 'no-match': notFound }">{{ matchLabel }}</span>
      </div>

      <!-- Slot for custom buttons before nav (e.g. mode toggle, filter) -->
      <slot name="before-nav" />

      <span v-if="$slots['before-nav']" class="sep" />

      <button class="nav-btn" :disabled="matchCount === 0" @click="emit('previous')">
        <IconChevronUp20Regular />
      </button>
      <button class="nav-btn" :disabled="matchCount === 0" @click="emit('next')">
        <IconChevronDown20Regular />
      </button>

      <!-- Slot for custom buttons after nav (e.g. options panel toggle) -->
      <slot name="after-nav" />

      <span class="sep" />
      <button class="close-btn" @click="emit('close')"><IconDismiss20Regular /></button>

      <!-- Slot for panels that anchor below the bar (options panel, etc.) -->
      <slot name="panel" />
    </div>
  </Transition>
</template>

<style scoped>
.search-bar {
  position: fixed;
  z-index: 9999;
  display: flex;
  align-items: center;
  gap: 2px;
  width: fit-content;
  padding: 1px 3px;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  box-sizing: border-box;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.4), 0 1px 3px rgba(0, 0, 0, 0.25);
}

.search-inner {
  display: flex;
  align-items: center;
  padding: 1px 6px;
  gap: 4px;
}

.search-input {
  width: 130px;
  border: none;
  background: none;
  outline: none;
  font-size: 13px;
  color: var(--text-primary);
  cursor: text;
  direction: rtl;
}

.search-input::placeholder { color: var(--text-secondary); }
.search-input::-webkit-search-cancel-button { filter: grayscale(1) opacity(0.4); }

.match-count {
  font-size: 11px;
  color: var(--text-secondary);
  white-space: nowrap;
  flex-shrink: 0;
  min-width: 32px;
  text-align: end;
}

.match-count.no-match { color: #e05252; }

.sep {
  width: 1px;
  height: 16px;
  background: var(--border-color);
  flex-shrink: 0;
  margin-inline: 1px;
}

.nav-btn, .close-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 24px;
  height: 24px;
  flex-shrink: 0;
  border-radius: 4px;
  cursor: pointer;
}

.nav-btn svg, .close-btn svg { width: 16px; height: 16px; }
.nav-btn:disabled { opacity: 0.3; cursor: default; }

.search-bar-enter-active, .search-bar-leave-active {
  transition: opacity 150ms ease, transform 150ms ease;
}
.search-bar-enter-from, .search-bar-leave-to {
  opacity: 0;
  transform: translateY(-6px);
}
</style>
