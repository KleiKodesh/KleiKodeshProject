<script setup lang="ts">
import { ref, watch, onMounted, computed } from 'vue'
import { useIntervalFn } from '@vueuse/core'
import {
  IconSearch20Regular,
  IconDismiss20Regular,
  IconFilter20Regular,
  IconOptions20Regular,
} from '@iconify-prerendered/vue-fluent'
import BottomSearchBar from '@/components/BottomSearchBar.vue'
import { useSearchCacheStore } from '@/stores/searchCacheStore'

const props = defineProps<{
  searchQuery: string
  isSearching: boolean
  filterCount: number
  atFilterCount: number
  isAdvancedOpen: boolean
  isAdvancedActive: boolean
  resultCount?: number
  totalResultCount?: number
  hasSearched?: boolean
}>()
const emit = defineEmits<{
  search: [string]
  cancel: []
  toggleFilter: []
  toggleAdvanced: []
  clear: []
  'update:searchQuery': [string]
}>()

const inputRef = ref<HTMLInputElement | null>(null)
const filterBtnRef = ref<HTMLElement | null>(null)
const advancedBtnRef = ref<HTMLElement | null>(null)
const localQuery = ref(props.searchQuery)
const recentQueries = ref<string[]>([])

const cacheStore = useSearchCacheStore()

onMounted(async () => {
  recentQueries.value = await cacheStore.getRecentQueries(10)
})

// Hide the datalist only when the search term part of the input exactly matches
// one of the recent queries — at that point the dropdown is superfluous.
// The @filter tokens are stripped before comparing so "@אברבנאל" doesn't prevent
// the match from being detected.
const datalistId = computed(() => {
  const term = localQuery.value.split('@')[0]!.trim().toLowerCase()
  if (!term) return 'search-history'
  const exactMatch = recentQueries.value.some((r) => r.toLowerCase() === term)
  return exactMatch ? undefined : 'search-history'
})

watch(
  () => props.searchQuery,
  (v) => { localQuery.value = v },
)
watch(localQuery, (v) => {
  emit('update:searchQuery', v)
  if (!v && recentQueries.value.length) {
    try { inputRef.value?.showPicker() } catch { /* unsupported — silently ignore */ }
  }
})

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
  if (localQuery.value.trim()) {
    emit('search', localQuery.value)
    // Refresh history so the query just submitted appears next time
    cacheStore.getRecentQueries(10).then((q) => { recentQueries.value = q })
  }
}
function handleClear() {
  localQuery.value = ''
  emit('clear')
  inputRef.value?.focus()
}
function handleFocus() {
  if (!localQuery.value && recentQueries.value.length) {
    try { inputRef.value?.showPicker() } catch { /* unsupported — silently ignore */ }
  }
}

function focusAndShowHistory() {
  inputRef.value?.focus()
  if (!localQuery.value && recentQueries.value.length) {
    // showPicker() requires a user gesture and will throw when called synchronously
    // from a programmatic .focus(). A short setTimeout gives the browser time to
    // settle focus and treats the call as part of the page-load trusted context.
    setTimeout(() => {
      try { inputRef.value?.showPicker() } catch { /* unsupported — silently ignore */ }
    }, 50)
  }
}

defineExpose({ focus: () => inputRef.value?.focus(), focusAndShowHistory, filterBtnRef, advancedBtnRef })
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
      :list="datalistId"
      class="search-input"
      :placeholder="placeholder"
      spellcheck="true"
      autocomplete="on"
      @focus="handleFocus"
      @keydown.enter="handleSearch"
      @keydown.esc="handleClear"
      @change="handleSearch"
    />
    <datalist id="search-history">
      <option v-for="q in recentQueries" :key="q" :value="q" />
    </datalist>
    <span v-if="hasSearched && resultCount != null" class="result-count" :class="{ 'is-searching': isSearching }">
      <template v-if="isSearching">{{ resultCount.toLocaleString('he-IL') }}</template>
      <template v-else-if="resultCount < (totalResultCount ?? resultCount)">{{ resultCount.toLocaleString('he-IL') }}/{{ (totalResultCount ?? resultCount).toLocaleString('he-IL') }}</template>
      <template v-else>{{ resultCount.toLocaleString('he-IL') }}</template>
    </span>
    <template #right>
      <button
        class="bar-btn"
        :disabled="!isSearching && !localQuery.trim()"
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
.result-count {
  font-size: 11px;
  color: var(--text-secondary);
  white-space: nowrap;
  flex-shrink: 0;
  padding-inline-end: 4px;
  opacity: 0.6;
}
.result-count.is-searching {
  opacity: 0.6;
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

<style>
/* Must be unscoped — scoped styles cannot reach browser-internal pseudo-elements */
/* !important required — Chrome 91+ re-introduced the arrow and ignores the rule without it */
input[name="full-text-search"]::-webkit-calendar-picker-indicator {
  display: none !important;
}
</style>
