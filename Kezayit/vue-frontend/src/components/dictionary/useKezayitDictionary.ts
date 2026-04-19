import { ref } from 'vue'
import { dictLookup, dictFuzzyCandidates } from '@/host/dictionaryDb'
import type { DictRow } from '@/host/dictionaryDb'
import { isHosted } from '@/host/seforimDb'

export interface DictSenseDisplay {
  headword:   string   // plain or vocalized (nikud ?? headword)
  definition: string
  sourceLabel: string | null
  isFuzzy?: boolean
}

// ── Levenshtein ───────────────────────────────────────────────────────────────

function levenshtein(a: string, b: string): number {
  const m = a.length
  const n = b.length
  const dp: number[] = Array.from({ length: n + 1 }, (_, i) => i)
  for (let i = 1; i <= m; i++) {
    let prev = dp[0]!
    dp[0] = i
    for (let j = 1; j <= n; j++) {
      const tmp = dp[j]!
      dp[j] = a[i - 1] === b[j - 1] ? prev : 1 + Math.min(prev, dp[j]!, dp[j - 1]!)
      prev = tmp
    }
  }
  return dp[n]!
}

// ── Word thesaurus (VSTO only) ────────────────────────────────────────────────

async function fetchThesaurus(word: string): Promise<string[][]> {
  if (!isHosted || typeof window.__webviewAction !== 'function') return []
  try {
    const res = await window.__webviewAction('getWordSynonyms', { word })
    if (res && Array.isArray((res as any).groups)) return (res as any).groups as string[][]
  } catch {
    // not available — degrade silently
  }
  return []
}

function thesaurusToSenses(groups: string[][]): DictSenseDisplay[] {
  return groups.flatMap((group) =>
    group.map((word) => ({
      headword: word,
      definition: '',
      sourceLabel: 'מילים נרדפות',
    })),
  )
}

function rowToSense(r: DictRow, isFuzzy = false): DictSenseDisplay {
  return {
    headword:    r.nikud ?? r.headword,
    definition:  r.definition,
    sourceLabel: r.source ?? null,
    isFuzzy,
  }
}

// ── Sort ──────────────────────────────────────────────────────────────────────

const ABBREVIATION_RE = /["\u05F4\u05F3]/  // ASCII " or ״ or ׳

// Source priority for plain search (case A)
const SOURCE_PRIORITY_PLAIN: Record<string, number> = {
  'המכלול':                          0,
  'מילון ארמי עברי':                 1,
  'ויקיפדיה':                        2,
  'ויקי ספרי יהדות - ראשי תיבות':   3,
  'קיצור ראשי תיבות':               4,
  'מילים נרדפות':                    5,
}

// Source priority for abbreviation search (case B)
const SOURCE_PRIORITY_ABBREV: Record<string, number> = {
  'קיצור ראשי תיבות':               0,
  'ויקיפדיה':                        1,
  'ויקי ספרי יהדות - ראשי תיבות':   2,
  'המכלול':                          3,
  'מילון ארמי עברי':                 4,
  'מילים נרדפות':                    5,
}

function sourcePriority(source: string | null, isAbbrev: boolean): number {
  const map = isAbbrev ? SOURCE_PRIORITY_ABBREV : SOURCE_PRIORITY_PLAIN
  return map[source ?? ''] ?? 4
}

function sortSenses(senses: DictSenseDisplay[], term: string): DictSenseDisplay[] {
  const isAbbrev = ABBREVIATION_RE.test(term)
  return [...senses].sort((a, b) => {
    // 1. Exact headword match first
    const aExact = a.headword === term ? 0 : 1
    const bExact = b.headword === term ? 0 : 1
    if (aExact !== bExact) return aExact - bExact
    // 2. Alphabetical by headword
    const hwCmp = a.headword.localeCompare(b.headword, 'he')
    if (hwCmp !== 0) return hwCmp
    // 3. Source priority within same headword
    return sourcePriority(a.sourceLabel, isAbbrev) - sourcePriority(b.sourceLabel, isAbbrev)
  })
}

// ── Composable ────────────────────────────────────────────────────────────────

export function useKezayitDictionary() {
  const senses = ref<DictSenseDisplay[]>([])
  const searching = ref(false)

  async function search(term: string) {
    const trimmed = term.trim()
    if (!trimmed) {
      senses.value = []
      return
    }
    searching.value = true
    try {
      const [rows, thesaurusGroups] = await Promise.all([
        dictLookup(trimmed),
        fetchThesaurus(trimmed),
      ])

      const thesaurusSenses = thesaurusToSenses(thesaurusGroups)

      if (rows.length > 0) {
        senses.value = sortSenses(
          [...rows.map((r) => rowToSense(r)), ...thesaurusSenses],
          trimmed
        )
        return
      }

      if (thesaurusSenses.length > 0) {
        senses.value = thesaurusSenses
        return
      }

      // Fuzzy Levenshtein fallback
      const fragment = trimmed.length >= 2 ? trimmed.slice(0, 2) : trimmed
      const candidates = await dictFuzzyCandidates(`%${fragment}%`)

      if (!candidates.length) {
        senses.value = thesaurusSenses
        return
      }

      const scored = candidates
        .map((hw) => ({ hw, dist: levenshtein(trimmed, hw) }))
        .sort((a, b) => a.dist - b.dist)
        .slice(0, 10)

      const maxDist = Math.max(2, Math.floor(trimmed.length / 2))
      const close = scored.filter((s) => s.dist <= maxDist)

      if (!close.length) {
        senses.value = thesaurusSenses
        return
      }

      const fuzzyRows = (
        await Promise.all(close.map(({ hw }) => dictLookup(hw)))
      ).flat().map((row) => rowToSense(row, true))

      senses.value = sortSenses([...fuzzyRows, ...thesaurusSenses], trimmed)
    } catch {
      senses.value = []
    } finally {
      searching.value = false
    }
  }

  return { senses, searching, search }
}
