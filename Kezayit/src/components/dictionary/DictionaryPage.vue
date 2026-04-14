<script setup lang="ts">
import { ref, watch, computed } from 'vue'
import { useDebounce } from '@vueuse/core'
import { IconSearch20Regular } from '@iconify-prerendered/vue-fluent'
import BottomSearchBar from '@/components/common/BottomSearchBar.vue'
import TabStrip from '@/components/common/TabStrip.vue'
import DictionaryListPane from './DictionaryListPane.vue'
import DictionaryDetailsPane from './DictionaryDetailsPane.vue'
import { useWiktionary } from './useWiktionary'
import { useAramaicSearch } from './useAramaicSearch'
import { useDictSuggestions } from './useDictSuggestions'
import { useTabStore } from '@/stores/tabStore'

const tabStore = useTabStore()

const TABS = [
  { key: 'list', label: 'רשימה' },
  { key: 'details', label: 'פרטים' },
]

const activeTab = ref('list')
const searchQuery = ref('')
const debouncedQuery = useDebounce(searchQuery, 350)

// ── Search sources (both offline) ─────────────────────────────────────────────

const {
  senses: wikiSenses,
  searching: wikiSearching,
  hasSearched,
  error,
  search: wikiSearch,
  getSuggestions: getWikiSuggestions,
} = useWiktionary()

const {
  results: aramaicSenses,
  searching: aramaicSearching,
  search: aramaicSearch,
  getSuggestions: getAramaicSuggestions,
} = useAramaicSearch()

// ── Suggestions ───────────────────────────────────────────────────────────────

const { suggestions, clearSuggestions } = useDictSuggestions(
  getWikiSuggestions,
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
})

watch(allSenses, (s) => {
  const first = s[0]
  if (first) tabStore.updateActiveTab({ title: `מילון · ${first.headword}` })
  else tabStore.updateActiveTab({ title: 'מילון' })
})

// ── Actions ───────────────────────────────────────────────────────────────────

const listPaneRef = ref<InstanceType<typeof DictionaryListPane> | null>(null)
const inputEl = ref<HTMLInputElement | null>(null)

function fillFromSuggestion(word: string) {
  searchQuery.value = word
  clearSuggestions()
  activeTab.value = 'details'
  inputEl.value?.focus()
}

function handleSearchWord(word: string) {
  searchQuery.value = word
  wikiSearch(word)
  aramaicSearch(word)
  activeTab.value = 'details'
}

function onInputKeydown(e: KeyboardEvent) {
  if (e.code === 'Escape') {
    clearSuggestions()
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
