<script setup lang="ts">
import { t } from '@/i18n'
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
import TopSearchBar from '@/components/TopSearchBar.vue'
import RecentSearchesDropdown from '@/components/common/RecentSearchesDropdown.vue'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { useRecentSearches } from '@/composables/useRecentSearches'
import type { FullTextSearchSortOrder } from './fullTextSearchTypes'

const props = defineProps<{
  searchQuery: string
  isSearching: boolean
  resultCount: number
  totalResultCount: number
  filterCount: number
  atFilterCount: number
  isOptionsOpen: boolean
  isOptionsActive: boolean
  /** False while the options show as the empty page's card: a dropdown of them is redundant. */
  showOptionsToggle?: boolean
  sortOrder: FullTextSearchSortOrder
  disabled?: boolean
  /**
   * The results list's combobox keydown handler, forwarded by the page (the list is
   * this bar's sibling, so it cannot be reached from here). Returns true when it
   * consumed the key — see onInputKeydown for where in the order it runs.
   */
  resultsKeydown?: (event: KeyboardEvent) => boolean
}>()
const emit = defineEmits<{
  search: [string]
  cancel: []
  toggleFilter: []
  toggleOptions: []
  clear: []
  'update:searchQuery': [string]
  'update:sortOrder': [FullTextSearchSortOrder]
}>()

const filterBtnRef = ref<HTMLElement | null>(null)
const optionsBtnRef = ref<HTMLElement | null>(null)

// The app's own recent-searches list (works in WebView2, unlike the browser's native
// form autofill). recents.query IS the input's model. Committing a suggestion
// (click / Enter) runs the search immediately.
const recents = useRecentSearches({ key: 'fts-search', onSelect: () => handleSearch() })
const localQuery = recents.query
localQuery.value = props.searchQuery

// ── Sort dropdown ─────────────────────────────────────────────────────────────
const SORT_OPTIONS: { value: FullTextSearchSortOrder; label: string }[] = [
  { value: 'catalogOrder', label: t('fullTextSearch.bar.sortCatalogOrder') },
  { value: 'relevance', label: t('fullTextSearch.bar.sortRelevance') },
  { value: 'bookName', label: t('fullTextSearch.bar.sortBookName') },
  { value: 'authorName', label: t('fullTextSearch.bar.sortAuthorName') },
  { value: 'chronological', label: t('fullTextSearch.bar.sortChronological') },
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
  t('fullTextSearch.bar.placeholderEnter'),
  t('fullTextSearch.bar.placeholderFilterHint'),
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
    recents.record() // commit the query to recent-searches history
    emit('search', localQuery.value)
  }
}
function handleClear() {
  localQuery.value = ''
  emit('clear')
  recents.inputEl.value?.focus()
}

// The native search field's clear button empties the text and fires `search`; clear the
// results with it, as Esc does. (Enter fires `search` too; keydown already handled it.)
function onNativeSearch() {
  if (!localQuery.value) handleClear()
}

// Three handlers want the arrows and Enter, in this order:
//
//   1. the recent-searches dropdown, while it is OPEN — the list the user can SEE
//      under the field wins its own keys; it preventDefaults what it consumed.
//      A plain Enter closes it and falls through, so running a search hands the
//      arrows straight to the results.
//   2. the results list (combobox model, useInputListNavigation): the arrows move
//      its highlight without moving the caret, and Enter WITH a highlight opens
//      that result. Left/Right are never taken — they are the caret's.
//   3. this field's own Enter (run the search) and Esc (clear), for keys nobody
//      above claimed.
//
// "While it is open" is the whole reason this is not one straight call chain.
// CLOSED, the dropdown still claims ArrowDown — to open itself, which is right for a
// bar with nothing under it. On a page of results it is not: the first ArrowDown
// after a search would raise a list of past queries instead of stepping into the
// results just found, and the Enter after it would re-run one of those queries
// rather than open a result. The suggestions are a PREFIX match, so the field
// holding the query that was searched usually hides them — the exact match is
// excluded — and the dropdown stays silent; with history that shares the prefix it
// does not, which is how this reached a user with a real search history and not the
// browser tests. So closed, the results get first refusal and the dropdown keeps
// whatever they leave.
function onInputKeydown(e: KeyboardEvent) {
  // Read before anything runs: the dropdown's own handler closes it as it goes, and
  // the order must be decided by the state the key ARRIVED in.
  const dropdownWasOpen = recents.open.value
  if (dropdownWasOpen) {
    recents.onKeydown(e)
    if (e.defaultPrevented) return
  }
  if (props.resultsKeydown?.(e)) return
  if (!dropdownWasOpen) {
    recents.onKeydown(e)
    if (e.defaultPrevented) return
  }
  if (e.key === 'Enter') handleSearch()
  else if (e.key === 'Escape') handleClear()
}

defineExpose({ focus: recents.focus, filterBtnRef, optionsBtnRef })
</script>

<template>
  <TopSearchBar>
    <template #left>
      <button
        ref="filterBtnRef"
        class="bar-btn"
        :class="{ 'filter-active': filterCount > 0 || atFilterCount > 0 }"
        :title="filterCount > 0 ? $t('fullTextSearch.bar.filterCount', { n: filterCount }) : $t('fullTextSearch.bar.filterResults')"
        @click.stop="$emit('toggleFilter')"
      >
        <IconFilter20Regular />
      </button>
      <button
        v-if="showOptionsToggle !== false"
        ref="optionsBtnRef"
        class="bar-btn"
        :class="{ 'filter-active': isOptionsOpen || isOptionsActive }"
        :title="$t('fullTextSearch.bar.searchOptions')"
        @click.stop="$emit('toggleOptions')"
      >
        <IconOptions20Regular />
      </button>
    </template>
    <div class="query-field">
      <input
        :ref="recents.setInput"
        v-model="localQuery"
        type="search"
        class="search-input"
        :placeholder="placeholder"
        :disabled="disabled"
        spellcheck="true"
        autocomplete="off"
        @focus="recents.onFocus"
        @input="recents.onInput"
        @blur="recents.onBlur"
        @keydown="onInputKeydown"
        @search="onNativeSearch"
      />
    </div>

    <RecentSearchesDropdown :controller="recents" />
    <template #right>
      <!-- Sort dropdown — shown only after results finish streaming in (hidden while
           searching / when there are no results), so it never reorders a partial set. -->
      <div v-if="!isSearching && resultCount > 0" ref="sortControlRef" class="sort-control">
        <button
          ref="sortToggleButtonRef"
          class="bar-btn"
          :class="{ 'filter-active': sortOrder !== 'catalogOrder' }"
          :title="$t('fullTextSearch.sortTitle', { label: SORT_OPTIONS.find((o) => o.value === sortOrder)!.label })"
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
        type="button"
        :disabled="disabled || (!isSearching && !localQuery.trim())"
        :title="isSearching ? $t('fullTextSearch.bar.cancelSearch') : $t('fullTextSearch.bar.search')"
        @click="isSearching ? $emit('cancel') : handleSearch()"
      >
        <IconDismiss20Regular v-if="isSearching" />
        <IconSearch20Regular v-else />
      </button>
    </template>
  </TopSearchBar>
</template>

<style scoped>
.query-field {
  position: relative;
  display: flex;
  flex: 1;
  /* Text inputs have an intrinsic minimum width (default size attribute), so in
     a tight bar the input refuses to shrink and pushes the buttons and the
     count badge out of the pill. Let it shrink instead. */
  min-width: 40px;
  /* TopSearchBar's type size reaches only a directly slotted input. */
  font-size: 13px;
}
.search-input {
  flex: 1;
  min-width: 0;
  padding: 0;
  font-size: inherit;
}
/* Box, hover, press, disabled and the .active state come from the global .bar-btn
   rule (main.css). Only the size differs: these sit inside the search pill, which
   is shorter than a title-bar row, so they cannot take the title-bar button size. */
.bar-btn {
  width: 20px;
  height: 20px;
  padding: 0;
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
  top: calc(100% + 6px);
  /* The sort toggle sits at the far-left corner of the (RTL) bar, so anchoring the
     dropdown's right edge would push it off the left of the screen. Anchor its left
     edge to the toggle's left edge instead, so it opens rightward into the viewport. */
  inset-inline-end: 0;
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
</style>
