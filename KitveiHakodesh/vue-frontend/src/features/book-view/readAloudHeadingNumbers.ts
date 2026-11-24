import type { ReadAloudText } from './readAloudSkip'
import { withExpansions, type Expansion } from './readAloudDicta'

/**
 * Heading numbers read the way a learner says them: "פרק ג" is read "פרק גימל",
 * "סימן י"ב" is read "סימן יוד בית".
 *
 * Only a known label at the START of a heading line counts (a scan of the seforim TOC
 * showed labels later in a heading are mostly ordinary words), followed by a number in its
 * written form - so "פרק שירה" stays as it is. Further label + number pairs in the same
 * heading ("פרק ג פסוק א") are read the same way.
 */

/** Labels taken from the real TOC data: the ones a number follows at the start of a heading. */
const LABELS = new Set([
  'פרק', 'פסוק', 'סימן', 'סעיף', 'תשובה', 'שאלה', 'דף', 'עמוד', 'רמז', 'אות', 'פסקה', 'פיסקא',
  'כלל', 'מצוה', 'מצווה', 'מאמר', 'חלק', 'ענף', 'שאילתא', 'הלכה', 'משנה', 'שער', 'פרשה', 'מזמור',
])

const LETTER_NAMES: Record<string, string> = {
  'א': 'אלף', 'ב': 'בית', 'ג': 'גימל', 'ד': 'דלת', 'ה': 'הא', 'ו': 'וו', 'ז': 'זין', 'ח': 'חית',
  'ט': 'טית', 'י': 'יוד', 'כ': 'כף', 'ך': 'כף', 'ל': 'למד', 'מ': 'מם', 'ם': 'מם', 'נ': 'נון',
  'ן': 'נון', 'ס': 'סמך', 'ע': 'עין', 'פ': 'פא', 'ף': 'פא', 'צ': 'צדי', 'ץ': 'צדי', 'ק': 'קוף',
  'ר': 'ריש', 'ש': 'שין', 'ת': 'תיו',
}

/** A number in its written form: hundreds, at most one tens and one units letter (ט"ו / ט"ז for 15 / 16). */
function isWrittenNumber(letters: string): boolean {
  if (letters === 'טו' || letters === 'טז') return true
  return letters.length > 0 && /^ת*[קרש]?[יכלמנסעפצ]?[אבגדהוזחט]?$/.test(letters) && !/י[הו]$/.test(letters)
}

/** label, optional comma / colon, then a number with or without geresh / gershayim. */
const PAIR = /([א-ת]+)[,:]?\s+([א-ת]{1,4}(?:['"׳״][א-ת])?['׳]?)(?=$|[\s.,:;)\-])/gy

/** Whether the line is only label + number pairs - a heading even without a heading tag. */
function isOnlyLabels(spoken: string): boolean {
  // Word by word, not one big regex: a nested-repeat regex backtracks for ages on a long
  // line and froze the page. A label line is short anyway.
  if (spoken.length > 60) return false
  const words = spoken.trim().split(/[\s,:.]+/).filter(Boolean)
  if (words.length < 2 || words.length % 2 !== 0) return false
  for (let i = 0; i < words.length; i += 2) {
    if (!LABELS.has(words[i]!) || !/^[א-ת'"׳״]{1,6}$/.test(words[i + 1]!)) return false
  }
  return true
}

/** A number marker at the line's start: "(כה)", "[ז']", "יב)" - the closing bracket is required. */
const LINE_MARKER = /^[(\[]?([א-ת]{1,4})['"׳״]?[)\]]/

const FINAL_TO_PLAIN: Record<string, string> = { 'ך': 'כ', 'ם': 'מ', 'ן': 'נ', 'ף': 'פ', 'ץ': 'צ' }
const plainForms = (letters: string) => letters.replace(/[ךםןףץ]/g, (c) => FINAL_TO_PLAIN[c]!)
const letterNames = (letters: string) => [...letters].map((c) => LETTER_NAMES[c]).join(' ')

export function withSpelledHeadingNumbers(text: ReadAloudText, isHeading: boolean): ReadAloudText {
  const { spoken } = text
  const lead = spoken.length - spoken.trimStart().length
  const expansions: Expansion[] = []
  let pos = lead
  // A line-start number marker is read by its letters on any line: "(כה)" is "כף הא".
  const marker = LINE_MARKER.exec(spoken.slice(lead))
  if (marker && isWrittenNumber(plainForms(marker[1]!))) {
    expansions.push({ start: lead, end: lead + marker[0].length, text: letterNames(marker[1]!) })
    pos = lead + marker[0].length
    pos += /^\s*/.exec(spoken.slice(pos))![0].length
  }
  if (!isHeading && !isOnlyLabels(spoken.slice(pos))) return withExpansions(text, expansions)
  for (;;) {
    PAIR.lastIndex = pos
    const m = PAIR.exec(spoken)
    if (!m || !LABELS.has(m[1]!)) break
    const numeral = m[2]!
    const letters = numeral.replace(/['"׳״]/g, '')
    if (!isWrittenNumber(plainForms(letters))) break
    const start = m.index + m[0].length - numeral.length
    expansions.push({ start, end: start + numeral.length, text: letterNames(letters) })
    pos = m.index + m[0].length
    // Next pair: past a separator and spaces.
    const sep = /^[.,:;\-]?\s*/.exec(spoken.slice(pos))
    pos += sep ? sep[0].length : 0
  }
  return withExpansions(text, expansions)
}
