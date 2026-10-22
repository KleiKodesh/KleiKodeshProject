/**
 * Per-word search options, read from and written to the query text itself.
 *
 * The search bar's query IS the option state: each word carries its own syntax, so the
 * options are saved with the search (tab, history, cache key) and need no store of their own.
 *   - prefixes / suffixes: `*word`, `word*` (both: `*word*`)
 *   - grammatical prefixes / suffixes: `%word`, `word%` (both: `%word%`)
 *   - fuzzy: `word~1` (or `~N` as typed)
 *   - markers: `^` spelling variants among the leading signs (`^%word`); `+` inflections and
 *     `=` synonyms among the trailing signs (`word%+`, `word=`, `word+=`)
 * Per word, `*` and `%` exclude each other on the same side (`*word%` is fine), and `~`
 * excludes both.
 * Only the "all words" choice of each option is a persisted setting.
 * Everything from the first `@` on is the book filter, never word syntax.
 */

export type WrapOption = 'wildcardPrefix' | 'wildcardSuffix' | 'grammarPrefix' | 'grammarSuffix' | 'fuzzy'
export type MarkOption = 'ketiv' | 'inflections' | 'synonyms'
export type WordOption = WrapOption | MarkOption

export const WRAP_OPTIONS: WrapOption[] = ['wildcardPrefix', 'wildcardSuffix', 'grammarPrefix', 'grammarSuffix', 'fuzzy']
export const WORD_OPTIONS: WordOption[] = [...WRAP_OPTIONS, 'ketiv', 'inflections', 'synonyms']

/** Each option's "all words" setting. */
export type AllWords = Record<WordOption, boolean>

/** The side of the word a wrap takes; fuzzy takes the whole word. */
const SIDE: Record<WrapOption, 'lead' | 'trail' | 'both'> = {
  wildcardPrefix: 'lead', grammarPrefix: 'lead',
  wildcardSuffix: 'trail', grammarSuffix: 'trail',
  fuzzy: 'both',
}

export const isWrapOption = (option: WordOption): option is WrapOption =>
  (WRAP_OPTIONS as WordOption[]).includes(option)

/** Two options a word cannot have at once: two wraps on the same side, or fuzzy and a wrap. */
export function optionsConflict(a: WordOption, b: WordOption): boolean {
  if (!isWrapOption(a) || !isWrapOption(b) || a === b) return false
  return SIDE[a] === 'both' || SIDE[b] === 'both' || SIDE[a] === SIDE[b]
}

/** One query word split into its options and its bare text. */
export interface QueryWord {
  core: string
  /** The wrap signs exactly as typed: lead '' | '*' | '%', trail '' | '*' | '%' | '~' | '~N'. */
  lead: string
  trail: string
  ketiv: boolean
  inflections: boolean
  synonyms: boolean
}

const LEADING_SIGNS = /^[*%+=^]*/
const TRAILING_SIGNS = /(?:[*%+=]|~[1-9]?)*$/

/** The token's markers: `^` among its leading signs (`*^word` = `^*word`), `+` (inflections)
 *  and `=` (synonyms) among its trailing signs (`word%+=`; leading ones are read too). `rest` is
 *  the token without them. */
export function splitMarks(token: string): { marks: string; rest: string } {
  const leading = token.match(LEADING_SIGNS)?.[0] ?? ''
  const body = token.slice(leading.length)
  const trailing = body.match(TRAILING_SIGNS)?.[0] ?? ''
  const marks = (leading.includes('+') || trailing.includes('+') ? '+' : '')
    + (leading.includes('=') || trailing.includes('=') ? '=' : '')
    + (leading.includes('^') ? '^' : '')
  return {
    marks,
    rest: leading.replace(/[+=^]/g, '') + body.slice(0, body.length - trailing.length) + trailing.replace(/[+=]/g, ''),
  }
}

/** A token as a QueryWord, or null when its syntax is more than the toggles express
 *  (`|`, `?`, `*word~`, a sign inside the word...): such a word gets no toggles. */
export function parseQueryWord(token: string): QueryWord | null {
  const { marks, rest } = splitMarks(token)
  const m = rest.match(/^([*%]?)([^*?~%|+=^@]+)([*%]?|~[1-9]?)$/)
  if (!m) return null
  const word: QueryWord = {
    core: m[2] ?? '', lead: m[1] ?? '', trail: m[3] ?? '',
    ketiv: marks.includes('^'), inflections: marks.includes('+'), synonyms: marks.includes('='),
  }
  // Fuzzy takes the whole word: a lead with '~' (e.g. *word~) is no toggle.
  return word.lead && word.trail.startsWith('~') ? null : word
}

export function renderQueryWord(word: QueryWord): string {
  return (word.ketiv ? '^' : '') + word.lead + word.core + word.trail
    + (word.inflections ? '+' : '') + (word.synonyms ? '=' : '')
}

export function hasOption(word: QueryWord, option: WordOption): boolean {
  switch (option) {
    case 'wildcardPrefix': return word.lead === '*'
    case 'wildcardSuffix': return word.trail === '*'
    case 'grammarPrefix': return word.lead === '%'
    case 'grammarSuffix': return word.trail === '%'
    case 'fuzzy': return word.trail.startsWith('~')
    default: return word[option]
  }
}

/** The word with the option on or off; turning on a wrap drops the wraps it conflicts with. */
export function withOption(word: QueryWord, option: WordOption, on: boolean): QueryWord {
  if (!isWrapOption(option)) return { ...word, [option]: on }
  if (hasOption(word, option) === on) return word
  let next = word
  if (on)
    for (const other of WRAP_OPTIONS)
      if (optionsConflict(other, option) && hasOption(next, other)) next = withOption(next, other, false)
  const sign = option.startsWith('wildcard') ? '*' : '%'
  switch (option) {
    case 'wildcardPrefix':
    case 'grammarPrefix': return { ...next, lead: on ? sign : '' }
    case 'wildcardSuffix':
    case 'grammarSuffix': return { ...next, trail: on ? sign : '' }
    case 'fuzzy': return { ...next, trail: on ? '~1' : '' }
  }
}

/** The query split at its book filter: word tokens of the search term, and the `@...` tail. */
export function splitQuery(query: string): { tokens: string[]; filter: string } {
  const at = query.indexOf('@')
  const term = at < 0 ? query : query.slice(0, at)
  return { tokens: term.trim().split(/\s+/).filter(Boolean), filter: at < 0 ? '' : query.slice(at) }
}

export function joinQuery(tokens: string[], filter: string): string {
  return [tokens.join(' '), filter].filter(Boolean).join(' ')
}

/** Map every parseable word of the query; other tokens and the filter stay as typed. */
export function rewriteQuery(query: string, map: (word: QueryWord) => QueryWord): string {
  const { tokens, filter } = splitQuery(query)
  const next = tokens.map((token) => {
    const word = parseQueryWord(token)
    return word ? renderQueryWord(map(word)) : token
  })
  return joinQuery(next, filter)
}

/** The "all words" options written onto every word; a wrap is skipped on a word holding a
 *  wrap it conflicts with (the word's own choice wins). */
export function applyAllWords(query: string, allWords: AllWords): string {
  const on = WORD_OPTIONS.filter((option) => allWords[option])
  if (!on.length) return query
  return rewriteQuery(query, (word) => {
    let next = word
    for (const option of on) {
      if (WRAP_OPTIONS.some((other) => optionsConflict(other, option) && hasOption(next, other))) continue
      next = withOption(next, option, true)
    }
    return next
  })
}

/** The query cut into runs of syntax signs and plain text, for the search input's coloring:
 *  a word's leading `* % + = ^`, trailing `* % + =` or `~N`, and every `|`. The `@` filter is plain. */
export function syntaxSegments(query: string): { text: string; sign: boolean }[] {
  const out: { text: string; sign: boolean }[] = []
  const push = (text: string, sign: boolean) => {
    if (!text) return
    const last = out[out.length - 1]
    if (last && last.sign === sign) last.text += text
    else out.push({ text, sign })
  }
  const at = query.indexOf('@')
  const term = at < 0 ? query : query.slice(0, at)
  for (const part of term.split(/(\s+)/)) {
    const m = part.match(/^([*%+=^]*)(.*?)((?:[*%+=]|~[1-9]?)*)$/)
    if (!part.trim() || !m) { push(part, false); continue }
    push(m[1] ?? '', true)
    for (const piece of (m[2] ?? '').split(/(\|)/)) push(piece, piece === '|')
    push(m[3] ?? '', true)
  }
  if (at >= 0) push(query.slice(at), false)
  return out
}
