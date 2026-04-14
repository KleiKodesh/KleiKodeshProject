import { ref } from 'vue'
import { queryDict } from '@/host/db'
import { SQL } from '@/host/queries.sql'
import type { WiktionarySense, WiktionaryDefinition } from './useWiktionary'

// ── Types ─────────────────────────────────────────────────────────────────────

export interface DictSuggestion {
  headword: string
  definition: string | null
}

// ── Raw DB row types ──────────────────────────────────────────────────────────

interface RawSense {
  id: number
  headword: string
  nikud: string | null
  pos: string | null
  binyan: string | null
  shoresh: string | null
  ktiv_male: string | null
  source_label: string | null
  sense_order: number
}

interface RawDefinition {
  id: number
  sense_id: number
  text: string
  def_order: number
}

// ── Single-word full load — 5 queries total regardless of sense/def count ─────

async function loadFullSenses(word: string): Promise<WiktionarySense[]> {
  const rawSenses = await queryDict<RawSense>(SQL.GET_DICT_SENSES_FOR_WORD, [word])
  if (!rawSenses.length) return []

  const senseIds = rawSenses.map((s) => s.id)

  const rawDefs = await queryDict<RawDefinition>(SQL.GET_DICT_ALL_DEFINITIONS(senseIds), senseIds)

  const defsBySense = new Map<number, RawDefinition[]>()
  for (const d of rawDefs) {
    if (!defsBySense.has(d.sense_id)) defsBySense.set(d.sense_id, [])
    defsBySense.get(d.sense_id)!.push(d)
  }

  return rawSenses.map((rs) => {
    const definitions: WiktionaryDefinition[] = (defsBySense.get(rs.id) ?? []).map((rd) => ({
      text: rd.text,
      layer: null,
      examples: [],
    }))

    return {
      headword: rs.headword,
      nikud: rs.nikud,
      pos: rs.pos ?? (rs.source_label?.startsWith('תורת אמת') ? 'ארמית' : null),
      binyan: rs.binyan,
      shoresh: rs.shoresh,
      ktivMale: rs.ktiv_male,
      etymology: null,
      definitions,
      sections: {},
      translations: [],
      sourceLabel: rs.source_label ?? null,
    } satisfies WiktionarySense
  })
}

// ── Composable ────────────────────────────────────────────────────────────────

export function useKezayitDictionary() {
  const results = ref<WiktionarySense[]>([])
  const searching = ref(false)

  async function search(term: string) {
    const trimmed = term.trim()
    if (!trimmed) {
      results.value = []
      return
    }
    searching.value = true
    try {
      results.value = await loadFullSenses(trimmed)
    } catch {
      results.value = []
    } finally {
      searching.value = false
    }
  }

  async function getSuggestions(term: string): Promise<DictSuggestion[]> {
    const trimmed = term.trim()
    if (!trimmed) return []
    try {
      const rows = await queryDict<{ headword: string; definition: string; source_label: string }>(
        SQL.DICT_SUGGEST,
        [`%${trimmed}%`, `${trimmed}%`],
      )
      return rows.map((r) => ({
        headword: r.headword,
        definition: r.definition ?? null,
      }))
    } catch {
      return []
    }
  }

  return { results, searching, search, getSuggestions }
}
