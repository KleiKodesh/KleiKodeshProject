/**
 * Per-mark punctuation settings for clean text, packed into the same flags number as the
 * CLEAN_TEXT toggles (2 bits per mark, from bit 8 up), so every render/copy path keeps
 * passing one number.
 *
 * Purpose: let the reader turn modern punctuation into the classic rabbinic style (only
 * periods, or periods and commas, a colon closing a passage) at any level they choose.
 * Brackets are not punctuation here and are never touched; neither are citation colons
 * (chapter:verse).
 */

export const MARK_MODE = { KEEP: 0, CONVERT: 1, REMOVE: 2 } as const
export type MarkMode = (typeof MARK_MODE)[keyof typeof MARK_MODE]

export interface PunctuationMark {
  /** i18n key suffix: bookView.toolbar.mark.<key> */
  key: string
  shift: number
  /** The modes this mark offers, in menu order. */
  modes: readonly MarkMode[]
  /** i18n key suffix of the CONVERT label: bookView.toolbar.markMode.<convertTo> */
  convertTo?: 'toPeriod' | 'toComma'
}

const K = MARK_MODE.KEEP
const C = MARK_MODE.CONVERT
const R = MARK_MODE.REMOVE

/** ? and ! (a run like ?! is one mark). Convert: a period. */
export const MARK_QUESTION_EXCLAMATION: PunctuationMark = { key: 'questionExclamation', shift: 8, modes: [K, C, R], convertTo: 'toPeriod' }
/** ; Convert: a comma. */
export const MARK_SEMICOLON: PunctuationMark = { key: 'semicolon', shift: 10, modes: [K, C, R], convertTo: 'toComma' }
export const MARK_COMMA: PunctuationMark = { key: 'comma', shift: 12, modes: [K, R] }
/** A colon inside a line (not a citation). Convert: a period. */
export const MARK_COLON: PunctuationMark = { key: 'colon', shift: 14, modes: [K, C, R], convertTo: 'toPeriod' }
/** The classic colon closing a passage, at the end of a line. Convert: a period. */
export const MARK_PASSAGE_END_COLON: PunctuationMark = { key: 'passageEndColon', shift: 16, modes: [K, C], convertTo: 'toPeriod' }
/** A period inside a line; the one ending a line always stays. */
export const MARK_PERIOD: PunctuationMark = { key: 'period', shift: 18, modes: [K, R] }
/** Quotation marks; remove keeps the acronym gershayim (a word's last two letters). */
export const MARK_QUOTES: PunctuationMark = { key: 'quotes', shift: 20, modes: [K, R] }
/** Dashes used as punctuation (the dibbur hamatchil dash has its own toggle). */
export const MARK_DASH: PunctuationMark = { key: 'dash', shift: 22, modes: [K, R] }

export const PUNCTUATION_MARKS: readonly PunctuationMark[] = [
  MARK_QUESTION_EXCLAMATION, MARK_SEMICOLON, MARK_COMMA, MARK_COLON,
  MARK_PASSAGE_END_COLON, MARK_PERIOD, MARK_QUOTES, MARK_DASH,
]

/** Every punctuation bit; zero means "leave punctuation as it is". */
export const PUNCTUATION_MASK = PUNCTUATION_MARKS.reduce((mask, mark) => mask | (3 << mark.shift), 0)

export function markMode(flags: number, mark: PunctuationMark): MarkMode {
  return ((flags >> mark.shift) & 3) as MarkMode
}

export function withMarkMode(flags: number, mark: PunctuationMark, mode: MarkMode): number {
  return (flags & ~(3 << mark.shift)) | (mode << mark.shift)
}

function preset(modes: [MarkMode, MarkMode, MarkMode, MarkMode, MarkMode, MarkMode, MarkMode, MarkMode]): number {
  return PUNCTUATION_MARKS.reduce((flags, mark, i) => withMarkMode(flags, mark, modes[i]!), 0)
}

/**
 * Levels from modern to classic. Order of each row: ?! ; , colon passage-end-colon period
 * quotes dash.
 */
export const PUNCTUATION_PRESETS: readonly { key: string; value: number }[] = [
  { key: 'original', value: 0 },
  { key: 'classic', value: preset([C, C, K, K, K, K, R, R]) },
  { key: 'periodsColons', value: preset([C, R, R, K, K, K, R, R]) },
  { key: 'periodsOnly', value: preset([C, R, R, C, K, K, R, R]) },
  { key: 'none', value: preset([R, R, R, R, K, R, R, R]) },
]
