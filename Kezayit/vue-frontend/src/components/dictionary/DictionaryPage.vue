<script setup lang="ts">
import { ref, watch, computed } from 'vue'
import { useDebounce } from '@vueuse/core'
import { IconSearch20Regular } from '@iconify-prerendered/vue-fluent'
import BottomSearchBar from '@/components/common/BottomSearchBar.vue'
import TabStrip from '@/components/common/TabStrip.vue'
import DictionaryListPane from './DictionaryListPane.vue'
import DictionaryDetailsPane from './DictionaryDetailsPane.vue'
import { useWiktionary } from './useWiktionary'
import { useKezayitDictionary } from './useKezayitDictionary'
import { useDictSuggestions } from './useDictSuggestions'
import { useHamichlol } from './useHamichlol'
import { useWordThesaurus } from './useWordThesaurus'
import { useOnlineStatus } from '@/utils/useOnlineStatus'
import { useTabStore } from '@/stores/tabStore'

const tabStore = useTabStore()

const TABS = [
  { key: 'list', label: 'רשימה' },
  { key: 'details', label: 'פרטים' },
  { key: 'sources', label: 'מקורות' },
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
} = useKezayitDictionary()

// ── Suggestions ───────────────────────────────────────────────────────────────

const { suggestions } = useDictSuggestions(
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

// ── המכלול online lookup ──────────────────────────────────────────────────────

const isOnline = useOnlineStatus()
const {
  results: hamichlolSenses,
  loading: hamichlolLoading,
} = useHamichlol(debouncedQuery, isOnline)

// ── Word thesaurus (VSTO only) ────────────────────────────────────────────────

const { groups: wordThesaurusGroups } = useWordThesaurus(debouncedQuery)

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

    <div v-else-if="activeTab === 'sources'" class="sources-pane">
      <div class="sources-placeholder">
        <p class="sources-title">תכונה עתידית</p>
        <p class="sources-body">
          כאן יופיעו מקורות נוספים למילון — כגון ספרי מילונים מהספרייה, מלבי"ם ביאור המילות, ומצודות.
        </p>
        <p class="sources-invite">
          נשמח לשמוע מה היית רוצה שתכונה זו תכלול!
        </p>
      </div>
    </div>

    <DictionaryDetailsPane
      v-else
      :searching="searching"
      :error="error"
      :not-found="notFound"
      :all-senses="allSenses"
      :suggestions="suggestions"
      :query-length="searchQuery.length"
      :hamichlol-senses="hamichlolSenses"
      :hamichlol-loading="hamichlolLoading"
      :word-thesaurus-groups="wordThesaurusGroups"
      @pick="fillFromSuggestion"
      @search-word="handleSearchWord"
    />

    <BottomSearchBar v-if="activeTab !== 'sources'">
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

/* ── Sources placeholder ── */
.sources-pane {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 24px 20px;
  overflow-y: auto;
}
.sources-placeholder {
  display: flex;
  flex-direction: column;
  gap: 10px;
  max-width: 320px;
  text-align: center;
  direction: rtl;
}
.sources-title {
  font-size: 14px;
  font-weight: 600;
  color: var(--text-primary);
}
.sources-body {
  font-size: 13px;
  color: var(--text-secondary);
  line-height: 1.6;
}
.sources-invite {
  font-size: 12px;
  color: var(--accent-color);
  line-height: 1.5;
}
</style>
