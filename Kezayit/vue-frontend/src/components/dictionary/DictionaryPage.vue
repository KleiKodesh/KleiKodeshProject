<script setup lang="ts">
import { watch, ref } from 'vue'
import { useDebounce } from '@vueuse/core'
import { IconSearch20Regular } from '@iconify-prerendered/vue-fluent'
import BottomSearchBar from '@/components/common/BottomSearchBar.vue'
import DictionaryWordPage from './DictionaryWordPage.vue'
import { useTabStore } from '@/stores/tabStore'
import { dictLookup, dictNikud, dictSenses, dictRelated, dictSynonyms, dictVariants, dictSpellCandidates } from '@/host/dictionaryDb'
import { isHosted } from '@/host/seforimDb'
import type { DictRow, DictSense, DictRelated } from '@/host/dictionaryDb'

export interface WordPageData {
  headword:    string
  kezayitRows: DictRow[]
  wikiSenses:  DictSense[]
  related:     DictRelated[]
  nikud:       string[]
  synonyms:    string[]
  variants:    string[]
}

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

const tabStore = useTabStore()
const searchQuery = ref('')
const debouncedQuery = useDebounce(searchQuery, 300)

const pageData  = ref<WordPageData | null>(null)
const searching   = ref(false)
const noResults   = ref(false)
const suggestions = ref<string[]>([])

watch(debouncedQuery, async (q) => {
  const trimmed = q.trim()
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
    // Step 1: fetch entries (exact → prefix → contains fallback)
    const { rows, isExact } = await dictLookup(trimmed)

    // Step 2: only fetch grammar/synonyms/nikud for exact matches
    const [wikiSenses, related, nikud, dbSynonyms, thesaurus, variants] = isExact
      ? await Promise.all([
          dictSenses(trimmed),
          dictRelated(trimmed),
          dictNikud(trimmed),
          dictSynonyms(trimmed),
          fetchThesaurus(trimmed),
          dictVariants(trimmed),
        ])
      : [[], [], [], [], [], []]

    // Merge DB synonyms + MS Word thesaurus, deduped
    const seen = new Set(dbSynonyms)
    const synonyms = [...dbSynonyms]
    for (const w of thesaurus) {
      if (!seen.has(w)) { seen.add(w); synonyms.push(w) }
    }

    // Spelling suggestions when not exact
    if (!isExact) {
      const candidates = await dictSpellCandidates(trimmed)
      const maxDist = Math.max(2, Math.floor(trimmed.length / 2))
      suggestions.value = candidates
        .map(hw => ({ hw, d: levenshtein(trimmed, hw) }))
        .filter(x => x.d <= maxDist && x.hw !== trimmed)
        .sort((a, b) => a.d - b.d)
        .slice(0, 8)
        .map(x => x.hw)
    }

    if (!rows.length && !wikiSenses.length && !nikud.length && !synonyms.length) {
      pageData.value = null
      noResults.value = true
      tabStore.updateActiveTab({ title: 'מילון' })
    } else {
      pageData.value = { headword: trimmed, kezayitRows: rows, wikiSenses, related, nikud, synonyms, variants }
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
        v-model="searchQuery"
        class="dict-search-input"
        type="search"
        placeholder="חפש מילה"
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
  direction: rtl;
}
.dict-body {
  flex: 1;
  overflow: hidden;
}
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
  direction: rtl;
  flex-shrink: 0;
}
.dict-suggestions-label {
  font-weight: 600;
}
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
.dict-suggestion-link:hover {
  text-decoration-color: var(--accent-color);
}
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
  direction: rtl;
}
.dict-search-input::placeholder { color: var(--text-secondary); }
.dict-search-input::-webkit-search-cancel-button { filter: grayscale(1) opacity(0.4); }
</style>
