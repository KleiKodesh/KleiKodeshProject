import { ref } from 'vue'
import { queryDict } from '@/host/dictionaryDb'
import { SQL } from '@/host/queries.sql'

export interface DictSense {
  headword: string
  definition: string
  sourceLabel: string | null
}

export function useKezayitDictionary() {
  const senses = ref<DictSense[]>([])
  const searching = ref(false)

  async function search(term: string) {
    const trimmed = term.trim()
    if (!trimmed) {
      senses.value = []
      return
    }
    searching.value = true
    try {
      const rows = await queryDict<{ headword: string; definition: string; source_label: string | null }>(
        SQL.SEARCH_DICT_SENSES,
        [trimmed, `${trimmed}%`, trimmed],
      )
      senses.value = rows.map((r) => ({
        headword: r.headword,
        definition: r.definition,
        sourceLabel: r.source_label ?? null,
      }))
    } catch {
      senses.value = []
    } finally {
      searching.value = false
    }
  }

  return { senses, searching, search }
}
