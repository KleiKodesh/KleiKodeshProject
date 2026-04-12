<script setup lang="ts">
import { watch } from 'vue'
import { IconSearch20Regular } from '@iconify-prerendered/vue-fluent'
import BottomSearchBar from '@/components/common/BottomSearchBar.vue'
import { useDictionarySearch } from './useDictionarySearch'
import { useDictionary } from './useDictionary'
import DictionaryBookShelf from './DictionaryBookShelf.vue'
import DictionaryResultsPage from './DictionaryResultsPage.vue'
import { useTabStore } from '@/stores/tabStore'

const { sections, loading: shelfLoading } = useDictionary()
const {
  searchQuery,
  debouncedQuery,
  results,
  filteredResults,
  searching,
  hasSearched,
  activeSource,
  bookCounts,
  expandedEntries,
  lastTerm,
  search,
  toggleEntry,
  toggleSource,
  clearSources,
} = useDictionarySearch()
const tabStore = useTabStore()

watch(debouncedQuery, (q) => search(q))

function searchWord(word: string) {
  searchQuery.value = word
  search(word)
}

function openBookInViewer(bookId: number, title: string) {
  tabStore.updateActiveTab({ route: '/book-view', title, bookId })
}

function openEntryInViewer(entryId: number) {
  const content = expandedEntries.value.get(entryId)
  if (!content || content.bookId === null) return
  tabStore.openTab({
    route: '/book-view',
    title: content.bookTitle,
    bookId: content.bookId,
    openTocLineIndex: content.lineIndex ?? undefined,
  })
}
</script>

<template>
  <div class="dict-page">
    <template v-if="hasSearched || searching">
      <DictionaryResultsPage
        :results="results"
        :filtered-results="filteredResults"
        :searching="searching"
        :has-searched="hasSearched"
        :expanded-entries="expandedEntries"
        :last-term="lastTerm"
        @open-in-viewer="openEntryInViewer"
        @search-word="searchWord"
      />
    </template>

    <div v-else class="dict-scroll">
      <DictionaryBookShelf :sections="sections" :loading="shelfLoading" @open="openBookInViewer" />
    </div>

    <BottomSearchBar>
      <template #left>
        <IconSearch20Regular class="search-icon" />
      </template>
      <input
        v-model="searchQuery"
        class="dict-search-input"
        type="search"
        placeholder="הקלד מילה לחיפוש..."
        dir="rtl"
        autofocus
      />
    </BottomSearchBar>
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

.dict-scroll {
  flex: 1;
  overflow-y: auto;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
}

.search-icon {
  color: var(--text-secondary);
}

.dict-search-input {
  flex: 1;
  background: none;
  border: none;
  outline: none;
  font-size: 13px;
  color: var(--text-primary);
  font-family: inherit;
  direction: rtl;
}

.dict-search-input::placeholder {
  color: var(--text-secondary);
}

.dict-search-input::-webkit-search-cancel-button {
  filter: grayscale(1) opacity(0.4);
}
</style>
