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
 * This setting covers the tetragrammaton only. The other divine names have their
 * own settings — see ElokimMode (אלהים family) and OtherNameKey (אדני, אל, שדי).
 * 'none' here is the master off switch: it disables all censoring.
 *
 * - 'none'      — no censoring at all, text passes through untouched
 * - 'yudDaled'  — יהוה → ידוד  (ה→ד in place, all marks kept)
 * - 'yudKuf'    — יהוה → יקוק  (ה→ק in place, all marks kept)
 * - 'doubleYud' — יהוה → יי    (both ה dropped; the second י inherits the ו's nikkud,
 *                              and every cantillation mark in the name is preserved)
 * - 'heApostrophe' — יהוה → ה'  (nikkud discarded; cantillation marks are gathered
 *                              onto the ה so the name keeps its trope)
 * - 'hyphen'    — יהוה → י‑ה‑ו‑ה  (all four letters kept, separated; every mark
 *                              stays on the letter that carried it)
 */
export type DivineNameMode =
  | 'none'
  | 'yudDaled'
  | 'yudKuf'
  | 'doubleYud'
  | 'heApostrophe'
  | 'hyphen'

export const DIVINE_NAME_MODES: readonly DivineNameMode[] = [
  'yudDaled',
  'yudKuf',
  'doubleYud',
  'heApostrophe',
  'hyphen',
  'none',
]

/** Hebrew labels for the settings UI, in display order. */
export const DIVINE_NAME_MODE_OPTIONS: readonly { value: DivineNameMode; label: string }[] = [
  { value: 'yudDaled', label: 'ידוד' },
  { value: 'yudKuf', label: 'יקוק' },
  { value: 'doubleYud', label: 'יי' },
  { value: 'heApostrophe', label: "ה'" },
  { value: 'hyphen', label: 'י‑ה‑ו‑ה' },
  { value: 'none', label: 'כתיב מלא' },
]

export const DEFAULT_DIVINE_NAME_MODE: DivineNameMode = 'yudDaled'

/**
 * How the אלהים family (אלהים, אלוהים, אלהי, אלוה) is censored.
 *
 * These names contain a ה, so they can either be broken with a separator or
 * have the ה swapped for another letter — or be left alone entirely.
 *
 * - 'hyphen' — א‑להים  (non-breaking separator after the א)
 * - 'kuf'    — אלקים   (ה→ק in place, all marks kept)
 * - 'daled'  — אלדים   (ה→ד in place, all marks kept)
 * - 'none'   — אלהים   (printed in full, uncensored)
 */
export type ElokimMode = 'hyphen' | 'kuf' | 'daled' | 'none'

export const ELOKIM_MODES: readonly ElokimMode[] = ['hyphen', 'kuf', 'daled', 'none']

/** Hebrew labels for the settings UI, in display order. */
export const ELOKIM_MODE_OPTIONS: readonly { value: ElokimMode; label: string }[] = [
  { value: 'hyphen', label: 'א‑להים' },
  { value: 'kuf', label: 'אלקים' },
  { value: 'daled', label: 'אלדים' },
  { value: 'none', label: 'כתיב מלא' },
]

export const DEFAULT_ELOKIM_MODE: ElokimMode = 'hyphen'

/**
 * The remaining names from the Shulchan Aruch's list of seven that may not be
 * erased (YD 276): אדני, אל, שדי, יה, צבאות, אהיה. None of these get letter
 * substitution — for אדני/אל/שדי/צבאות there is no ה to swap; for יה/אהיה the
 * ה carries the whole word, so swapping it would leave nothing recognisable.
 * Each name is independently either hyphen-separated (אדנ‑י) or left as-is.
 * Selection replaces the old single hyphen/none toggle: the user picks which
 * names to censor.
 */
export type OtherNameKey = 'adnai' | 'el' | 'shadai' | 'yah' | 'tzevaot' | 'ehyeh'

export const OTHER_NAME_KEYS: readonly OtherNameKey[] = ['adnai', 'el', 'shadai', 'yah', 'tzevaot', 'ehyeh']

/** Hebrew labels for the settings UI, in display order. */
export const OTHER_NAME_OPTIONS: readonly { value: OtherNameKey; label: string }[] = [
  { value: 'adnai', label: 'אדנ‑י' },
  { value: 'el', label: 'א‑ל' },
  { value: 'shadai', label: 'ש‑די' },
  { value: 'yah', label: 'י‑ה' },
  { value: 'tzevaot', label: 'צ‑באות' },
  { value: 'ehyeh', label: 'א‑היה' },
]

/**
 * Every name except אהיה is censored by default. אהיה is spelled identically
 * to the mundane verb "I will be" outside of its one appearance in Shemot 3:14
 * (אהיה אשר אהיה), so blanket-censoring it would hit ordinary text; leave it
 * opt-in rather than on by default.
 */
export const DEFAULT_OTHER_NAMES_SELECTED: readonly OtherNameKey[] = ['adnai', 'el', 'shadai', 'yah', 'tzevaot']

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

/** Coerce a persisted value into a valid ElokimMode, or null to keep the default. */
export function normalizeElokimMode(value: unknown): ElokimMode | null {
  if (typeof value === 'string' && (ELOKIM_MODES as readonly string[]).includes(value)) {
    return value as ElokimMode
  }
  return null
}

/**
 * Coerce a persisted value into a valid list of selected other-name keys, or
 * null to keep the default. Migrates the legacy mode string: 'hyphen' → all
 * names selected, 'none' → none selected.
 */
export function normalizeOtherNamesSelected(value: unknown): OtherNameKey[] | null {
  if (value === 'hyphen') return [...DEFAULT_OTHER_NAMES_SELECTED]
  if (value === 'none') return []
  if (Array.isArray(value) && value.every((v) => (OTHER_NAME_KEYS as readonly string[]).includes(v))) {
    return value as OtherNameKey[]
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

/**
 * U+2011 NON-BREAKING HYPHEN — the separator used inside censored names
 * (א‑להים, ק‑ל, ש‑די).
 *
 * A plain hyphen-minus (U+002D) is a line-break opportunity, so a censored name
 * could wrap mid-word and end up split across two lines — unacceptable for a
 * divine name. U+2011 renders identically but forbids the break, and unlike a
 * CSS nowrap rule it travels with the text, so copy-to-clipboard and paste into
 * Word keep the name whole.
 */
const SEP = '‑'

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
    case 'hyphen':
      // י‑ה‑ו‑ה — all four letters kept and separated. Each capture group is a
      // letter plus its own marks, so joining the groups leaves every nikkud and
      // te'am on the letter that carried it.
      return {
        regex: TETRA_RE,
        replacement: (_m: string, y: string, h1: string, v: string, h2: string) =>
          [y, h1, v, h2].join(SEP),
      }
  }
}

/**
 * The אלהים family (אלהים, אלוהים, אלהי, אלוה).
 *
 * Each rule captures the ה as its own group, so 'kuf'/'daled' can swap just that
 * letter and leave its points and te'amim in place; 'hyphen' instead inserts the
 * separator after the א and leaves every letter alone. 'none' contributes no
 * rules, so these names pass through untouched.
 */
function elokimRules(mode: ElokimMode): Rule[] {
  // Uncensored — contribute no rules at all, leaving these names untouched.
  if (mode === 'none') return []

  // For the substitution modes the ה group keeps its marks and only the letter changes.
  const he = (h: string) => (mode === 'hyphen' ? h : h.replace('ה', mode === 'kuf' ? 'ק' : 'ד'))
  // The separator goes in only when we are not substituting a letter.
  const sep = mode === 'hyphen' ? SEP : ''

  return [
    // אלהים → א-להים / אלקים / אלדים  (not followed by אחרים)
    {
      regex: new RegExp(`(א${D})(ל${D})(ה${D})(י${D})(ם${D})(?!\\s*א${D}ח${D}ר${D}י${D}ם)${HWB}`, 'g'),
      replacement: (_m: string, a: string, l: string, h: string, y: string, m: string) =>
        a + sep + l + he(h) + y + m,
    },
    // אלוהים → א-לוהים / אלוקים / אלודים  (not followed by אחרים)
    {
      regex: new RegExp(
        `(א${D})(ל${D})(ו${D})(ה${D})(י${D})(ם${D})(?!\\s*א${D}ח${D}ר${D}י${D}ם)${HWB}`,
        'g',
      ),
      replacement: (_m: string, a: string, l: string, v: string, h: string, y: string, m: string) =>
        a + sep + l + v + he(h) + y + m,
    },
    // אלהי → א-להי / אלקי / אלדי
    {
      regex: new RegExp(`(א${D})(ל${D})(ה${D})(י${D})${HWB}`, 'g'),
      replacement: (_m: string, a: string, l: string, h: string, y: string) =>
        a + sep + l + he(h) + y,
    },
    // אלוה → א-לוה / אלוק / אלוד
    {
      regex: new RegExp(`(א${D})(ל${D})(ו${D})(ה${D})${HWB}`, 'g'),
      replacement: (_m: string, a: string, l: string, v: string, h: string) =>
        a + sep + l + v + he(h),
    },
  ]
}

/**
 * The divine names containing no ה: אדני, אל, שדי.
 * Also covers יה: it does contain a ה, but it is a two-letter name where the ה
 * carries the whole word — swapping it (יק / יד) would leave nothing
 * recognisable — so it only ever gets the hyphen treatment, never substitution.
 *
 * There is no letter to substitute in any of these four, so each is either
 * separated or left alone, independently of the others — see OtherNameKey.
 */
function noHeNameRules(selected: readonly OtherNameKey[]): Rule[] {
  const rules: Rule[] = []

  if (selected.includes('adnai')) {
    // אדני → אדנ-י
    // Only censor when the נ carries a kamatz (ָ), which identifies the divine name אֲדֹנָי.
    // Any other vowel on the נ (chirik, patach, etc.) is a regular word — skip.
    rules.push({
      regex: new RegExp(`(א${D})(ד${D})(נ[\\u0591-\\u05C7]*\\u05B8[\\u0591-\\u05C7]*)(י${D})${HWB}`, 'g'),
      replacement: `$1$2$3${SEP}$4`,
    })
  }

  if (selected.includes('el')) {
    // אל with tsere (צרה) → א-ל
    // Tsere is ֵ. Only censor when אל stands as its own word — meaning the character
    // before any prefix must be a non-Hebrew character (space, punctuation, start of string).
    // Supports zero, one, or two single-letter prefixes (ו ב כ ל מ ש ה) with their diacritics.
    // Prefix letters are listed as plain Unicode code points to avoid embedding nikkud inside
    // the character class. The prefix(es) are captured as group 1 and restored unchanged.
    // The lamed may carry an optional chirik + yud suffix (the possessive form of the same
    // name), with cantillation allowed on any of those letters. Only that vocalized form is
    // matched — an unvocalized spelling is left alone, since it is an ordinary word.
    // ב=ב ו=ו כ=כ ל=ל מ=מ ש=ש ה=ה
    rules.push({
      regex: new RegExp(
        `(?:^|(?<=[^\\u05D0-\\u05EA\\u0591-\\u05C7]))` +
        `([\\u05D1\\u05D5\\u05DB\\u05DC\\u05DE\\u05E9\\u05D4]${D}(?:[\\u05D1\\u05D5\\u05DB\\u05DC\\u05DE\\u05E9\\u05D4]${D})?)?(א[\\u0591-\\u05C7]*\\u05B5[\\u0591-\\u05C7]*)(ל${D}(?:\u05B4${D}י${D})?)${HWB}`,
        'gm',
      ),
      replacement: (_m: string, prefix: string | undefined, a: string, l: string) =>
        (prefix ?? '') + a + SEP + l,
    })
  }

  if (selected.includes('shadai')) {
    // שדי with patach under shin and kamatz under dalet → ש-די
    // Patach = ַ, Kamatz = ָ
    rules.push({
      regex: new RegExp(`(ש\\u05B7[\\u0591-\\u05C7]*)(ד\\u05B8[\\u0591-\\u05C7]*)(י${D})${HWB}`, 'g'),
      replacement: (_m: string, sh: string, d: string, y: string) => sh + SEP + d + y,
    })
    // שדי with patach under shin and patach under dalet → ש-די
    rules.push({
      regex: new RegExp(`(ש\\u05B7[\\u0591-\\u05C7]*)(ד\\u05B7[\\u0591-\\u05C7]*)(י${D})${HWB}`, 'g'),
      replacement: (_m: string, sh: string, d: string, y: string) => sh + SEP + d + y,
    })
  }

  if (selected.includes('yah')) {
    // יה → י-ה
    // Matches י with kamatz (ָ) followed by ה with any diacritics/teamim, as a standalone word.
    // Must run after the יהוה rule so it never fires mid-match on the four-letter name.
    rules.push({
      regex: new RegExp(`(י[\\u0591-\\u05C7]*\\u05B8[\\u0591-\\u05C7]*)(ה${D})${HWB}`, 'g'),
      replacement: (_m: string, y: string, h: string) => y + SEP + h,
    })
  }

  if (selected.includes('tzevaot')) {
    // צבאות → צ-באות
    // Standalone word — צ ב א ו ת with any diacritics, split after the צ.
    rules.push({
      regex: new RegExp(`(צ${D})(ב${D}א${D}ו${D}ת${D})${HWB}`, 'g'),
      replacement: (_m: string, tz: string, rest: string) => tz + SEP + rest,
    })
  }

  if (selected.includes('ehyeh')) {
    // אהיה → א-היה  (only the phrase אהיה אשר אהיה, Shemot 3:14)
    // The bare word אהיה is also the ordinary verb "I will be" everywhere else in
    // Tanach, so only censor when followed by אשר אהיה — the specific divine Name.
    rules.push({
      regex: new RegExp(
        `(א${D})(ה${D}י${D}ה${D})(?=\\s+א${D}ש${D}ר${D}\\s+א${D}ה${D}י${D}ה${D})`,
        'g',
      ),
      replacement: (_m: string, a: string, rest: string) => a + SEP + rest,
    })
    // The trailing אהיה in the phrase — censor it too, once the phrase is confirmed
    // by the lookbehind mirroring the lookahead above.
    rules.push({
      regex: new RegExp(
        `(?<=א${D}ש${D}ר${D}\\s+)(א${D})(ה${D}י${D}ה${D})${HWB}`,
        'g',
      ),
      replacement: (_m: string, a: string, rest: string) => a + SEP + rest,
    })
  }

  return rules
}

/** The three independent censoring choices. */
export interface CensorOptions {
  /** How the tetragrammaton (יהוה) is written. 'none' disables ALL censoring. */
  mode?: DivineNameMode
  /** How the אלהים family is censored. */
  elokim?: ElokimMode
  /** Which of the names with no ה (אדני, אל, שדי) are censored. */
  otherNames?: readonly OtherNameKey[]
}

/**
 * Apply divine-name censoring to `text`.
 *
 * Accepts either a bare DivineNameMode (the tetragrammaton setting, with the
 * other two groups at their defaults) or a CensorOptions object. Everything
 * defaults such that a bare call reproduces the original ידוד behaviour.
 *
 * `mode: 'none'` is the master off switch — it returns the text untouched
 * regardless of the other two settings.
 */
export function censorDivineNames(
  text: string,
  options: DivineNameMode | CensorOptions = DEFAULT_DIVINE_NAME_MODE,
): string {
  const {
    mode = DEFAULT_DIVINE_NAME_MODE,
    elokim = DEFAULT_ELOKIM_MODE,
    otherNames = DEFAULT_OTHER_NAMES_SELECTED,
  } = typeof options === 'string' ? { mode: options } : options

  if (mode === 'none') return text

  const rules = [
    tetragrammatonRule(mode),
    ...elokimRules(elokim),
    ...noHeNameRules(otherNames),
  ]

  let result = text
  for (const { regex, replacement } of rules) {
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
