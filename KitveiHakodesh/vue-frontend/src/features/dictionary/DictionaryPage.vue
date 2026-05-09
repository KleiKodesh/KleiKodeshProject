<script setup lang="ts">
import { watch, ref, computed, onMounted, nextTick } from 'vue'
import { useDebounce } from '@vueuse/core'
import { storeToRefs } from 'pinia'
import { IconSearch20Regular } from '@iconify-prerendered/vue-fluent'
import BottomSearchBar from '@/components/BottomSearchBar.vue'
import DictionaryWordPage from './DictionaryWordPage.vue'
import { useTabStore } from '@/stores/tabStore'
import { useSettingsStore } from '@/stores/settingsStore'
import { useZoomHandler } from '@/composables/useZoom'
import { dictionaryCacheGet, dictionaryCacheSet, dictionaryCacheClear } from './dictionaryCache'
import { dictLinks, dictSynonyms, dictVariants, dictSpellCandidates, dictKetivVariants, combinedLookup } from '@/webview-host/dictionaryDb'
import { expandKetivHaser } from '@/utils/hebrewKetivExpander'
import { isHosted } from '@/webview-host/seforimDb'
import type { SenseRow, DictLink, MetzudatRow, MenchemRow, AruchRow } from '@/webview-host/dictionaryDb'
import type { WordPageData } from './dictionaryTypes'

function levenshtein(a: string, b: string): number {
  const m = a.length, n = b.length
  const dp: number[] = Array.from({ length: n + 1 }, (_, i) => i)
  for (let i = 1; i <= m; i++) {
    let prev = dp[0]!; dp[0] = i
    for (let j = 1; j <= n; j++) {
      const tmp = dp[j]!
      dp[j] = a[i-1] === b[j-1] ? prev : 1 + Math.min(prev, dp[j]!, dp[j-1]!)
      prev = tmp
    }
  }
  return dp[n]!
}

async function fetchThesaurus(word: string): Promise<string[]> {
  if (!isHosted || typeof window.__webviewAction !== 'function') return []
  try {
    const res = await window.__webviewAction('getWordSynonyms', { word })
    if (res && Array.isArray((res as any).groups)) return (res as any).groups.flat() as string[]
  } catch { /* not available */ }
  return []
}

const tabStore  = useTabStore()
const settingsStore = useSettingsStore()
const { dictionaryZoom: zoom } = storeToRefs(settingsStore)

const isDictionaryActive = computed(() => tabStore.activeTab?.route === '/dictionary')
useZoomHandler({ zoom, enabled: isDictionaryActive })

const fontPx = computed(() => (zoom.value / 100) * 13)

const searchQuery = ref('')
const searchInputRef = ref<HTMLInputElement | null>(null)

function focusSearchInput() {
  nextTick(() => searchInputRef.value?.focus())
}

onMounted(() => {
  dictionaryCacheClear()
  const saved = tabStore.activeTab?.searchQuery
  if (saved) searchQuery.value = saved
  focusSearchInput()
})

watch(() => tabStore.activeTab?.route, (route) => {
  if (route === '/dictionary') focusSearchInput()
})

const debouncedQuery = useDebounce(searchQuery, 300)

const pageData    = ref<WordPageData | null>(null)
const searching   = ref(false)
const noResults   = ref(false)
const suggestions = ref<string[]>([])

watch(debouncedQuery, async (q) => {
  const trimmed = q.trim()
  tabStore.updateActiveTab({ searchQuery: trimmed || undefined })
  if (!trimmed) {
    pageData.value = null
    noResults.value = false
    tabStore.updateActiveTab({ title: 'מילון' })
    return
  }
  searching.value = true
  noResults.value = false
  suggestions.value = []
  try {
    const cached = await dictionaryCacheGet(trimmed)
    if (cached) {
      pageData.value = cached
      tabStore.updateActiveTab({ title: `מילון · ${trimmed}` })
      return
    }

    const [{ dictRows, metzudatRows, malbimRows, menchemRows, aruchRows, isExact }] = await Promise.all([
      combinedLookup(trimmed),
    ])

    // Split dictionary senses: Radak (source_id=7) groups with commentary sources
    const RADAK_SOURCE_ID = 7
    const dictSenses  = dictRows.filter(r => r.source_id !== RADAK_SOURCE_ID)
    const radakSenses = dictRows.filter(r => r.source_id === RADAK_SOURCE_ID)

    const [links, dbSynonyms, thesaurus, variants] = isExact
      ? await Promise.all([
          dictLinks(trimmed),
          dictSynonyms(trimmed),
          fetchThesaurus(trimmed),
          dictVariants(trimmed),
        ])
      : [[], [], [], []]

    const seen = new Set(dbSynonyms)
    const synonyms = [...dbSynonyms]
    for (const w of thesaurus) {
      if (!seen.has(w)) { seen.add(w); synonyms.push(w) }
    }

    // כתיב חסר and Levenshtein — always computed, shown in קשורים (or in the
    // no-results bar when there are no results at all)
    const ketivExpansions = expandKetivHaser(trimmed)
    const [ketivSuggestions, spellCandidates] = await Promise.all([
      dictKetivVariants(ketivExpansions),
      dictSpellCandidates(trimmed),
    ])
    const maxDist = Math.max(2, Math.floor(trimmed.length / 2))
    const levenshteinSuggestions = spellCandidates
      .map(hw => ({ hw, d: levenshtein(trimmed, hw) }))
      .filter(x => x.d <= maxDist && x.hw !== trimmed)
      .sort((a, b) => a.d - b.d)
      .slice(0, 8)
      .map(x => x.hw)

    if (!isExact) {
      // No-results bar: כתיב חסר first, Levenshtein only as fallback
      if (ketivSuggestions.length > 0) {
        suggestions.value = ketivSuggestions.slice(0, 8)
      } else {
        suggestions.value = levenshteinSuggestions.slice(0, 8)
      }
    }

    if (!dictRows.length && !metzudatRows.length && !malbimRows.length && !menchemRows.length && !aruchRows.length && !synonyms.length) {
      pageData.value = null
      noResults.value = true
      tabStore.updateActiveTab({ title: 'מילון' })
    } else {
      const result: WordPageData = {
        headword:    trimmed,
        senses:      dictSenses,
        radak:       radakSenses,
        metzudat:    metzudatRows,
        malbim:      malbimRows,
        menchemRows,
        aruchRows,
        links, synonyms, variants,
        ketivSuggestions,
        levenshteinSuggestions,
      }
      pageData.value = result
      dictionaryCacheSet(trimmed, result)
      tabStore.updateActiveTab({ title: `מילון · ${trimmed}` })
    }
  } finally {
    searching.value = false
  }
})

function onSelect(headword: string) {
  searchQuery.value = headword
}
</script>

<template>
  <div class="dict-page">
    <div class="dict-body">
      <div v-if="searching" class="dict-state">מחפש...</div>

      <DictionaryWordPage
        v-else-if="pageData"
        :data="pageData"
        :font-px="fontPx"
        @select="onSelect"
      />

      <div v-else class="dict-empty">
        <IconSearch20Regular class="dict-empty-icon" />
      </div>
    </div>

    <div v-if="noResults" class="dict-no-results">
      <template v-if="suggestions.length">
        <span class="dict-suggestions-label">אולי התכוונת ל: </span>
        <span v-for="(w, i) in suggestions" :key="w">
          <button class="dict-suggestion-link" @click="onSelect(w)">{{ w }}</button><span v-if="i < suggestions.length - 1">, </span>
        </span>
      </template>
      <span v-else>לא נמצאו תוצאות</span>
    </div>

    <BottomSearchBar>
      <template #left>
        <IconSearch20Regular class="search-icon" />
      </template>
      <input
        ref="searchInputRef"
        :value="searchQuery"
        class="dict-search-input"
        type="text"
        placeholder="חפש מילה"
        spellcheck="true"
        autocomplete="on"
        @input="searchQuery = ($event.target as HTMLInputElement).value"
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
.dict-body { flex: 1; overflow: hidden; }
.dict-state {
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 40px 20px;
  font-size: 13px;
  color: var(--text-secondary);
}
.dict-no-results {
  padding: 8px 16px;
  font-size: 12px;
  color: var(--text-secondary);
  flex-shrink: 0;
}
.dict-suggestions-label { font-weight: 600; }
.dict-suggestion-link {
  font-size: 12px;
  font-family: inherit;
  color: var(--accent-color);
  background: none;
  border: none;
  padding: 0;
  cursor: pointer;
  text-decoration: underline;
  text-underline-offset: 2px;
  text-decoration-color: color-mix(in srgb, var(--accent-color) 40%, transparent);
}
.dict-suggestion-link:hover { text-decoration-color: var(--accent-color); }
.dict-empty {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 100%;
  color: var(--text-secondary);
  opacity: 0.4;
}
.dict-empty-icon { width: 48px; height: 48px; }
.search-icon { color: var(--text-secondary); }
.dict-search-input {
  flex: 1;
  background: none;
  border: none;
  outline: none;
  font-size: 13px;
  color: var(--text-primary);
  font-family: inherit;
}
.dict-search-input::placeholder { color: var(--text-secondary); }
.dict-search-input::-webkit-search-cancel-button { filter: grayscale(1) opacity(0.4); }
</style>
