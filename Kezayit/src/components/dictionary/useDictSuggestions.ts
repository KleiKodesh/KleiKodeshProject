import { ref, computed, watch } from 'vue'
import type { Ref } from 'vue'
import type { DictSuggestion } from './useKezayitDictionary'

/**
 * Manages the merged autosuggest list from both offline sources:
 * wikidictionary.db (Hebrew) and dictionary.db (Aramaic + abbreviations).
 *
 * Both sources return DictSuggestion[] so they merge cleanly.
 */
function stripTags(text: string | null): string | null {
  if (!text) return null
  const cleaned = text
    .replace(/\[\[([^\]|]+\|)?([^\]]+)\]\]/g, '$2') // [[link|text]] -> text
    .replace(/'{2,3}/g, '')                           // wiki bold/italic
    .replace(/\s+/g, ' ')
    .trim()
  return cleaned.length > 120 ? cleaned.slice(0, 120) + '…' : cleaned || null
}

export function useDictSuggestions(
  getWikiSuggestions: (term: string) => Promise<{ headword: string; definition: string | null }[]>,
  getAramaicSuggestions: (term: string) => Promise<DictSuggestion[]>,
  debouncedQuery: Ref<string>,
) {
  const wikiSuggestions = ref<{ headword: string; definition: string | null }[]>([])
  const aramaicSuggestions = ref<DictSuggestion[]>([])

  watch(debouncedQuery, async (q) => {
    if (!q.trim()) {
      wikiSuggestions.value = []
      aramaicSuggestions.value = []
      return
    }
    const [wiki, aramaic] = await Promise.all([getWikiSuggestions(q), getAramaicSuggestions(q)])
    wikiSuggestions.value = wiki.map(s => ({ ...s, definition: stripTags(s.definition) }))
    aramaicSuggestions.value = aramaic.map(s => ({ ...s, definition: stripTags(s.definition) }))
  })

  const suggestions = computed<DictSuggestion[]>(() => {
    const query = debouncedQuery.value.trim()
    const aramaicHeadwords = new Set(aramaicSuggestions.value.map((s) => s.headword))

    // Wiki suggestions not already covered by Aramaic
    const wikiOnly = wikiSuggestions.value
      .filter((s) => !aramaicHeadwords.has(s.headword))
      .map((s) => ({ headword: s.headword, definition: s.definition }))

    const all = [...aramaicSuggestions.value, ...wikiOnly]
    all.sort((a, b) => {
      const aPrefix = a.headword.startsWith(query) ? 0 : 1
      const bPrefix = b.headword.startsWith(query) ? 0 : 1
      if (aPrefix !== bPrefix) return aPrefix - bPrefix
      return a.headword.localeCompare(b.headword, 'he')
    })
    return all.slice(0, 60)
  })

  return { suggestions }
}
