import { ref } from 'vue'
import { queryWikiDict } from '@/host/dictionaryDb'
import { SQL } from '@/host/queries.sql'

// ── Types ─────────────────────────────────────────────────────────────────────

export interface WiktionaryDefinition {
  text: string
  examples: { text: string; source: string | null }[]
}

export interface WiktionarySense {
  headword: string
  nikud: string | null
  pos: string | null
  binyan: string | null
  shoresh: string | null
  ktivMale: string | null
  etymology?: string | null
  definitions: WiktionaryDefinition[]
  sections: Record<string, string[]>
  translations?: { lang: string; text: string }[]
  sourceLabel?: string | null
  readMoreUrl?: string | null
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

// ── Offline DB lookup ─────────────────────────────────────────────────────────

async function loadFullSenses(word: string): Promise<WiktionarySense[]> {
  const rawSenses = await queryWikiDict<RawSense>(SQL.GET_WIKIDICT_SENSES_FOR_WORD, [word, word])
  if (!rawSenses.length) return []

  const senseIds = rawSenses.map((s) => s.id)

  const [rawDefs, rawExamples, rawSections] = await Promise.all([
    queryWikiDict<RawDefinition>(SQL.GET_WIKIDICT_ALL_DEFINITIONS(senseIds), senseIds),
    queryWikiDict<RawExample>(SQL.GET_WIKIDICT_ALL_EXAMPLES(senseIds), senseIds),
    queryWikiDict<RawSectionRow>(SQL.GET_WIKIDICT_ALL_SECTIONS(senseIds), senseIds),
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

  return rawSenses
    .map((rs) => {
      const definitions: WiktionaryDefinition[] = (defsBySense.get(rs.id) ?? []).map((rd) => ({
        text: rd.text,
        examples: (examplesByDef.get(rd.id) ?? []).map((e) => ({
          text: e.text,
          source: e.source,
        })),
      }))

      if (!definitions.length) return null

      const sections: Record<string, string[]> = {}
      for (const row of sectionsBySense.get(rs.id) ?? []) {
        if (!sections[row.section_name]) sections[row.section_name] = []
        sections[row.section_name]!.push(row.item_text)
      }

      return {
        headword: rs.headword,
        nikud: rs.nikud,
        pos: rs.pos,
        binyan: rs.binyan,
        shoresh: rs.shoresh,
        ktivMale: rs.ktiv_male,
        definitions,
        sections,
        sourceLabel: rs.source_label ?? 'ויקימילון',
      } satisfies WiktionarySense
    })
    .filter((s): s is NonNullable<typeof s> => s !== null)
}

// ── Composable ────────────────────────────────────────────────────────────────

export function useWiktionary() {
  const senses = ref<WiktionarySense[]>([])
  const searching = ref(false)
  const hasSearched = ref(false)
  const notFound = ref(false)
  const error = ref<string | null>(null)

  async function search(term: string) {
    const trimmed = term.trim()
    if (!trimmed) {
      senses.value = []
      hasSearched.value = false
      notFound.value = false
      error.value = null
      return
    }
    searching.value = true
    hasSearched.value = true
    notFound.value = false
    error.value = null
    senses.value = []
    try {
      const results = await loadFullSenses(trimmed)
      if (!results.length) {
        notFound.value = true
        return
      }
      senses.value = results
    } catch {
      error.value = 'שגיאה בטעינת הנתונים'
    } finally {
      searching.value = false
    }
  }

  async function getSuggestions(term: string): Promise<{ headword: string; definition: string | null }[]> {
    const trimmed = term.trim()
    if (!trimmed) return []
    try {
      const rows = await queryWikiDict<{ headword: string; definition: string | null }>(
        SQL.WIKIDICT_SUGGEST,
        [`%${trimmed}%`, `%${trimmed}%`, `${trimmed}%`, `${trimmed}%`],
      )
      return rows.map((r) => ({ headword: r.headword, definition: r.definition ?? null }))
    } catch {
      return []
    }
  }

  return { senses, searching, hasSearched, notFound, error, search, getSuggestions }
}
