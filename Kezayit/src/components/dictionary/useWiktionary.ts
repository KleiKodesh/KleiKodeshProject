import { ref } from 'vue'
import { queryWikiDict } from '@/host/db'
import { SQL } from '@/host/queries.sql'

// ── Types ─────────────────────────────────────────────────────────────────────

export interface WiktionaryDefinition {
  text: string
  layer: string | null
  examples: { text: string; source: string | null }[]
}

export interface WiktionarySense {
  nikud: string | null
  headword: string
  pos: string | null
  binyan: string | null
  shoresh: string | null
  ktivMale: string | null
  etymology?: string | null
  definitions: WiktionaryDefinition[]
  sections: Record<string, string[]>
  translations: { lang: string; words: string[] }[]
  sourceLabel?: string | null
}

// ── Blocked layer tags (same set as before — now filtered at query time) ───────

export const BLOCKED_LAYERS = new Set([
  'גס',
  'גסות',
  'גסה',
  'סלנג',
  'סלנג ישראלי',
  'מדובר',
  'דיבורי',
  'ארגו',
  "ז'רגון",
  'פוגעני',
  'גנאי',
])

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
  filter_tag: string | null
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

// ── Offline DB lookup — same 5-query pattern as useAramaicSearch ──────────────

async function loadFullSenses(word: string): Promise<WiktionarySense[]> {
  const rawSenses = await queryWikiDict<RawSense>(SQL.GET_WIKIDICT_SENSES_FOR_WORD, [word])
  if (!rawSenses.length) return []

  const senseIds = rawSenses.map((s) => s.id)

  const [rawDefs, rawExamples, rawSections, rawTranslations] = await Promise.all([
    queryWikiDict<RawDefinition>(SQL.GET_WIKIDICT_ALL_DEFINITIONS(senseIds), senseIds),
    queryWikiDict<RawExample>(SQL.GET_WIKIDICT_ALL_EXAMPLES(senseIds), senseIds),
    queryWikiDict<RawSectionRow>(SQL.GET_WIKIDICT_ALL_SECTIONS(senseIds), senseIds),
    queryWikiDict<RawTranslation>(SQL.GET_WIKIDICT_ALL_TRANSLATIONS(senseIds), senseIds),
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

  return rawSenses
    .map((rs) => {
      // Filter blocked layer tags
      const definitions: WiktionaryDefinition[] = (defsBySense.get(rs.id) ?? [])
        .filter((rd) => !rd.filter_tag || !BLOCKED_LAYERS.has(rd.filter_tag))
        .map((rd) => ({
          text: rd.text,
          layer: rd.filter_tag,
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

      const transMap = new Map<string, string[]>()
      for (const t of translationsBySense.get(rs.id) ?? []) {
        if (!transMap.has(t.lang)) transMap.set(t.lang, [])
        transMap.get(t.lang)!.push(t.word)
      }

      return {
        headword: rs.headword,
        nikud: rs.nikud,
        pos: rs.pos,
        binyan: rs.binyan,
        shoresh: rs.shoresh,
        ktivMale: rs.ktiv_male,
        etymology: rs.etymology ?? null,
        definitions,
        sections,
        translations: [...transMap.entries()].map(([lang, words]) => ({ lang, words })),
        sourceLabel: rs.source_label ?? 'ויקימילון',
      } as WiktionarySense
    })
    .filter((s): s is WiktionarySense => s !== null)
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

  async function getSuggestions(term: string): Promise<string[]> {
    const trimmed = term.trim()
    if (!trimmed) return []
    try {
      const rows = await queryWikiDict<{ headword: string }>(SQL.WIKIDICT_SUGGEST, [
        `%${trimmed}%`,
        `${trimmed}%`,
      ])
      return rows.map((r) => r.headword)
    } catch {
      return []
    }
  }

  return {
    senses,
    searching,
    hasSearched,
    notFound,
    error,
    search,
    getSuggestions,
  }
}
