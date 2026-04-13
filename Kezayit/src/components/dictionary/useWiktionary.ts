import { ref } from 'vue'
import { useDebounce } from '@vueuse/core'

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
  /** Etymology/expansion note — extracted at import time from (=...) prefix, e.g. 'על לב' for אליבא */
  etymology?: string | null
  definitions: WiktionaryDefinition[]
  sections: Record<string, string[]>
  translations: { lang: string; words: string[] }[]
  /** Source label — set for DB entries (e.g. 'מילון ארמי א'), null for live Wiktionary */
  sourceLabel?: string | null
}

// ── Helpers ───────────────────────────────────────────────────────────────────

function stripNikud(s: string): string {
  return s.replace(/[\u05B0-\u05C7\u05F0-\u05F4\uFB1D-\uFB4E]/g, '').trim()
}

function containsHebrew(s: string): boolean {
  return /[\u05D0-\u05EA]/.test(s)
}

function cleanWiki(s: string): string {
  return s
    .replace(/\{\{[^{}]*\}\}/g, '')
    .replace(/\{\{[^{}]*\}\}/g, '')
    .replace(/\[\[([^\]|]+\|)?([^\]]+)\]\]/g, '$2')
    .replace(/'{2,3}/g, '')
    .replace(/<ref[^>]*>[\s\S]*?<\/ref>/g, '')
    .replace(/<[^>]+>/g, '')
    .replace(/\s+/g, ' ')
    .trim()
}

function extractFromTemplate(block: string, key: string): string | null {
  const m = block.match(new RegExp(`\\|${key}\\s*=\\s*([^\\n|]+)`))
  return m ? (m[1] ?? '').trim() : null
}

function extractShoresh(block: string): string | null {
  const m3 = block.match(/\{\{שרש3\|([^|]+)\|([^|]+)\|([^|]+)/)
  if (m3) return `${m3[1] ?? ''}-${m3[2] ?? ''}-${m3[3] ?? ''}`
  const m1 = block.match(/\{\{שרש\|([^|}\s]+)/)
  if (m1) return m1[1] ?? null
  return null
}

const KNOWN_SECTIONS = new Set([
  'גיזרון',
  'נגזרות',
  'מילים נרדפות',
  'ניגודים',
  'צירופים',
  'מידע נוסף',
  'ראו גם',
  'הערות שוליים',
])

const KEEP_LANGS = new Set(['אנגלית', 'ערבית', 'ארמית'])

// Layers that are inappropriate for an Orthodox Jewish audience
const BLOCKED_LAYERS = new Set([
  'גס',
  'גסות',
  'גסה', // vulgar
  'סלנג',
  'סלנג ישראלי', // slang
  'מדובר',
  'דיבורי', // colloquial/spoken
  'ארגו',
  "ז'רגון", // jargon/argot
  'פוגעני',
  'גנאי', // offensive/derogatory
])

// ── Parser ────────────────────────────────────────────────────────────────────

export function parseWikitext(title: string, wikitext: string): WiktionarySense[] {
  if (!wikitext || /^#הפניה|^#REDIRECT/i.test(wikitext.trim())) return []

  const lines = wikitext.split('\n')
  const senses: WiktionarySense[] = []
  let cur: WiktionarySense | null = null
  let curSection: string | null = null
  let curDefIdx = -1

  function flush() {
    if (cur && (cur.definitions.length > 0 || Object.keys(cur.sections).length > 0)) {
      senses.push(cur)
    }
    cur = null
    curSection = null
    curDefIdx = -1
  }

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i] ?? ''

    const senseMatch = line.match(/^==([^=][^=]*)==\s*$/)
    if (senseMatch) {
      flush()
      const rawHeader = (senseMatch[1] ?? '').replace(/\{\{[^}]*\}\}/g, '').trim()
      const nikud = /[\u05B0-\u05C7]/.test(rawHeader) ? rawHeader : null
      const headword = stripNikud(rawHeader) || stripNikud(title)
      cur = {
        nikud,
        headword,
        pos: null,
        binyan: null,
        shoresh: null,
        ktivMale: null,
        definitions: [],
        sections: {},
        translations: [],
      }
      continue
    }

    if (!cur) continue

    if (line.includes('{{ניתוח דקדוקי')) {
      let block = line
      let j = i + 1
      let depth = (line.match(/\{\{/g) ?? []).length - (line.match(/\}\}/g) ?? []).length
      while (j < lines.length && depth > 0) {
        const nl = lines[j] ?? ''
        block += '\n' + nl
        depth += (nl.match(/\{\{/g) ?? []).length
        depth -= (nl.match(/\}\}/g) ?? []).length
        j++
      }
      i = j - 1
      cur.shoresh = cur.shoresh || extractShoresh(block)
      cur.binyan = cur.binyan || extractFromTemplate(block, 'בניין')
      cur.pos = cur.pos || extractFromTemplate(block, 'חלק דיבר')
      cur.ktivMale = cur.ktivMale || extractFromTemplate(block, 'כתיב מלא')
      continue
    }

    const secMatch = line.match(/^===([^=]+)===\s*$/)
    if (secMatch) {
      curSection = (secMatch[1] ?? '').trim()
      curDefIdx = -1
      if (KNOWN_SECTIONS.has(curSection) && !cur.sections[curSection]) {
        cur.sections[curSection] = []
      }
      continue
    }

    if (/^====/.test(line)) {
      curSection = null
      continue
    }

    if (!curSection && /^#{1,2}[^:#*]/.test(line)) {
      const layerMatch = line.match(/\{\{(?:מקרא|רובד|משלב)\|([^|}]+)/)
      const layer = layerMatch ? (layerMatch[1] ?? '').trim() : null
      // Skip definitions with blocked layer tags
      if (layer && BLOCKED_LAYERS.has(layer)) continue
      const text = cleanWiki(line.replace(/^#+\s*/, ''))
      if (text && text.length > 1) {
        cur.definitions.push({ text, layer, examples: [] })
        curDefIdx = cur.definitions.length - 1
      }
      continue
    }

    if (!curSection && /^#[:#*]/.test(line) && curDefIdx >= 0) {
      const citMatch = line.match(/\{\{צט[^|]*\|([^|]+)\|([^|]+)\|([^|]+)\|([^|}]+)/)
      if (citMatch) {
        const src = `${citMatch[2] ?? ''} ${citMatch[3] ?? ''}, ${citMatch[4] ?? ''}`
        const def = cur.definitions[curDefIdx]
        if (def) def.examples.push({ text: cleanWiki(citMatch[1] ?? ''), source: src })
      }
      continue
    }

    if (curSection && KNOWN_SECTIONS.has(curSection)) {
      if (curSection === 'תרגום') {
        const langMatch = line.match(/^\*\s*([^:：]+)[：:]\s*(.+)/)
        if (langMatch) {
          const lang = (langMatch[1] ?? '').trim()
          if (KEEP_LANGS.has(lang)) {
            const words = [...(langMatch[2] ?? '').matchAll(/\{\{ת\|[^|]+\|([^|}]+)/g)].map((m) =>
              (m[1] ?? '').trim(),
            )
            if (words.length) cur.translations.push({ lang, words })
          }
        }
        continue
      }

      if (/^\*+/.test(line)) {
        const text = cleanWiki(line.replace(/^\*+\s*/, ''))
        if (text && containsHebrew(text) && text.length < 80) {
          cur.sections[curSection]?.push(text)
        }
        continue
      }

      if (line.trim() && !/^[={<[]/.test(line)) {
        const text = cleanWiki(line)
        if (text && text.length > 4) cur.sections[curSection]?.push(text)
      }
    }
  }

  flush()
  return senses
}

// ── API ───────────────────────────────────────────────────────────────────────

// ── API ───────────────────────────────────────────────────────────────────────

const API = 'https://he.wiktionary.org/w/api.php'

async function fetchWikitext(word: string): Promise<{ title: string; wikitext: string } | null> {
  const url = `${API}?action=query&titles=${encodeURIComponent(word)}&prop=revisions&rvprop=content&rvslots=main&format=json&origin=*`
  const res = await fetch(url)
  if (!res.ok) throw new Error(`HTTP ${res.status}`)
  const data = await res.json()
  const pages = data?.query?.pages ?? {}
  const page = Object.values(pages)[0] as any
  if (!page || page.missing) return null
  const wikitext: string =
    page.revisions?.[0]?.slots?.main?.['*'] ?? page.revisions?.[0]?.['*'] ?? ''
  return { title: page.title as string, wikitext }
}

async function fetchSuggestions(term: string): Promise<string[]> {
  const url = `${API}?action=opensearch&search=${encodeURIComponent(term)}&limit=100&namespace=0&format=json&origin=*`
  const res = await fetch(url)
  if (!res.ok) return []
  const data = await res.json()
  // opensearch returns [query, [titles], [descriptions], [urls]]
  return (data[1] as string[]) ?? []
}

// ── Composable ────────────────────────────────────────────────────────────────

export function useWiktionary() {
  const searchQuery = ref('')
  const debouncedQuery = useDebounce(searchQuery, 350)
  const senses = ref<WiktionarySense[]>([])
  const title = ref<string | null>(null)
  const suggestions = ref<string[]>([])
  const searching = ref(false)
  const hasSearched = ref(false)
  const notFound = ref(false)
  const error = ref<string | null>(null)

  async function loadSuggestions(term: string) {
    if (!term.trim()) {
      suggestions.value = []
      return
    }
    try {
      suggestions.value = await fetchSuggestions(term.trim())
    } catch {
      suggestions.value = []
    }
  }

  function clearSuggestions() {
    suggestions.value = []
  }

  async function search(term: string) {
    const trimmed = term.trim()
    if (!trimmed) {
      senses.value = []
      title.value = null
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
    title.value = null
    try {
      const fetched = await fetchWikitext(trimmed)
      if (!fetched) {
        notFound.value = true
        return
      }
      const parsed = parseWikitext(fetched.title, fetched.wikitext)
      if (parsed.length === 0) {
        notFound.value = true
        return
      }
      title.value = fetched.title
      senses.value = parsed
    } catch {
      error.value = 'שגיאה בטעינת הנתונים'
    } finally {
      searching.value = false
    }
  }

  function searchWord(word: string) {
    searchQuery.value = word
    clearSuggestions()
    search(word)
  }

  return {
    searchQuery,
    debouncedQuery,
    senses,
    title,
    suggestions,
    searching,
    hasSearched,
    notFound,
    error,
    search,
    searchWord,
    loadSuggestions,
    clearSuggestions,
  }
}
