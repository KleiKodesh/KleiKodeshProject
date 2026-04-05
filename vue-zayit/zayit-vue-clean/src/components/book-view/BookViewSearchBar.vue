<script setup lang="ts">
import { ref, watch, computed, nextTick } from 'vue'
import {
  IconChevronUp20Regular,
  IconChevronDown20Regular,
  IconDismiss20Regular,
  IconLayoutRowTwoFocusTop20Filled,
  IconLayoutRowTwoFocusBottom20Filled,
} from '@iconify-prerendered/vue-fluent'

export type SearchMode = 'content' | 'commentary'

const props = defineProps<{
  visible: boolean
  matchCount: number
  currentMatch: number
  commentaryVisible: boolean
  mode: SearchMode
}>()
const emit = defineEmits<{
  close: []
  queryChange: [string]
  next: []
  prev: []
  modeChange: [SearchMode]
}>()

const inputRef = ref<HTMLInputElement | null>(null)
const inputValue = ref('')
const searchMode = ref<SearchMode>(props.mode)

watch(
  () => props.mode,
  (m) => {
    if (searchMode.value !== m) searchMode.value = m
  },
)

watch(inputValue, (v) => emit('queryChange', v))
watch(searchMode, (m) => {
  emit('modeChange', m)
  nextTick(() => inputRef.value?.focus())
})
watch(
  () => props.visible,
  (v) => {
    if (v) nextTick(() => inputRef.value?.focus())
  },
)
watch(
  () => props.commentaryVisible,
  (v) => {
    if (!v && searchMode.value === 'commentary') searchMode.value = 'content'
  },
)

const placeholder = computed(() =>
  searchMode.value === 'content' ? 'חיפוש בטקסט...' : 'חיפוש במפרשים...',
)
const matchLabel = computed(() =>
  props.matchCount === 0 ? '0 / 0' : `${props.currentMatch + 1} / ${props.matchCount}`,
)

function onClose() {
  inputValue.value = ''
  emit('close')
}

defineExpose({ focus: () => inputRef.value?.focus() })
</script>

<template>
  <Transition name="search-bar">
    <div v-if="visible" class="search-bar">
      <div class="search-inner">
        <input
          ref="inputRef"
          v-model="inputValue"
          type="search"
          name="book-search"
          class="search-input"
          :placeholder="placeholder"
          @keydown.enter.exact="emit('next')"
          @keydown.shift.enter="emit('prev')"
          @keydown.esc="onClose"
        />
        <span class="match-count" :class="{ 'no-match': inputValue && matchCount === 0 }">{{
          matchLabel
        }}</span>
      </div>
      <button
        v-if="commentaryVisible"
        class="mode-btn"
        :class="{ active: searchMode === 'commentary' }"
        :title="searchMode === 'content' ? 'עבור לחיפוש במפרשים' : 'עבור לחיפוש בטקסט'"
        @click="searchMode = searchMode === 'content' ? 'commentary' : 'content'"
      >
        <IconLayoutRowTwoFocusBottom20Filled
          v-if="searchMode === 'commentary'"
        /><IconLayoutRowTwoFocusTop20Filled v-else />
      </button>
      <span v-if="commentaryVisible" class="sep" />
      <button class="nav-btn" :disabled="matchCount === 0" @click="emit('prev')">
        <IconChevronUp20Regular />
      </button>
      <button class="nav-btn" :disabled="matchCount === 0" @click="emit('next')">
        <IconChevronDown20Regular />
      </button>
      <span class="sep" />
      <button class="close-btn" @click="onClose"><IconDismiss20Regular /></button>
    </div>
  </Transition>
</template>

<style scoped>
.search-bar {
  display: flex;
  align-items: center;
  gap: 2px;
  padding: 4px 8px;
  background: var(--bg-toolbar);
  border-top: 1px solid var(--border-color);
  box-sizing: border-box;
  user-select: none;
  flex-shrink: 0;
}
.mode-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  border-radius: 4px;
  flex-shrink: 0;
  color: var(--text-secondary);
}
.mode-btn svg {
  width: 16px;
  height: 16px;
}
.mode-btn.active {
  color: var(--accent-color);
}
.search-inner {
  display: flex;
  align-items: center;
  flex: 1;
  padding: 3px 8px;
  gap: 6px;
  background: var(--input-bg);
  border: 1px solid var(--border-color);
  border-radius: 999px;
}
.search-input {
  flex: 1;
  min-width: 0;
  border: none;
  background: none;
  outline: none;
  font-size: 13px;
  color: var(--text-primary);
  cursor: text;
}
.search-input::placeholder {
  color: var(--text-secondary);
}
.search-input::-webkit-search-cancel-button {
  filter: grayscale(1) opacity(0.4);
}
.match-count {
  font-size: 11px;
  color: var(--text-secondary);
  white-space: nowrap;
  flex-shrink: 0;
  min-width: 32px;
  text-align: end;
}
.match-count.no-match {
  color: #e05252;
}
.sep {
  width: 1px;
  height: 16px;
  background: var(--border-color);
  flex-shrink: 0;
  margin-inline: 1px;
}
.nav-btn,
.close-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  flex-shrink: 0;
  border-radius: 4px;
  cursor: pointer;
}
.nav-btn svg,
.close-btn svg {
  width: 16px;
  height: 16px;
}
.nav-btn:disabled {
  opacity: 0.3;
  cursor: default;
}
.search-bar-enter-active,
.search-bar-leave-active {
  transition:
    opacity 150ms ease,
    transform 150ms ease;
}
.search-bar-enter-from,
.search-bar-leave-to {
  opacity: 0;
  transform: translateY(100%);
}
</style>
