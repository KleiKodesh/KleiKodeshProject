import { ref, watch } from 'vue'
import type { Ref } from 'vue'
import type { WiktionarySense } from './useWiktionary'

const BASE = 'https://he.hamichlol.org.il/w/api.php'

function pageUrl(title: string): string {
  return `https://he.hamichlol.org.il/${encodeURIComponent(title.replace(/ /g, '_'))}`
}

// Parse a disambiguation page extract into one sense per bullet option.
// Each line looks like: "מילה (בלשנות) – יחידה בסיסית של השפה"
// The part before the dash becomes the headword; the part after becomes the definition.
function parseDisambiguation(title: string, extract: string): WiktionarySense[] {
  const lines = extract.split('\n').map((l) => l.trim()).filter(Boolean)
  const senses: WiktionarySense[] = []

  for (const line of lines) {
    // Skip section headers (short lines with no dash separator)
    const dashIdx = line.search(/[–—-]/)
    if (dashIdx === -1) continue

    const rawHeadword = line.slice(0, dashIdx).trim()
    const definition = line.slice(dashIdx + 1).trim()
    if (!rawHeadword || !definition) continue

    senses.push({
      headword: rawHeadword,
      nikud: null,
      pos: null,
      binyan: null,
      shoresh: null,
      ktivMale: null,
      etymology: null,
      definitions: [{ text: definition, examples: [] }],
      sections: {},
      translations: [],
      sourceLabel: 'המכלול',
      readMoreUrl: pageUrl(rawHeadword),
    })
  }

  // Fallback: if nothing parsed, return a single sense with the raw extract
  if (!senses.length) {
    senses.push({
      headword: title,
      nikud: null,
      pos: null,
      binyan: null,
      shoresh: null,
      ktivMale: null,
      etymology: null,
      definitions: [{ text: extract.trim(), examples: [] }],
      sections: {},
      translations: [],
      sourceLabel: 'המכלול',
      readMoreUrl: pageUrl(title),
    })
  }

  return senses
}

async function fetchSenses(word: string): Promise<WiktionarySense[]> {
  // Fetch both the page categories (to detect disambiguation) and the full extract
  const params = new URLSearchParams({
    action: 'query',
    titles: word,
    prop: 'extracts|categories',
    exintro: '0',
    explaintext: '1',
    redirects: '1',
    format: 'json',
    formatversion: '2',
    origin: '*',
  })
  const res = await fetch(`${BASE}?${params}`)
  if (!res.ok) return []
  const data = await res.json()
  const page = data?.query?.pages?.[0]
  if (!page || page.missing || !page.extract?.trim()) return []

  const cats: string[] = (page.categories ?? []).map((c: { title: string }) => c.title as string)
  const isDisambig = cats.some((c) => c.includes('פירושונים') || c.includes('disambiguation'))

  if (isDisambig) {
    return parseDisambiguation(page.title, page.extract)
  }

  // Regular article — return a single sense with the intro (first 4 sentences)
  const sentences = page.extract.split(/(?<=[.!?])\s+/)
  const intro = sentences.slice(0, 4).join(' ').trim()
  return [
    {
      headword: page.title,
      nikud: null,
      pos: null,
      binyan: null,
      shoresh: null,
      ktivMale: null,
      etymology: null,
      definitions: [{ text: intro || page.extract.trim(), examples: [] }],
      sections: {},
      translations: [],
      sourceLabel: 'המכלול',
      readMoreUrl: pageUrl(page.title),
    },
  ]
}

export function useHamichlol(query: Ref<string>, isOnline: Ref<boolean>) {
  const result = ref<WiktionarySense | null>(null)
  const results = ref<WiktionarySense[]>([])
  const loading = ref(false)

  let lastQuery = ''

  watch(
    [query, isOnline],
    async ([q, online]) => {
      const trimmed = q.trim()
      if (!trimmed || !online) {
        result.value = null
        results.value = []
        loading.value = false
        return
      }
      if (trimmed === lastQuery) return
      lastQuery = trimmed
      loading.value = true
      try {
        const senses = await fetchSenses(trimmed)
        results.value = senses
        // Keep result pointing to the first sense for backwards compat
        result.value = senses[0] ?? null
      } catch {
        result.value = null
        results.value = []
      } finally {
        loading.value = false
      }
    },
    { immediate: false },
  )

  return { result, results, loading }
}
