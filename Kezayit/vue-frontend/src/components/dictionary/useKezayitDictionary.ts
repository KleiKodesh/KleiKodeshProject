import { ref } from 'vue'
import { queryDict } from '@/host/dictionaryDb'
import { SQL } from '@/host/queries.sql'
import { isHosted } from '@/host/seforimDb'

export interface DictSense {
  headword: string
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

// ── Composable ────────────────────────────────────────────────────────────────

type RawRow = { headword: string; nikud: string | null; definition: string; source_label: string | null }

export function useKezayitDictionary() {
  const senses = ref<DictSense[]>([])
  const searching = ref(false)
  const thesaurusGroups = ref<string[][]>([])

  async function search(term: string) {
    const trimmed = term.trim()
    if (!trimmed) {
      senses.value = []
      thesaurusGroups.value = []
      return
    }
    searching.value = true
    try {
      // Run DB search and thesaurus lookup in parallel
      const [rows, thesaurus] = await Promise.all([
        queryDict<RawRow>(SQL.SEARCH_DICT_SENSES, [trimmed, `${trimmed}%`, trimmed]),
        fetchThesaurus(trimmed),
      ])

      thesaurusGroups.value = thesaurus

      if (rows.length > 0) {
        senses.value = rows.map((r) => ({
          headword: r.headword,
          definition: r.definition,
          sourceLabel: r.source_label ?? null,
        }))
        return
      }

      // Fallback: fuzzy via Levenshtein on contains-match candidates
      const fragment = trimmed.length >= 2 ? trimmed.slice(0, 2) : trimmed
      const candidates = await queryDict<{ headword: string }>(
        SQL.DICT_FUZZY_CANDIDATES,
        [`%${fragment}%`],
      )

      if (!candidates.length) {
        senses.value = []
        return
      }

      const scored = candidates
        .map((c) => ({ headword: c.headword, dist: levenshtein(trimmed, c.headword) }))
        .sort((a, b) => a.dist - b.dist)
        .slice(0, 10)

      const maxDist = Math.max(2, Math.floor(trimmed.length / 2))
      const close = scored.filter((s) => s.dist <= maxDist)
      if (!close.length) {
        senses.value = []
        return
      }

      const headwords = close.map((s) => s.headword)
      const allFuzzy: RawRow[] = []
      for (const hw of headwords) {
        const r = await queryDict<RawRow>(SQL.SEARCH_DICT_SENSES, [hw, `${hw}%`, hw])
        allFuzzy.push(...r)
      }

      senses.value = allFuzzy.map((r) => ({
        headword: r.headword,
        definition: r.definition,
        sourceLabel: r.source_label ?? null,
        isFuzzy: true,
      }))
    } catch {
      senses.value = []
      thesaurusGroups.value = []
    } finally {
      searching.value = false
    }
  }

  return { senses, searching, thesaurusGroups, search }
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