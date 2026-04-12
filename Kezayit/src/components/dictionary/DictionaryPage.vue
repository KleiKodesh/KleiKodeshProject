<script setup lang="ts">
import { watch } from 'vue'
import {
  IconBookLetter24Filled,
  IconSearch24Regular,
  IconArrowLeft24Regular,
} from '@iconify-prerendered/vue-fluent'
import { useDictionarySearch } from './useDictionarySearch'
import { useDictionary } from './useDictionary'
import DictionaryBookShelf from './DictionaryBookShelf.vue'
import DictionaryEntryView from './DictionaryEntryView.vue'
import { useTabStore } from '@/stores/tabStore'

const { sections, loading: shelfLoading } = useDictionary()
const {
  searchQuery,
  debouncedQuery,
  results,
  filteredResults,
  selectedEntry,
  searching,
  hasSearched,
  activeBookId,
  bookCounts,
  search,
  selectEntry,
  clearEntry,
} = useDictionarySearch()
const tabStore = useTabStore()

watch(debouncedQuery, (q) => search(q))

const BOOK_LABELS: Record<number, string> = {
  473: 'ערוך',
  471: 'הפלאה שבערכין',
  6105: 'שרשים',
  472: 'לעזי רש"י',
}

function openBookInViewer(bookId: number, title: string) {
  tabStore.updateActiveTab({ route: '/book-view', title, bookId })
}

function openEntryInViewer() {
  if (!selectedEntry) return
  tabStore.updateActiveTab({
    route: '/book-view',
    title: selectedEntry.bookTitle,
    bookId: selectedEntry.bookId,
    openTocLineIndex: selectedEntry.lineIndex,
  })
}
</script>

<template>
  <div class="dict-page">
    <!-- ── Entry detail view (full screen) ── -->
    <template v-if="selectedEntry">
      <div class="dict-header">
        <button class="dict-back-btn" @click="clearEntry()">
          <IconArrowLeft24Regular />
        </button>
        <span class="dict-header-title">{{
          BOOK_LABELS[selectedEntry.bookId] ?? selectedEntry.bookTitle
        }}</span>
      </div>
      <DictionaryEntryView :entry="selectedEntry" @open-in-viewer="openEntryInViewer" />
    </template>

    <!-- ── Search / browse view ── -->
    <template v-else>
      <div class="dict-header">
        <IconBookLetter24Filled class="dict-header-icon" />
        <div class="dict-search-wrap">
          <IconSearch24Regular class="dict-search-icon" />
          <input
            v-model="searchQuery"
            class="dict-search-input"
            type="search"
            placeholder="חפש מילה..."
            dir="rtl"
            autofocus
          />
        </div>
      </div>

      <!-- Filter tabs -->
      <div v-if="results.length > 0" class="dict-filter-bar">
        <button
          class="dict-filter-tab"
          :class="{ active: activeBookId === null }"
          @click="activeBookId = null"
        >
          הכל <span class="dict-filter-count">{{ results.length }}</span>
        </button>
        <button
          v-for="[bookId, info] in bookCounts"
          :key="bookId"
          class="dict-filter-tab"
          :class="{ active: activeBookId === bookId }"
          @click="activeBookId = bookId"
        >
          {{ BOOK_LABELS[bookId] ?? info.title }}
          <span class="dict-filter-count">{{ info.count }}</span>
        </button>
      </div>

      <!-- Results list -->
      <div class="dict-scroll">
        <div v-if="searching" class="dict-state-msg">מחפש...</div>

        <div v-else-if="hasSearched && filteredResults.length === 0" class="dict-state-msg">
          לא נמצאו תוצאות
        </div>

        <template v-else-if="filteredResults.length > 0">
          <button
            v-for="entry in filteredResults"
            :key="`${entry.bookId}-${entry.lineIndex}`"
            class="dict-result-row"
            @click="selectEntry(entry)"
          >
            <span class="dict-result-headword">{{ entry.headword }}</span>
            <span class="dict-result-source">{{
              BOOK_LABELS[entry.bookId] ?? entry.bookTitle
            }}</span>
          </button>
        </template>

        <!-- Empty state: book shelf -->
        <DictionaryBookShelf
          v-else
          :sections="sections"
          :loading="shelfLoading"
          @open="openBookInViewer"
        />
      </div>
    </template>
  </div>
</template>

<style scoped>
.dict-page {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
  background: var(--bg-primary);
}

/* ── Header ── */
.dict-header {
  display: flex;
  align-items: center;
  gap: 10px;
  height: 52px;
  padding: 0 12px;
  background: var(--bg-toolbar);
  border-bottom: 1px solid var(--border-color);
  flex-shrink: 0;
}

.dict-header-icon {
  color: #7b5ea7;
  width: 22px;
  height: 22px;
  flex-shrink: 0;
}

.dict-header-title {
  font-size: 15px;
  font-weight: 600;
  color: var(--text-primary);
}

.dict-back-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 36px;
  height: 36px;
  flex-shrink: 0;
  color: var(--text-primary);
  font-size: 20px;
}

/* Search pill — fills the header */
.dict-search-wrap {
  flex: 1;
  display: flex;
  align-items: center;
  gap: 8px;
  background: var(--input-bg);
  border: 1px solid var(--border-color);
  border-radius: 999px;
  padding: 0 14px;
  height: 36px;
}

.dict-search-icon {
  width: 16px;
  height: 16px;
  color: var(--text-secondary);
  flex-shrink: 0;
}

.dict-search-input {
  flex: 1;
  background: none;
  border: none;
  outline: none;
  font-size: 15px;
  color: var(--text-primary);
  font-family: inherit;
}

.dict-search-input::placeholder {
  color: var(--text-secondary);
}

/* ── Filter bar ── */
.dict-filter-bar {
  display: flex;
  align-items: center;
  gap: 4px;
  padding: 6px 12px;
  background: var(--bg-secondary);
  border-bottom: 1px solid var(--border-color);
  flex-shrink: 0;
  overflow-x: auto;
  scrollbar-width: none;
}

.dict-filter-tab {
  display: flex;
  align-items: center;
  gap: 5px;
  height: 30px;
  padding: 0 12px;
  border-radius: 999px;
  font-size: 13px;
  color: var(--text-secondary);
  white-space: nowrap;
  flex-shrink: 0;
  border: 1px solid transparent;
}

.dict-filter-tab.active {
  background: color-mix(in srgb, var(--accent-color) 15%, transparent);
  border-color: color-mix(in srgb, var(--accent-color) 40%, transparent);
  color: var(--accent-color);
}

.dict-filter-count {
  font-size: 11px;
  opacity: 0.65;
}

/* ── Scrollable content ── */
.dict-scroll {
  flex: 1;
  overflow-y: auto;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
}

.dict-state-msg {
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 48px 16px;
  font-size: 14px;
  color: var(--text-secondary);
}

/* ── Result rows ── */
.dict-result-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  width: 100%;
  min-height: 52px;
  padding: 0 16px;
  gap: 12px;
  background: none;
  border: none;
  border-bottom: 1px solid var(--border-color);
  border-radius: 0;
  cursor: pointer;
  text-align: right;
}

.dict-result-row:hover {
  background: color-mix(in srgb, var(--text-primary) 5%, transparent);
}

.dict-result-row:active {
  background: color-mix(in srgb, var(--text-primary) 9%, transparent);
}

.dict-result-headword {
  font-size: 16px;
  font-weight: 600;
  color: var(--text-primary);
  flex: 1;
  text-align: right;
}

.dict-result-source {
  font-size: 11px;
  color: var(--text-secondary);
  flex-shrink: 0;
  white-space: nowrap;
  background: color-mix(in srgb, var(--text-secondary) 12%, transparent);
  padding: 2px 7px;
  border-radius: 999px;
}
</style>
