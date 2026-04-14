import { ref, computed, watch } from 'vue'
import type { Ref } from 'vue'
import type { DictSuggestion } from './useAramaicSearch'

/**
 * Manages the merged autosuggest list from both offline sources:
 * wikidictionary.db (Hebrew) and dictionary.db (Aramaic + abbreviations).
 *
 * Both sources return DictSuggestion[] so they merge cleanly.
 */
export function useDictSuggestions(
  getWikiSuggestions: (term: string) => Promise<string[]>,
  getAramaicSuggestions: (term: string) => Promise<DictSuggestion[]>,
  debouncedQuery: Ref<string>,
) {
  const wikiSuggestions = ref<string[]>([])
  const aramaicSuggestions = ref<DictSuggestion[]>([])

  watch(debouncedQuery, async (q) => {
    if (!q.trim()) {
      wikiSuggestions.value = []
      aramaicSuggestions.value = []
      return
    }
    const [wiki, aramaic] = await Promise.all([getWikiSuggestions(q), getAramaicSuggestions(q)])
    wikiSuggestions.value = wiki
    aramaicSuggestions.value = aramaic
  })

  const hebrewOnly = /[\u05D0-\u05EA]/

  const suggestions = computed<DictSuggestion[]>(() => {
    const query = debouncedQuery.value.trim()
    const aramaicHeadwords = new Set(aramaicSuggestions.value.map((s) => s.headword))

    // Wiki headwords not already covered by Aramaic
    const wikiOnly = wikiSuggestions.value
      .filter((hw) => hebrewOnly.test(hw) && !aramaicHeadwords.has(hw))
      .map((hw) => ({ headword: hw, definition: null }))

    const aramaicItems = aramaicSuggestions.value.filter((s) => hebrewOnly.test(s.headword))

    const all = [...aramaicItems, ...wikiOnly]
    all.sort((a, b) => {
      const aPrefix = a.headword.startsWith(query) ? 0 : 1
      const bPrefix = b.headword.startsWith(query) ? 0 : 1
      if (aPrefix !== bPrefix) return aPrefix - bPrefix
      const alpha = a.headword.localeCompare(b.headword, 'he')
      if (alpha !== 0) return alpha
      // Entries with definitions (Aramaic) after bare headwords (wiki)
      return (a.definition ? 1 : 0) - (b.definition ? 1 : 0)
    })
    return all.slice(0, 60)
  })

  function clearSuggestions() {
    wikiSuggestions.value = []
    aramaicSuggestions.value = []
  }

  return { suggestions, clearSuggestions }
}
