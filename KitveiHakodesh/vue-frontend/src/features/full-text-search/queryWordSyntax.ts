/**
 * Per-word search syntax. The options panel's toggles ("all words" settings and per-word
 * choices) are written onto the words at search time (applyWordOptions); the panel never
 * reads or edits the typed text.
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

/** The words each option is on for, by core; a key present (even empty) means the option's
 *  row is in "per word" mode. Separate from the typed text. */
export type PerWord = Partial<Record<WordOption, string[]>>

/** The toggles written onto the typed words. A word's own typed signs always win: an option
 *  already typed is not doubled, and a wrap that conflicts with a typed wrap is skipped.
 *  Per-word choices are applied before "all words", so a per-word wrap beats a conflicting
 *  "all words" one on that word. */
export function applyWordOptions(query: string, allWords: AllWords, perWord: PerWord): string {
  const all = WORD_OPTIONS.filter((option) => allWords[option])
  const per = WORD_OPTIONS.filter((option) => perWord[option]?.length)
  if (!all.length && !per.length) return query
  return rewriteQuery(query, (word) => {
    const options = [...per.filter((option) => perWord[option]!.includes(word.core)), ...all]
    let next = word
    for (const option of options) {
      if (WRAP_OPTIONS.some((other) => optionsConflict(other, option) && hasOption(next, other))) continue
      next = withOption(next, option, true)
    }
    return next
  })
}
