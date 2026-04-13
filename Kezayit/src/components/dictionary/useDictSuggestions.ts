import { ref, computed, watch } from 'vue'
import type { Ref } from 'vue'
import type { DictSuggestion } from './useAramaicSearch'

/**
 * Manages the merged autosuggest list from both Wiktionary (OpenSearch)
 * and the local Aramaic DB.
 *
 * Receives the wiki suggestions ref and the aramaic getter from the parent
 * so it shares the same composable instances — no duplicate fetches.
 */
export function useDictSuggestions(
  wikiSuggestions: Ref<string[]>,
  getAramaicSuggestions: (term: string) => Promise<DictSuggestion[]>,
  debouncedQuery: Ref<string>,
) {
  const aramaicSuggestions = ref<DictSuggestion[]>([])

  watch(debouncedQuery, async (q) => {
    aramaicSuggestions.value = await getAramaicSuggestions(q)
  })

  const hebrewOnly = /[\u05D0-\u05EA]/

  const suggestions = computed<DictSuggestion[]>(() => {
    // Aramaic entries always have definitions — prefer them over bare Wiktionary headwords
    const aramaicHeadwords = new Set(aramaicSuggestions.value.map((s) => s.headword))

    // Wiki-only: headwords not in Aramaic DB, sorted prefix-first then alpha
    const query = debouncedQuery.value.trim()
    const wikiOnly = wikiSuggestions.value
      .filter((hw) => hebrewOnly.test(hw) && !aramaicHeadwords.has(hw))
      .sort((a, b) => {
        const aPrefix = a.startsWith(query) ? 0 : 1
        const bPrefix = b.startsWith(query) ? 0 : 1
        if (aPrefix !== bPrefix) return aPrefix - bPrefix
        return a.localeCompare(b, 'he')
      })
      .map((hw) => ({ headword: hw, definition: null }))

    const aramaicItems = aramaicSuggestions.value.filter((s) => hebrewOnly.test(s.headword))

    // Aramaic first (already ordered by SQL: prefix then alpha), then wiki-only
    return [...aramaicItems, ...wikiOnly].slice(0, 60)
  })

  function clearSuggestions() {
    aramaicSuggestions.value = []
  }

  return { suggestions, clearSuggestions }
}
