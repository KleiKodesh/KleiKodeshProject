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
  ketiv: boolean
  related: boolean
}

/** The leading `+` / `^` markers of a token, in any order. */
export function splitMarks(token: string): { marks: string; rest: string } {
  const rest = token.replace(/^[+^]+/, '')
  return { marks: token.slice(0, token.length - rest.length), rest }
}

/** A token as a QueryWord, or null when its syntax is more than the toggles express
 *  (`|`, `?`, a one-sided `*` or `%`, `~2`...): such a word gets no toggles. */
export function parseQueryWord(token: string): QueryWord | null {
  const { marks, rest } = splitMarks(token)
  let core = rest
  let wrap: WrapOption | null = null
  let m: RegExpMatchArray | null
  if ((m = rest.match(/^\*([^*?~%|]+)\*$/))) { wrap = 'wildcard'; core = m[1] ?? '' }
  else if ((m = rest.match(/^%([^*?~%|]+)%$/))) { wrap = 'grammar'; core = m[1] ?? '' }
  else if ((m = rest.match(/^([^*?~%|]+)~1?$/))) { wrap = 'fuzzy'; core = m[1] ?? '' }
  if (!core || /[*?~%|+^]/.test(core)) return null
  return { core, wrap, ketiv: marks.includes('^'), related: marks.includes('+') }
}

export function renderQueryWord(word: QueryWord): string {
  const wrapped =
    word.wrap === 'wildcard' ? `*${word.core}*`
    : word.wrap === 'grammar' ? `%${word.core}%`
    : word.wrap === 'fuzzy' ? `${word.core}~1`
    : word.core
  return (word.related ? '+' : '') + (word.ketiv ? '^' : '') + wrapped
}

export function hasOption(word: QueryWord, option: WordOption): boolean {
  return isWrapOption(option) ? word.wrap === option : word[option]
}

export function withOption(word: QueryWord, option: WordOption, on: boolean): QueryWord {
  if (isWrapOption(option)) {
    if (on) return { ...word, wrap: option }
    return word.wrap === option ? { ...word, wrap: null } : word
  }
  return { ...word, [option]: on }
}
