/**
 * Replaces divine names in Hebrew text with censored equivalents.
 * Preserves all diacritics and cantillation marks on surrounding letters.
 */
export function censorDivineNames(text: string): string {
  const D = '[\\u0591-\\u05C7]*'

  const patterns: { regex: RegExp; replacement: string | ((...args: string[]) => string) }[] = [
    // יהוה → יקוק
    {
      regex: new RegExp(`(י${D})(ה${D})(ו${D})(ה${D})`, 'g'),
      replacement: (_m: string, y: string, h1: string, v: string, h2: string) =>
        y + h1.replace('ה', 'ק') + v + h2.replace('ה', 'ק'),
    },
    // אדני → אדנ-י
    {
      regex: new RegExp(`(א${D})(ד${D})(נ${D})(י${D})`, 'g'),
      replacement: '$1$2$3-$4',
    },
    // אלהים → אלקים (not followed by אחרים)
    {
      regex: new RegExp(`(א${D})(ל${D})(ה${D})(י${D})(ם${D})(?!\\s*א${D}ח${D}ר${D}י${D}ם)`, 'g'),
      replacement: (_m: string, a: string, l: string, h: string, y: string, m: string) =>
        a + l + h.replace('ה', 'ק') + y + m,
    },
    // אלוהים → אלוקים (not followed by אחרים)
    {
      regex: new RegExp(`(א${D})(ל${D})(ו${D})(ה${D})(י${D})(ם${D})(?!\\s*א${D}ח${D}ר${D}י${D}ם)`, 'g'),
      replacement: (_m: string, a: string, l: string, v: string, h: string, y: string, m: string) =>
        a + l + v + h.replace('ה', 'ק') + y + m,
    },
    // אלהי → אלקי
    {
      regex: new RegExp(`(א${D})(ל${D})(ה${D})(י${D})`, 'g'),
      replacement: (_m: string, a: string, l: string, h: string, y: string) =>
        a + l + h.replace('ה', 'ק') + y,
    },
    // אלוה → אלוק (not followed by י or ם)
    {
      regex: new RegExp(`(א${D})(ל${D})(ו${D})(ה${D})(?![יםא])`, 'g'),
      replacement: (_m: string, a: string, l: string, v: string, h: string) =>
        a + l + v + h.replace('ה', 'ק'),
    },
  ]

  let result = text
  for (const { regex, replacement } of patterns) {
    result = typeof replacement === 'function'
      ? result.replace(regex, replacement as (...args: string[]) => string)
      : result.replace(regex, replacement)
  }
  return result
}
