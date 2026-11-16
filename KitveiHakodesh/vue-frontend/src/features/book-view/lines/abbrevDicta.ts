/**
 * Dicta ranking for the abbreviation tooltip.
 *
 * The dictionary answer is shown at once; this asks Dicta's abbreviation expander in
 * parallel, sending the line around the selection so its ranking considers context.
 * When it answers, the tooltip's senses are re-ordered once by Dicta's ranking
 * (mergeDictaRanking). Silent on any failure, short timeout, memory-only cache.
 */

const ENDPOINTS = [
  'https://nakdan-u2-1a.loadbalancer.dicta.org.il/api',
  'https://nakdan-u1-0.loadbalancer.dicta.org.il/api',
  'https://nakdan-5-1.loadbalancer.dicta.org.il/api',
]
const TIMEOUT_MS = 3000
const CONTEXT_CHARS = 400
const CACHE_LIMIT = 200

const MARKS = /[֑-ׇ]/g
const FINAL_TO_BASE: Record<string, string> = { 'ך': 'כ', 'ם': 'מ', 'ן': 'נ', 'ף': 'פ', 'ץ': 'צ' }

/** For comparing: no nikkud, quote marks or punctuation; finals as plain letters; single spaces. */
export function normalizeForMatch(text: string): string {
  return [...text.replace(MARKS, '').replace(/[^א-ת\s]/g, ' ')]
    .map((ch) => FINAL_TO_BASE[ch] ?? ch)
    .join('')
    .replace(/\s+/g, ' ')
    .trim()
}

/** Whether an abbreviation mark is present (" ' ׳ ״). */
export function hasAbbrevMark(text: string): boolean {
  return /["'׳״“”‘’]/.test(text)
}

export interface RankedSense {
  text: string
  /** True for a Dicta option with no dictionary sense. */
  fromDicta: boolean
}

/**
 * Dicta's ranking decides the order. Dictionary senses matching a Dicta option (whole sense
 * or one of its comma/semicolon parts) take that option's place; options with no sense are
 * added in their place marked fromDicta; unmatched senses follow in their own order.
 * Options equal to the abbreviation itself (Dicta's "unsure") are dropped.
 */
export function mergeDictaRanking(abbreviation: string, senses: string[], options: string[]): RankedSense[] {
  const self = normalizeForMatch(abbreviation).replace(/ /g, '')
  const keysOf = (sense: string) => {
    // A "headword — text" label: compare the text part.
    const body = sense.includes(' — ') ? sense.slice(sense.indexOf(' — ') + 3) : sense
    const keys = new Set<string>([normalizeForMatch(body)])
    for (const part of body.split(/[,;،]/)) {
      const key = normalizeForMatch(part)
      if (key) keys.add(key)
    }
    return keys
  }
  const senseKeys = senses.map(keysOf)
  const used = new Set<number>()
  const out: RankedSense[] = []
  const seenOptions = new Set<string>()
  for (const option of options) {
    const key = normalizeForMatch(option)
    if (!key || key.replace(/ /g, '') === self || seenOptions.has(key)) continue
    seenOptions.add(key)
    let matched = false
    senseKeys.forEach((keys, i) => {
      if (used.has(i) || !keys.has(key)) return
      used.add(i)
      out.push({ text: senses[i]!, fromDicta: false })
      matched = true
    })
    if (!matched) out.push({ text: option.replace(MARKS, '').trim(), fromDicta: true })
  }
  senses.forEach((text, i) => { if (!used.has(i)) out.push({ text, fromDicta: false }) })
  return out
}

/** The context sent: the line trimmed to ~400 chars around the selection, plus where the abbreviation sits in it. */
export function contextAround(line: string, start: number, end: number): { text: string; start: number; end: number } {
  if (line.length <= CONTEXT_CHARS) return { text: line, start, end }
  const half = Math.floor((CONTEXT_CHARS - (end - start)) / 2)
  let from = Math.max(0, start - half)
  let to = Math.min(line.length, end + half)
  // Cut on spaces so no word is sent broken.
  const sp1 = line.indexOf(' ', from)
  if (from > 0 && sp1 !== -1 && sp1 < start) from = sp1 + 1
  const sp2 = line.lastIndexOf(' ', to)
  if (to < line.length && sp2 >= end) to = sp2
  return { text: line.slice(from, to), start: start - from, end: end - from }
}

interface DictaToken { str?: string; abbreviation?: { options?: string[] } }

/** Options of the token covering [start,end) in the sent text. */
export function optionsAt(tokens: DictaToken[], start: number, end: number): string[] | null {
  let pos = 0
  for (const token of tokens) {
    const str = token.str ?? ''
    const tEnd = pos + str.length
    if (pos < end && tEnd > start && token.abbreviation?.options?.length) return token.abbreviation.options
    pos = tEnd
  }
  return null
}

const cache = new Map<string, string[]>()

/** Dicta's ranked options for the abbreviation at [start,end) of the context, or null. */
export async function fetchDictaOptions(
  context: { text: string; start: number; end: number },
  signal: AbortSignal,
): Promise<string[] | null> {
  if (typeof navigator !== 'undefined' && navigator.onLine === false) return null
  const key = `${context.text.slice(context.start, context.end)}\u0000${context.text}`
  const hit = cache.get(key)
  if (hit) return hit
  const body = JSON.stringify({
    task: 'abbrexp', data: context.text, genre: 'rabbinic', useTokenization: true, userData: 'gave permission',
  })
  const deadline = AbortSignal.any([signal, AbortSignal.timeout(TIMEOUT_MS)])
  for (const url of ENDPOINTS) {
    if (deadline.aborted) return null
    try {
      const response = await fetch(url, {
        method: 'POST',
        // Simple request on purpose: a JSON content type triggers a CORS preflight Dicta fails.
        headers: { 'Content-Type': 'text/plain;charset=UTF-8' },
        body,
        signal: deadline,
      })
      if (!response.ok) continue
      const data = (await response.json())?.data
      if (!Array.isArray(data)) continue
      const options = optionsAt(data as DictaToken[], context.start, context.end)
      if (!options) return null
      if (cache.size >= CACHE_LIMIT) cache.delete(cache.keys().next().value!)
      cache.set(key, options)
      return options
    } catch {
      if (deadline.aborted) return null
    }
  }
  return null
}
