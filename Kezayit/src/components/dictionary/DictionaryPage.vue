<script setup lang="ts">
import { ref, watch } from 'vue'
import { IconSearch20Regular } from '@iconify-prerendered/vue-fluent'
import BottomSearchBar from '@/components/common/BottomSearchBar.vue'
import TabStrip from '@/components/common/TabStrip.vue'
import DictionaryListPane from './DictionaryListPane.vue'
import DictionaryDetailsPane from './DictionaryDetailsPane.vue'
import { useWiktionary } from './useWiktionary'
import { useAramaicSearch } from './useAramaicSearch'
import { useDictSuggestions } from './useDictSuggestions'
import { useTabStore } from '@/stores/tabStore'
import { computed } from 'vue'

const tabStore = useTabStore()

const TABS = [
  { key: 'list', label: 'רשימה' },
  { key: 'details', label: 'פרטים' },
]

const activeTab = ref('list')

// ── Search sources ────────────────────────────────────────────────────────────

const {
  searchQuery,
  debouncedQuery,
  senses: wikiSenses,
  title: wikiTitle,
  suggestions: wikiSuggestions,
  searching: wikiSearching,
  hasSearched,
  error,
  search: wikiSearch,
  searchWord: wikiSearchWord,
  loadSuggestions: loadWikiSuggestions,
  clearSuggestions: clearWikiSuggestions,
} = useWiktionary()

const {
  results: aramaicSenses,
  searching: aramaicSearching,
  search: aramaicSearch,
  getSuggestions: getAramaicSuggestions,
} = useAramaicSearch()

// ── Suggestions composable ────────────────────────────────────────────────────

const { suggestions, clearSuggestions: clearAramaicSuggestions } = useDictSuggestions(
  wikiSuggestions,
  getAramaicSuggestions,
  debouncedQuery,
)

// ── Merged results ────────────────────────────────────────────────────────────

const searching = computed(() => wikiSearching.value || aramaicSearching.value)
const allSenses = computed(() => [...wikiSenses.value, ...aramaicSenses.value])
const notFound = computed(
  () => hasSearched.value && !searching.value && allSenses.value.length === 0 && !error.value,
)

watch(debouncedQuery, (q) => {
  wikiSearch(q)
  aramaicSearch(q)
  loadWikiSuggestions(q)
})

watch(wikiTitle, (t) => {
  tabStore.updateActiveTab({ title: t ? `מילון · ${t}` : 'מילון' })
})

// ── Actions ───────────────────────────────────────────────────────────────────

const listPaneRef = ref<InstanceType<typeof DictionaryListPane> | null>(null)
const inputEl = ref<HTMLInputElement | null>(null)

function fillFromSuggestion(word: string) {
  searchQuery.value = word
  clearWikiSuggestions()
  clearAramaicSuggestions()
  activeTab.value = 'details'
  inputEl.value?.focus()
}

function handleSearchWord(word: string) {
  wikiSearchWord(word)
  aramaicSearch(word)
  activeTab.value = 'details'
}

function onInputKeydown(e: KeyboardEvent) {
  if (e.code === 'Escape') {
    clearWikiSuggestions()
    clearAramaicSuggestions()
    return
  }
  if (!suggestions.value.length) return
  if (e.code === 'ArrowDown' || e.code === 'ArrowUp' || e.code === 'Tab') {
    e.preventDefault()
    listPaneRef.value?.focus()
  }
}
</script>

<template>
  <div class="dict-page">
    <TabStrip v-model="activeTab" :tabs="TABS" />

    <DictionaryListPane
      v-if="activeTab === 'list'"
      ref="listPaneRef"
      :suggestions="suggestions"
      @pick="fillFromSuggestion"
    />

    <DictionaryDetailsPane
      v-else
      :searching="searching"
      :error="error"
      :not-found="notFound"
      :all-senses="allSenses"
      :wiki-senses-count="wikiSenses.length"
      :aramaic-senses-count="aramaicSenses.length"
      :suggestions="suggestions"
      :query-length="searchQuery.length"
      :has-wiki-attribution="wikiSenses.length > 0"
      @pick="fillFromSuggestion"
      @search-word="handleSearchWord"
    />

    <BottomSearchBar>
      <template #left>
        <IconSearch20Regular class="search-icon" />
      </template>
      <input
        ref="inputEl"
        v-model="searchQuery"
        class="dict-search-input"
        type="search"
        placeholder="חפש מילה"
        dir="rtl"
        autofocus
        @keydown="onInputKeydown"
        @keydown.enter="activeTab = 'details'"
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
  direction: rtl;
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
