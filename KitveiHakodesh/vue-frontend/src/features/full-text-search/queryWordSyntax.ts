/**
 * Per-word search options, read from and written to the query text itself.
 *
 * The search bar's query IS the per-word state: each word carries its own syntax, so the
 * options are saved with the search (tab, history, cache key) and need no store of their own.
 *   - wrap (one at most): `*word*` wildcard, `%word%` grammar, `word~1` fuzzy
 *   - leading markers: `+` related forms, `^` spelling variants (`+^%word%`)
 * Only the "all words" choice of each option is a persisted setting.
 */

export type WrapOption = 'wildcard' | 'grammar' | 'fuzzy'
export type MarkOption = 'ketiv' | 'related'
export type WordOption = WrapOption | MarkOption

export const WRAP_OPTIONS: WrapOption[] = ['wildcard', 'grammar', 'fuzzy']
export const WORD_OPTIONS: WordOption[] = [...WRAP_OPTIONS, 'ketiv', 'related']

export const isWrapOption = (option: WordOption): option is WrapOption =>
  (WRAP_OPTIONS as WordOption[]).includes(option)

/** One query word split into its options and its bare text. */
export interface QueryWord {
  core: string
  wrap: WrapOption | null
  /** The wrap exactly as typed (`*` or `%` on one side, `~2`...), kept until the wrap changes. */
  lead: string
  trail: string
  ketiv: boolean
  related: boolean
}

/** The leading `+` / `^` markers of a token, in any order. */
export function splitMarks(token: string): { marks: string; rest: string } {
  const rest = token.replace(/^[+^]+/, '')
  return { marks: token.slice(0, token.length - rest.length), rest }
}

/** A token as a QueryWord, or null when its syntax is more than the toggles express
 *  (`|`, `?`, mixed wraps, a `*` inside the word...): such a word gets no toggles.
 *  A one-sided `*` or `%`, or `~N`, counts as that wrap and is kept as typed. */
export function parseQueryWord(token: string): QueryWord | null {
  const { marks, rest } = splitMarks(token)
  const m = rest.match(/^([*%]*)([^*?~%|+^]+)([*%]*|~[1-9]?)$/)
  if (!m) return null
  const lead = m[1] ?? '', core = m[2] ?? '', trail = m[3] ?? ''
  const sides = lead + trail
  let wrap: WrapOption | null = null
  if (trail.startsWith('~')) wrap = lead ? null : 'fuzzy'
  else if (/^\*+$/.test(sides)) wrap = 'wildcard'
  else if (/^%+$/.test(sides)) wrap = 'grammar'
  if (sides && !wrap) return null  // mixed, e.g. %word*
  return { core, wrap, lead, trail, ketiv: marks.includes('^'), related: marks.includes('+') }
}

export function renderQueryWord(word: QueryWord): string {
  return (word.related ? '+' : '') + (word.ketiv ? '^' : '') + word.lead + word.core + word.trail
}

/** The wrap's default form, both sides: `*word*`, `%word%`, `word~1`. */
function wrapSides(wrap: WrapOption | null): { lead: string; trail: string } {
  if (wrap === 'wildcard') return { lead: '*', trail: '*' }
  if (wrap === 'grammar') return { lead: '%', trail: '%' }
  if (wrap === 'fuzzy') return { lead: '', trail: '~1' }
  return { lead: '', trail: '' }
}

export function hasOption(word: QueryWord, option: WordOption): boolean {
  return isWrapOption(option) ? word.wrap === option : word[option]
}

export function withOption(word: QueryWord, option: WordOption, on: boolean): QueryWord {
  if (isWrapOption(option)) {
    if (on) return word.wrap === option ? word : { ...word, wrap: option, ...wrapSides(option) }
    return word.wrap === option ? { ...word, wrap: null, ...wrapSides(null) } : word
  }
  return { ...word, [option]: on }
}
