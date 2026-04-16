<script setup lang="ts">
import { ref, watch } from 'vue'
import { useIntervalFn } from '@vueuse/core'
import {
  IconSearch20Regular,
  IconDismiss20Regular,
  IconFilter20Regular,
} from '@iconify-prerendered/vue-fluent'
import BottomSearchBar from '@/components/common/BottomSearchBar.vue'

const props = defineProps<{
  searchQuery: string
  isSearching: boolean
  filterCount: number
  atFilterCount: number
  disabled?: boolean
}>()
const emit = defineEmits<{
  search: [string]
  cancel: []
  toggleFilter: []
  clear: []
  'update:searchQuery': [string]
}>()

const inputRef = ref<HTMLInputElement | null>(null)
const filterBtnRef = ref<HTMLElement | null>(null)
const localQuery = ref(props.searchQuery)

watch(
  () => props.searchQuery,
  (v) => { localQuery.value = v },
)
watch(localQuery, (v) => emit('update:searchQuery', v))

// ── Animated placeholder ──────────────────────────────────────────────────────

const PLACEHOLDERS = [
  'הזן טקסט לחיפוש...',
  'הוסף @ לסינון לפי ספר או קטגוריה',
  'שויתי לנגדי תמיד',
  'כי ביצחק @רשי @ רמבן',
  'קיש קיש קריא @בבלי בבא מציעא',
]
const placeholder = ref(PLACEHOLDERS[0]!)
let phraseIdx = 0, charIdx = 0, pauseTicks = 0

const { pause: pauseTyping, resume: resumeTyping } = useIntervalFn(() => {
  if (pauseTicks > 0) { pauseTicks--; return }
  const target = PLACEHOLDERS[phraseIdx]!
  if (charIdx < target.length) {
    placeholder.value = target.slice(0, ++charIdx)
  } else {
    pauseTicks = 12
    phraseIdx = (phraseIdx + 1) % PLACEHOLDERS.length
    charIdx = 0
  }
}, 80)

watch(localQuery, (v) => (v ? pauseTyping() : resumeTyping()))

// ── Actions ───────────────────────────────────────────────────────────────────

function handleSearch() {
  if (localQuery.value.trim()) emit('search', localQuery.value)
}
function handleClear() {
  localQuery.value = ''
  emit('clear')
  inputRef.value?.focus()
}

defineExpose({ focus: () => inputRef.value?.focus(), filterBtnRef })
</script>

<template>
  <BottomSearchBar>
    <template #left>
      <button
        ref="filterBtnRef"
        class="bar-btn"
        :class="{ 'filter-active': filterCount > 0 || atFilterCount > 0 }"
        :title="filterCount > 0 ? `סינון: ${filterCount} ספרים` : 'סינון תוצאות'"
        @click.stop="$emit('toggleFilter')"
      >
        <IconFilter20Regular />
      </button>
    </template>
    <input
      ref="inputRef"
      v-model="localQuery"
      type="text"
      name="full-text-search"
      class="search-input"
      :placeholder="placeholder"
      :disabled="disabled"
      @keydown.enter="handleSearch"
      @keydown.esc="handleClear"
    />
    <template #right>
      <button
        class="bar-btn"
        :disabled="disabled || (!isSearching && !localQuery.trim())"
        :title="isSearching ? 'ביטול חיפוש' : 'חיפוש'"
        @click="isSearching ? $emit('cancel') : handleSearch()"
      >
        <div v-if="isSearching" class="spinner-wrap">
          <svg class="ring" viewBox="0 0 24 24">
            <circle
              cx="12"
              cy="12"
              r="10"
              fill="none"
              stroke-width="2"
              stroke="var(--border-color)"
            />
            <circle
              cx="12"
              cy="12"
              r="10"
              fill="none"
              stroke-width="2"
              stroke="var(--accent-color)"
              stroke-dasharray="31.4 31.4"
              stroke-linecap="round"
            />
          </svg>
          <IconDismiss20Regular class="cancel-icon" />
        </div>
        <IconSearch20Regular v-else />
      </button>
    </template>
  </BottomSearchBar>
</template>

<style scoped>
.search-input {
  flex: 1;
  background: none;
  border: none;
  outline: none;
  font-size: 13px;
  color: var(--text-primary);
  direction: rtl;
}
.search-input::placeholder {
  color: var(--text-secondary);
}
.bar-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 20px;
  height: 20px;
  border-radius: 4px;
  flex-shrink: 0;
}
.bar-btn:disabled {
  opacity: 0.35;
  cursor: not-allowed;
}
.filter-active {
  color: var(--accent-color);
}
.spinner-wrap {
  position: relative;
  width: 20px;
  height: 20px;
  display: flex;
  align-items: center;
  justify-content: center;
}
.ring {
  width: 20px;
  height: 20px;
  animation: spin 1s linear infinite;
}
.cancel-icon {
  position: absolute;
  width: 12px;
  height: 12px;
  color: var(--text-secondary);
}
@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}
</style>
