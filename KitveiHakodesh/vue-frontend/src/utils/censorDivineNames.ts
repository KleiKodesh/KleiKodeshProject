/**
 * Replaces divine names in Hebrew text with censored equivalents.
 * Preserves all diacritics and cantillation marks on surrounding letters.
 *
 * NOTE: JS \b (word boundary) does not recognise Hebrew characters — it only works with
 * ASCII word chars [a-zA-Z0-9_]. After a Hebrew letter \b never matches, so every pattern
 * that formerly used \b was silently broken. We use a negative lookahead for Hebrew letters
 * and diacritics instead: (?![א-ת֑-ׇ])
 */

/**
 * How the four-letter name (יהוה) is rendered.
 *
 * All modes except 'none' also apply the dash treatment to the other divine names
 * (אדני, אלהים, אלוה, אל, שדי, יה) — only the tetragrammaton itself varies.
 *
 * - 'none'      — no censoring at all, text passes through untouched
 * - 'yudDaled'  — יהוה → ידוד  (ה→ד in place, all marks kept)
 * - 'yudKuf'    — יהוה → יקוק  (ה→ק in place, all marks kept)
 * - 'doubleYud' — יהוה → יי    (both ה dropped; the second י inherits the ו's nikkud,
 *                              and every cantillation mark in the name is preserved)
 * - 'heApostrophe' — יהוה → ה'  (nikkud discarded; cantillation marks are gathered
 *                              onto the ה so the name keeps its trope)
 */
export type DivineNameMode = 'none' | 'yudDaled' | 'yudKuf' | 'doubleYud' | 'heApostrophe'

export const DIVINE_NAME_MODES: readonly DivineNameMode[] = [
  'yudDaled',
  'yudKuf',
  'doubleYud',
  'heApostrophe',
  'none',
]

/** Hebrew labels for the settings UI, in display order. */
export const DIVINE_NAME_MODE_OPTIONS: readonly { value: DivineNameMode; label: string }[] = [
  { value: 'yudDaled', label: 'ידוד' },
  { value: 'yudKuf', label: 'יקוק' },
  { value: 'doubleYud', label: 'יי' },
  { value: 'heApostrophe', label: "ה'" },
  { value: 'none', label: 'כתיב מלא' },
]

export const DEFAULT_DIVINE_NAME_MODE: DivineNameMode = 'yudDaled'

/**
 * Coerce a persisted value into a valid mode.
 * Migrates the legacy boolean setting: true → 'yudDaled' (the only censoring
 * the old build did), false → 'none'.
 */
export function normalizeDivineNameMode(value: unknown): DivineNameMode | null {
  if (value === true) return 'yudDaled'
  if (value === false) return 'none'
  if (typeof value === 'string' && (DIVINE_NAME_MODES as readonly string[]).includes(value)) {
    return value as DivineNameMode
  }
  return null
}

// Any Hebrew point or cantillation mark.
const D = '[\\u0591-\\u05C7]*'
// Cantillation marks (te'amim) only — no vowel points, no dagesh/shin dots.
// U+0591–U+05AF is the te'amim block; meteg and rafe sit outside it and are
// point-class marks, so they are deliberately excluded.
const TEAMIM_RE = /[֑-֯]/g
// Hebrew word boundary: not followed by another Hebrew letter or diacritic.
// Replaces \b which does not work after non-ASCII characters.
const HWB = '(?![\\u05D0-\\u05EA\\u0591-\\u05C7])'

/** Four capture groups: י, ה, ו, ה — each with its trailing marks. */
const TETRA_RE = new RegExp(`(י${D})(ה${D})(ו${D})(ה${D})${HWB}`, 'g')

/** Keep only the cantillation marks from a group, dropping the letter and its vowels. */
function teamimOf(group: string): string {
  return group.match(TEAMIM_RE)?.join('') ?? ''
}

/**
 * יהוה → יי
 *
 * The two ה letters are dropped. The second י takes the ו's nikkud, so
 * יְהוָה becomes יְיָ. Cantillation marks are preserved wherever they sat:
 * marks on the dropped ה letters migrate onto the neighbouring י (vowel points
 * on a dropped letter cannot be kept — the letter they belonged to is gone).
 */
function toDoubleYud(_m: string, y: string, h1: string, v: string, h2: string): string {
  // First י: its own marks, plus any te'amim orphaned by the first ה.
  const first = y + teamimOf(h1)
  // Second י: the ו's marks verbatim (nikkud + te'amim — v[0] is the ו itself),
  // plus te'amim orphaned by the final ה.
  const second = 'י' + v.slice(1) + teamimOf(h2)
  return first + second
}

/**
 * יהוה → ה'
 *
 * All nikkud is discarded — the four vocalized letters collapse to one bare ה,
 * so no vowel has a letter left to sit on. Cantillation is preserved: every
 * te'am found anywhere in the name is gathered onto the ה, before the
 * apostrophe, so a marked name keeps its trope (יְהוָ֖ה → ה֖').
 */
function toHeApostrophe(...groups: string[]): string {
  // groups[0] is the whole match; groups 1-4 are the י ה ו ה letter groups.
  const teamim = groups.slice(1, 5).map(teamimOf).join('')
  return 'ה' + teamim + "'"
}

interface Rule {
  regex: RegExp
  replacement: string | ((...args: string[]) => string)
}

/** Rules for the tetragrammaton, keyed by mode. */
function tetragrammatonRule(mode: Exclude<DivineNameMode, 'none'>): Rule {
  switch (mode) {
    case 'yudDaled':
      return {
        regex: TETRA_RE,
        replacement: (_m: string, y: string, h1: string, v: string, h2: string) =>
          y + h1.replace('ה', 'ד') + v + h2.replace('ה', 'ד'),
      }
    case 'yudKuf':
      return {
        regex: TETRA_RE,
        replacement: (_m: string, y: string, h1: string, v: string, h2: string) =>
          y + h1.replace('ה', 'ק') + v + h2.replace('ה', 'ק'),
      }
    case 'doubleYud':
      return { regex: TETRA_RE, replacement: toDoubleYud }
    case 'heApostrophe':
      // Plain ה' — nikkud discarded, cantillation carried onto the ה.
      return { regex: TETRA_RE, replacement: toHeApostrophe }
  }
}

/**
 * The other divine names. These are censored identically in every mode except
 * 'none' — the mode only selects how the tetragrammaton is written.
 */
function otherNameRules(): Rule[] {
  return [
    // יָהּ → י-הּ
    // Matches י with kamatz (ָ) followed by ה with any diacritics/teamim, as a standalone word.
    // Must come after the יהוה rule so it never fires mid-match on the four-letter name.
    {
      regex: new RegExp(`(י[\\u0591-\\u05C7]*\\u05B8[\\u0591-\\u05C7]*)(ה${D})${HWB}`, 'g'),
      replacement: (_m: string, y: string, h: string) => y + '-' + h,
    },
    // אדני → אדנ-י
    // Only censor when the נ carries a kamatz (ָ), which identifies the divine name אֲדֹנָי.
    // Any other vowel on the נ (chirik, patach, etc.) is a regular word — skip.
    {
      regex: new RegExp(`(א${D})(ד${D})(נ[\\u0591-\\u05C7]*\\u05B8[\\u0591-\\u05C7]*)(י${D})${HWB}`, 'g'),
      replacement: '$1$2$3-$4',
    },
    // אלהים → א-להים (not followed by אחרים)
    {
      regex: new RegExp(`(א${D})(ל${D})(ה${D})(י${D})(ם${D})(?!\\s*א${D}ח${D}ר${D}י${D}ם)${HWB}`, 'g'),
      replacement: (_m: string, a: string, l: string, h: string, y: string, m: string) =>
        a + '-' + l + h + y + m,
    },
    // אלוהים → א-לוהים (not followed by אחרים)
    {
      regex: new RegExp(
        `(א${D})(ל${D})(ו${D})(ה${D})(י${D})(ם${D})(?!\\s*א${D}ח${D}ר${D}י${D}ם)${HWB}`,
        'g',
      ),
      replacement: (_m: string, a: string, l: string, v: string, h: string, y: string, m: string) =>
        a + '-' + l + v + h + y + m,
    },
    // אלהי → א-להי
    {
      regex: new RegExp(`(א${D})(ל${D})(ה${D})(י${D})${HWB}`, 'g'),
      replacement: (_m: string, a: string, l: string, h: string, y: string) =>
        a + '-' + l + h + y,
    },
    // אלוה → א-לוה
    {
      regex: new RegExp(`(א${D})(ל${D})(ו${D})(ה${D})${HWB}`, 'g'),
      replacement: (_m: string, a: string, l: string, v: string, h: string) =>
        a + '-' + l + v + h,
    },
    // אל with tsere (צרה) → א-ל
    // Tsere is ֵ. Only censor when אל stands as its own word — meaning the character
    // before any prefix must be a non-Hebrew character (space, punctuation, start of string).
    // Supports zero, one, or two single-letter prefixes (ו ב כ ל מ ש ה) with their diacritics.
    // Prefix letters are listed as plain Unicode code points to avoid embedding nikkud inside
    // the character class. The prefix(es) are captured as group 1 and restored unchanged.
    // ב=ב ו=ו כ=כ ל=ל מ=מ ש=ש ה=ה
    {
      regex: new RegExp(
        `(?:^|(?<=[^\\u05D0-\\u05EA\\u0591-\\u05C7]))` +
        `([\\u05D1\\u05D5\\u05DB\\u05DC\\u05DE\\u05E9\\u05D4]${D}(?:[\\u05D1\\u05D5\\u05DB\\u05DC\\u05DE\\u05E9\\u05D4]${D})?)?(א[\\u0591-\\u05C7]*\\u05B5[\\u0591-\\u05C7]*)(ל${D})${HWB}`,
        'gm',
      ),
      replacement: (_m: string, prefix: string | undefined, a: string, l: string) =>
        (prefix ?? '') + a + '-' + l,
    },
    // שדי with patach under shin and kamatz under dalet → ש-די
    // Patach = ַ, Kamatz = ָ
    {
      regex: new RegExp(`(ש\\u05B7[\\u0591-\\u05C7]*)(ד\\u05B8[\\u0591-\\u05C7]*)(י${D})${HWB}`, 'g'),
      replacement: (_m: string, sh: string, d: string, y: string) => sh + '-' + d + y,
    },
    // שדי with patach under shin and patach under dalet → ש-די
    {
      regex: new RegExp(`(ש\\u05B7[\\u0591-\\u05C7]*)(ד\\u05B7[\\u0591-\\u05C7]*)(י${D})${HWB}`, 'g'),
      replacement: (_m: string, sh: string, d: string, y: string) => sh + '-' + d + y,
    },
  ]
}

/**
 * Apply divine-name censoring to `text`.
 *
 * `mode` defaults to 'yudDaled' so existing single-argument callers keep the old
 * behaviour. Pass 'none' to get the text back unchanged.
 */
export function censorDivineNames(text: string, mode: DivineNameMode = DEFAULT_DIVINE_NAME_MODE): string {
  if (mode === 'none') return text

  let result = text
  for (const { regex, replacement } of [tetragrammatonRule(mode), ...otherNameRules()]) {
    // Shared RegExp objects are stateful only with /y; /g is reset per .replace call,
    // but reset lastIndex defensively since TETRA_RE is module-level and reused.
    regex.lastIndex = 0
    result =
      typeof replacement === 'function'
        ? result.replace(regex, replacement as (...args: string[]) => string)
        : result.replace(regex, replacement)
  }
  return result
}
