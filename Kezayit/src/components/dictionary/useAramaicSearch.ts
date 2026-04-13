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
  etymology: string | null
  source_label: string | null
  sense_order: number
}

interface RawDefinition {
  id: number
  sense_id: number
  text: string
  layer: string | null
  def_order: number
}

interface RawExample {
  definition_id: number
  text: string
  source: string | null
}

interface RawSectionRow {
  sense_id: number
  section_name: string
  item_text: string
  item_order: number
}

interface RawTranslation {
  sense_id: number
  lang: string
  word: string
}

// ── Single-word full load — 5 queries total regardless of sense/def count ─────

async function loadFullSenses(word: string): Promise<WiktionarySense[]> {
  const rawSenses = await queryDict<RawSense>(SQL.GET_DICT_SENSES_FOR_WORD, [word])
  if (!rawSenses.length) return []

  const senseIds = rawSenses.map((s) => s.id)

  const [rawDefs, rawExamples, rawSections, rawTranslations] = await Promise.all([
    queryDict<RawDefinition>(SQL.GET_DICT_ALL_DEFINITIONS(senseIds), senseIds),
    queryDict<RawExample>(SQL.GET_DICT_ALL_EXAMPLES(senseIds), senseIds),
    queryDict<RawSectionRow>(SQL.GET_DICT_ALL_SECTIONS(senseIds), senseIds),
    queryDict<RawTranslation>(SQL.GET_DICT_ALL_TRANSLATIONS(senseIds), senseIds),
  ])

  const defsBySense = new Map<number, RawDefinition[]>()
  for (const d of rawDefs) {
    if (!defsBySense.has(d.sense_id)) defsBySense.set(d.sense_id, [])
    defsBySense.get(d.sense_id)!.push(d)
  }

  const examplesByDef = new Map<number, RawExample[]>()
  for (const e of rawExamples) {
    if (!examplesByDef.has(e.definition_id)) examplesByDef.set(e.definition_id, [])
    examplesByDef.get(e.definition_id)!.push(e)
  }

  const sectionsBySense = new Map<number, RawSectionRow[]>()
  for (const s of rawSections) {
    if (!sectionsBySense.has(s.sense_id)) sectionsBySense.set(s.sense_id, [])
    sectionsBySense.get(s.sense_id)!.push(s)
  }

  const translationsBySense = new Map<number, RawTranslation[]>()
  for (const t of rawTranslations) {
    if (!translationsBySense.has(t.sense_id)) translationsBySense.set(t.sense_id, [])
    translationsBySense.get(t.sense_id)!.push(t)
  }

  return rawSenses.map((rs) => {
    const definitions: WiktionaryDefinition[] = (defsBySense.get(rs.id) ?? []).map((rd) => ({
      text: rd.text,
      layer: rd.layer,
      examples: (examplesByDef.get(rd.id) ?? []).map((e) => ({
        text: e.text,
        source: e.source,
      })),
    }))

    const sections: Record<string, string[]> = {}
    for (const row of sectionsBySense.get(rs.id) ?? []) {
      if (!sections[row.section_name]) sections[row.section_name] = []
      sections[row.section_name]!.push(row.item_text)
    }

    const transMap = new Map<string, string[]>()
    for (const t of translationsBySense.get(rs.id) ?? []) {
      if (!transMap.has(t.lang)) transMap.set(t.lang, [])
      transMap.get(t.lang)!.push(t.word)
    }

    return {
      headword: rs.headword,
      nikud: rs.nikud,
      pos: rs.pos ?? (rs.source_label?.startsWith('תורת אמת') ? 'ארמית' : null),
      binyan: rs.binyan,
      shoresh: rs.shoresh,
      ktivMale: rs.ktiv_male,
      etymology: rs.etymology,
      definitions,
      sections,
      translations: [...transMap.entries()].map(([lang, words]) => ({ lang, words })),
      sourceLabel: rs.source_label ?? null,
    } satisfies WiktionarySense
  })
}

// ── Composable ────────────────────────────────────────────────────────────────

export function useAramaicSearch() {
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
