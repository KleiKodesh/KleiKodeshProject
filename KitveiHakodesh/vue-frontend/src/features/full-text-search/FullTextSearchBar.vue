<script setup lang="ts">
import { ref, watch } from 'vue'
import { useIntervalFn } from '@vueuse/core'
import {
  IconSearch20Regular,
  IconDismiss20Regular,
  IconFilter20Regular,
  IconOptions20Regular,
  IconArrowSort20Regular,
  IconCheckmark20Regular,
} from '@iconify-prerendered/vue-fluent'
import BottomSearchBar from '@/components/BottomSearchBar.vue'
import { useDropdownClose } from '@/composables/useDropdownClose'
import type { FullTextSearchSortOrder } from './fullTextSearchTypes'

const props = defineProps<{
  searchQuery: string
  isSearching: boolean
  resultCount: number
  totalResultCount: number
  filterCount: number
  atFilterCount: number
  isAdvancedOpen: boolean
  isAdvancedActive: boolean
  sortOrder: FullTextSearchSortOrder
  disabled?: boolean
}>()
const emit = defineEmits<{
  search: [string]
  cancel: []
  toggleFilter: []
  toggleAdvanced: []
  clear: []
  'update:searchQuery': [string]
  'update:sortOrder': [FullTextSearchSortOrder]
}>()

const inputRef = ref<HTMLInputElement | null>(null)
const filterBtnRef = ref<HTMLElement | null>(null)
const advancedBtnRef = ref<HTMLElement | null>(null)
const localQuery = ref(props.searchQuery)

// ── Sort dropdown ─────────────────────────────────────────────────────────────
const SORT_OPTIONS: { value: FullTextSearchSortOrder; label: string }[] = [
  { value: 'lineId', label: 'סדר מקורי' },
  { value: 'relevance', label: 'רלוונטיות' },
  { value: 'bookName', label: 'שם הספר' },
  { value: 'chronological', label: 'סדר כרונולוגי' },
]
const isSortDropdownOpen = ref(false)
const sortToggleButtonRef = ref<HTMLElement | null>(null)
const sortControlRef = ref<HTMLElement | null>(null)

const { justClosed } = useDropdownClose(
  sortControlRef,
  () => { isSortDropdownOpen.value = false },
  { toggleButton: sortToggleButtonRef },
)

function toggleSortDropdown() {
  if (justClosed.value) return
  isSortDropdownOpen.value = !isSortDropdownOpen.value
}
function selectSortOrder(value: FullTextSearchSortOrder) {
  emit('update:sortOrder', value)
  isSortDropdownOpen.value = false
}

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
  'כי ביצחק @רשי @רמבן',
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

defineExpose({ focus: () => inputRef.value?.focus(), filterBtnRef, advancedBtnRef })
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
      <button
        ref="advancedBtnRef"
        class="bar-btn"
        :class="{ 'filter-active': isAdvancedOpen || isAdvancedActive }"
        title="אפשרויות מתקדמות"
        @click.stop="$emit('toggleAdvanced')"
      >
        <IconOptions20Regular />
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
      spellcheck="true"
      autocomplete="on"
      @keydown.enter="handleSearch"
      @keydown.esc="handleClear"
    />
    <span v-if="resultCount > 0 || (isSearching && resultCount > 0)" class="result-count-badge">
      {{ resultCount.toLocaleString() }}
      <template v-if="!isSearching && resultCount < totalResultCount">
        / {{ totalResultCount.toLocaleString() }}
      </template>
    </span>
    <template #right>
      <!-- Sort dropdown — shown only after results finish streaming in (hidden while
           searching / when there are no results), so it never reorders a partial set. -->
      <div v-if="!isSearching && resultCount > 0" ref="sortControlRef" class="sort-control">
        <button
          ref="sortToggleButtonRef"
          class="bar-btn"
          :class="{ 'filter-active': sortOrder !== 'lineId' }"
          :title="'מיון: ' + SORT_OPTIONS.find((o) => o.value === sortOrder)!.label"
          @click.stop="toggleSortDropdown"
        >
          <IconArrowSort20Regular />
        </button>
        <div v-if="isSortDropdownOpen" class="sort-dropdown">
          <div
            v-for="option in SORT_OPTIONS"
            :key="option.value"
            role="option"
            class="sort-dropdown__item"
            :class="{ 'is-selected': sortOrder === option.value }"
            @click="selectSortOrder(option.value)"
          >
            <IconCheckmark20Regular
              class="sort-dropdown__checkmark"
              :class="{ 'is-visible': sortOrder === option.value }"
            />
            <span>{{ option.label }}</span>
          </div>
        </div>
      </div>
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
/* Sort control */
.sort-control {
  position: relative;
  display: flex;
  align-items: center;
}
.sort-dropdown {
  position: absolute;
  bottom: calc(100% + 6px);
  right: 0;
  min-width: 140px;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.2);
  overflow: hidden;
  z-index: 100;
}
.sort-dropdown__item {
  display: flex;
  align-items: center;
  gap: 6px;
  height: 26px;
  padding: 0 10px;
  font-size: 12px;
  color: var(--text-primary);
  cursor: pointer;
  white-space: nowrap;
}
.sort-dropdown__item:hover {
  background: color-mix(in srgb, var(--text-primary) 6%, transparent);
}
.sort-dropdown__item:active {
  background: color-mix(in srgb, var(--text-primary) 10%, transparent);
}
.sort-dropdown__item.is-selected {
  color: var(--accent-color);
}
.sort-dropdown__checkmark {
  flex-shrink: 0;
  opacity: 0;
  color: var(--accent-color);
}
.sort-dropdown__checkmark.is-visible {
  opacity: 1;
}
.result-count-badge {
  font-size: 11px;
  color: var(--text-secondary);
  white-space: nowrap;
  flex-shrink: 0;
  padding: 0 4px;
  direction: ltr;
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
