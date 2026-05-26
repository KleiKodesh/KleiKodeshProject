<script setup lang="ts">
import { ref, watch, computed, nextTick } from 'vue'
import {
  IconLayoutRowTwoFocusTop20Filled,
  IconLayoutRowTwoFocusBottom20Filled,
  IconChevronUp20Regular,
  IconChevronDown20Regular,
  IconDismiss20Regular,
} from '@iconify-prerendered/vue-fluent'
import type { SearchMode } from './bookViewTypes'

const props = defineProps<{
  visible: boolean
  toolbarVisible: boolean
  matchCount: number
  currentMatch: number
  commentaryVisible: boolean
  mode: SearchMode
  query?: string
}>()
const emit = defineEmits<{
  close: []
  queryChange: [string]
  next: []
  prev: []
  modeChange: [SearchMode]
}>()

const inputRef = ref<HTMLInputElement | null>(null)
const inputValue = ref(props.query ?? '')
const searchMode = ref<SearchMode>(props.mode)

watch(() => props.query, (q) => {
  const nextValue = q ?? ''
  if (nextValue !== inputValue.value) inputValue.value = nextValue
})
watch(() => props.mode, (m) => { if (searchMode.value !== m) searchMode.value = m })
watch(inputValue, (v) => emit('queryChange', v))
watch(searchMode, (m) => { emit('modeChange', m); nextTick(() => inputRef.value?.focus()) })
watch(() => props.visible, (v) => { if (v) nextTick(() => inputRef.value?.focus()) })
watch(() => props.commentaryVisible, (v) => {
  if (!v && searchMode.value === 'commentary') searchMode.value = 'content'
})

const APP_TITLE_BAR = 40
const BOOK_TOOLBAR = 32

const panelStyle = computed(() => ({
  top: `${APP_TITLE_BAR + (props.toolbarVisible ? BOOK_TOOLBAR : 0) + 4}px`,
}))

const placeholder = computed(() =>
  searchMode.value === 'content' ? 'חיפוש בטקסט...' : 'חיפוש במפרשים...',
)

const matchLabel = computed(() => {
  if (!inputValue.value) return ''
  if (props.matchCount === 0) return 'לא נמצא'
  if (props.matchCount > 0) return `${props.currentMatch + 1} / ${props.matchCount}`
  return ''
})

function onClose() {
  inputValue.value = ''
  emit('close')
}

function onInput(event: Event) {
  inputValue.value = (event.target as HTMLInputElement).value
}

function onKeydown(event: KeyboardEvent) {
  if (event.key === 'Enter') event.shiftKey ? emit('prev') : emit('next')
  else if (event.key === 'Escape') onClose()
}

function toggleSearchMode() {
  searchMode.value = searchMode.value === 'content' ? 'commentary' : 'content'
}

defineExpose({ focus: () => inputRef.value?.focus() })
</script>

<template>
  <Transition name="search-bar">
    <div v-if="visible" class="search-bar" :style="panelStyle">
      <div class="search-inner">
        <input
          ref="inputRef"
          :value="inputValue"
          type="search"
          class="search-input"
          :placeholder="placeholder"
          spellcheck="true"
          autocomplete="on"
          @input="onInput"
          @keydown="onKeydown"
        />
        <span class="match-count" :class="{ 'no-match': props.matchCount === 0 }">{{ matchLabel }}</span>
      </div>

      <button
        v-if="props.commentaryVisible"
        class="mode-btn"
        :class="{ active: searchMode === 'commentary' }"
        :title="searchMode === 'content' ? 'עבור לחיפוש במפרשים' : 'עבור לחיפוש בטקסט'"
        @click="toggleSearchMode"
      >
        <IconLayoutRowTwoFocusBottom20Filled v-if="searchMode === 'commentary'" />
        <IconLayoutRowTwoFocusTop20Filled v-else />
      </button>
      <span v-if="props.commentaryVisible" class="sep" />

      <button class="nav-btn" :disabled="props.matchCount === 0" @click="emit('prev')">
        <IconChevronUp20Regular />
      </button>
      <button class="nav-btn" :disabled="props.matchCount === 0" @click="emit('next')">
        <IconChevronDown20Regular />
      </button>

      <span class="sep" />
      <button class="close-btn" @click="onClose"><IconDismiss20Regular /></button>

      <slot name="panel" />
    </div>
  </Transition>
</template>

<style scoped>
.search-bar {
  position: fixed;
  z-index: 9999;
  left: 0;
  right: 0;
  margin: 0 auto;
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

.mode-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 24px;
  height: 24px;
  border-radius: 4px;
  flex-shrink: 0;
  color: var(--text-secondary);
}
.mode-btn svg { width: 16px; height: 16px; }
.mode-btn.active { color: var(--accent-color); }
</style>
