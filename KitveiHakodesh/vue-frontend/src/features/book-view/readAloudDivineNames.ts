import type { ReadAloudText } from './readAloudSkip'
import { withExpansions, type Expansion } from './readAloudDicta'

/**
 * Holy names read aloud by their spoken substitutes, whatever way the book writes them:
 * the four-letter name and its written stand-ins (יקוק, ידוד, יי, ה', ד', י‑ה‑ו‑ה) read
 * "השם"; the אלהים family (also אלקים, אלדים, א‑להים) reads with ק - "אלוקים"; אדני reads
 * "אדושם", שדי "שקי", צבאות after a name "צבקות", and the separated forms א‑ל / י‑ה read
 * "קל" / "קה" (plain אל and יה are ordinary words too, so they are left alone).
 *
 * Runs before Dicta, so the substitute gets its nikkud like any other word, and keeps
 * the map to the shown text (each spoken letter maps onto the name it stands for).
 */

/** Any Hebrew point or cantillation mark. */
const M = '[\\u0591-\\u05C7]*'
/** Not followed by a Hebrew letter or mark - JS \b does not work on Hebrew. */
const END = '(?![\\u05D0-\\u05EA\\u0591-\\u05C7])'
/** Not preceded by a Hebrew letter or mark. */
const START = '(?<![\\u05D0-\\u05EA\\u0591-\\u05C7])'
/** A separator inside a censored name: hyphen, non-breaking hyphen, maqaf. */
const SEP = '[-\\u2011\\u05BE]'
/** One-letter prefixes (ו, ב, כ, ל, מ, ש, ה) - kept as written, with their points. */
const PREFIX = `((?:[ובכלמשה]${M}){0,3})`
/** A letter with its marks. */
const L = (letters: string) => `[${letters}]${M}`
const GERESH = `['\\u05F3]`

interface NamePattern {
  re: RegExp
  /** The spoken form of the name itself (the prefix is kept in front of it). */
  spoken: (match: RegExpExecArray) => string
}

const elokimSuffix = (m: RegExpExecArray) => (m[3] ?? '').replace(/[֑-ׇ]/g, '')

const PATTERNS: NamePattern[] = [
  // יהוה and its written stand-ins.
  { re: new RegExp(`${START}${PREFIX}(${L('י')}${SEP}?${L('הקד')}${SEP}?${L('ו')}${SEP}?${L('הקד')})${END}`, 'g'), spoken: () => 'השם' },
  { re: new RegExp(`${START}${PREFIX}(${L('י')}${L('י')}${GERESH}?)${END}`, 'g'), spoken: () => 'השם' },
  // ה' / ד' - not after a label or next to other one-letter numbers (see isNumberContext).
  { re: new RegExp(`${START}${PREFIX}(${L('הד')}${GERESH})(?![\\u05D0-\\u05EA])`, 'g'), spoken: () => 'השם' },
  // The אלהים family: אל (ו) then ה/ק/ד (or a separator after the א), then the ending.
  {
    re: new RegExp(`${START}${PREFIX}(${L('א')}${SEP}?${L('ל')}(?:${L('ו')})?${L('הקד')})((?:${L('י')}(?:${L('ם')}|${L('נ')}${L('ו')}|${L('ך')}|${L('כ')}${L('ם')}|${L('ה')}${L('ם')}|${L('ו')}|${L('ה')}${L('ן')}|${L('כ')}${L('ן')})?)?)${END}`, 'g'),
    spoken: (m) => {
      const suffix = elokimSuffix(m)
      const hasVav = /ו/.test(m[2]!.replace(/[֑-ׇ]/g, '').slice(2))
      // Bare אלה ("these") and אלד / אלק without an ending are not names.
      if (!suffix && !hasVav) return ''
      return `אלוק${suffix}`
    },
  },
  { re: new RegExp(`${START}${PREFIX}(${L('א')}${L('ד')}${L('נ')}${SEP}?${L('י')})${END}`, 'g'), spoken: () => 'אדושם' },
  { re: new RegExp(`${START}${PREFIX}(${L('ש')}${SEP}?${L('ד')}${L('י')})${END}`, 'g'), spoken: () => 'שקי' },
  { re: new RegExp(`${START}${PREFIX}(${L('א')}${SEP}${L('ל')})${END}`, 'g'), spoken: () => 'קל' },
  { re: new RegExp(`${START}${PREFIX}(${L('י')}${SEP}${L('ה')})${END}`, 'g'), spoken: () => 'קה' },
]

/** צבאות right after a name ("ה' צבאות", "אלוקים צבאות"). */
const TZEVAOT = new RegExp(`^\\s+(${L('צ')}${SEP}?${L('ב')}${L('א')}${L('ו')}${L('ת')})${END}`)

/** Words after which a ה' / ד' is a number, not the name: labels and their short forms. */
const NUMBER_LABELS = /^(פרק|סימן|סי|סעיף|סע|ס|דף|עמוד|הלכה|הל|משנה|פסוק|אות|שער|חלק|פרשה|מזמור|תשובה|כלל|ענף|רמז|פסקה|מאמר|שאלה|מצוה|יום|שנת|ב|ג|ד|ה|ו|ז|ח|ט|י)['׳]?$/

/** Words a number counts ("ד' אמות", "ה' פעמים"): before them a ה' / ד' is a number. */
const COUNTED_WORDS = new Set(['אמות', 'אמה', 'טפחים', 'טפח', 'פעמים', 'ימים', 'שנים', 'מינים', 'כוסות', 'חלקים', 'דברים', 'ספרי', 'ספרים', 'מיני', 'כנפות', 'פרשיות', 'בתים', 'שעות', 'זוזים', 'אצבעות', 'רוחות', 'אבות', 'ראשי'])

/** Whether the ה' / ד' at `start` is a number: after a label, beside another letter-number, or before a counted word. */
function isNumberContext(text: string, start: number, end: number): boolean {
  // A marker in brackets - "(ה')", "[ד']" - is a paragraph number.
  if (/[([]/.test(text[start - 1] ?? '') && /[)\]]/.test(text[end] ?? '')) return true
  const before = text.slice(0, start).trimEnd().split(/\s+/).pop() ?? ''
  const after = text.slice(end).trimStart().split(/\s+/)[0] ?? ''
  const strip = (s: string) => s.replace(/[֑-ׇ,.:;()[\]]/g, '')
  const oneLetterNumber = (s: string) => /^[א-ת]['׳]$/.test(strip(s))
  return NUMBER_LABELS.test(strip(before)) || oneLetterNumber(before) || oneLetterNumber(after) || COUNTED_WORDS.has(strip(after))
}

export function withDivineNamesSpoken(text: ReadAloudText): ReadAloudText {
  const { spoken } = text
  if (!/[א-ת]/.test(spoken)) return text
  const found: Expansion[] = []
  const taken = (s: number, e: number) => found.some((f) => s < f.end && e > f.start)
  for (const { re, spoken: say } of PATTERNS) {
    re.lastIndex = 0
    for (let m = re.exec(spoken); m; m = re.exec(spoken)) {
      const prefix = m[1] ?? ''
      const start = m.index + prefix.length
      const end = m.index + m[0].length
      if (taken(start, end)) continue
      if (/^[הד]/.test(m[2] ?? '') && /['׳]$/.test(m[2] ?? '') && isNumberContext(spoken, m.index, end)) continue
      const word = say(m)
      if (!word) continue
      found.push({ start, end, text: word })
      const tz = TZEVAOT.exec(spoken.slice(end))
      if (tz) {
        const tzStart = end + tz[0].length - tz[1]!.length
        found.push({ start: tzStart, end: tzStart + tz[1]!.length, text: 'צבקות' })
      }
    }
  }
  found.sort((a, b) => a.start - b.start)
  return withExpansions(text, found)
}
