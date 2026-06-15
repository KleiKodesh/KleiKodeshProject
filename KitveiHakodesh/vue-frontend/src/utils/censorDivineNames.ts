/**
 * Replaces divine names in Hebrew text with censored equivalents.
 * Preserves all diacritics and cantillation marks on surrounding letters.
 *
 * NOTE: JS \b (word boundary) does not recognise Hebrew characters — it only works with
 * ASCII word chars [a-zA-Z0-9_]. After a Hebrew letter \b never matches, so every pattern
 * that formerly used \b was silently broken. We use a negative lookahead for Hebrew letters
 * and diacritics instead: (?![\u05D0-\u05EA\u0591-\u05C7])
 */
export function censorDivineNames(text: string): string {
  const D = '[\\u0591-\\u05C7]*'
  // Tsere (צרה) = \u05B5
  // Patach (פתח) = \u05B7
  // Kamatz (קמץ) = \u05B8

  // Hebrew word boundary: not followed by another Hebrew letter or diacritic.
  // Replaces \b which does not work after non-ASCII characters.
  const HWB = '(?![\\u05D0-\\u05EA\\u0591-\\u05C7])'

  const patterns: { regex: RegExp; replacement: string | ((...args: string[]) => string) }[] = [
    // יהוה → ידוד
    {
      regex: new RegExp(`(י${D})(ה${D})(ו${D})(ה${D})${HWB}`, 'g'),
      replacement: (_m: string, y: string, h1: string, v: string, h2: string) =>
        y + h1.replace('ה', 'ד') + v + h2.replace('ה', 'ד'),
    },
    // יָהּ → י-הּ
    // Matches י with kamatz (\u05B8) followed by ה with any diacritics/teamim, as a standalone word.
    // Must come after the יהוה rule so it never fires mid-match on the four-letter name.
    {
      regex: new RegExp(`(י[\\u0591-\\u05C7]*\\u05B8[\\u0591-\\u05C7]*)(ה${D})${HWB}`, 'g'),
      replacement: (_m: string, y: string, h: string) => y + '-' + h,
    },
    // אדני → אדנ-י
    // Only censor when the נ carries a kamatz (\u05B8), which identifies the divine name אֲדֹנָי.
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
    // Tsere is \u05B5. Only censor when אל stands as its own word — meaning the character
    // before any prefix must be a non-Hebrew character (space, punctuation, start of string).
    // Supports zero, one, or two single-letter prefixes (ו ב כ ל מ ש ה) with their diacritics.
    // Prefix letters are listed as plain Unicode code points to avoid embedding nikkud inside
    // the character class. The prefix(es) are captured as group 1 and restored unchanged.
    // \u05D1=ב \u05D5=ו \u05DB=כ \u05DC=ל \u05DE=מ \u05E9=ש \u05D4=ה
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
    // Patach = \u05B7, Kamatz = \u05B8
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

  let result = text
  for (const { regex, replacement } of patterns) {
    result =
      typeof replacement === 'function'
        ? result.replace(regex, replacement as (...args: string[]) => string)
        : result.replace(regex, replacement)
  }
  return result
}
