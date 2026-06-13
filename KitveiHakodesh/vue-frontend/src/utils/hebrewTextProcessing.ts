/**
 * Hebrew text processing utilities.
 * State 0: full diacritics (nikkud + cantillation)
 * State 1: remove cantillation only (U+0591–U+05AF, U+05C0)
 * State 2: remove nikkud as well (U+05B0–U+05BD, U+05C1, U+05C2, U+05C4, U+05C5, U+05C7)
 *          and replace modern punctuation uncommon in older Hebrew texts:
 *          ! → .   ? → .   ; → ,
 *
 * Operates directly on the HTML string with regex — no DOM parsing — so it is
 * safe to call on every render cycle without layout/GC cost.
 * Tag content (< ... >) is skipped so attribute values are never mutated.
 * HTML entities encoding Hebrew diacritics (&#xNNNN; or &#NNNNN;) are resolved
 * before filtering so they are caught by the unicode range patterns.
 */
export function applyDiacriticsFilter(html: string, state: number): string {
  if (state === 0 || !html || html === '\u00A0') return html

  // Decode numeric HTML entities that fall in the Hebrew diacritic ranges so they
  // are caught by the regex filters below.
  // Covers &#xNNNN; (hex) and &#NNNNN; (decimal) forms only — named entities like
  // &nbsp; are left as-is since they are never Hebrew diacritics.
  const decoded = html.replace(/&#x([0-9a-fA-F]+);|&#([0-9]+);/g, (match, hex, dec) => {
    const codePoint = hex != null ? parseInt(hex, 16) : parseInt(dec, 10)
    // Only decode codepoints in the Hebrew diacritic ranges we care about
    if (codePoint >= 0x0591 && codePoint <= 0x05C7) return String.fromCodePoint(codePoint)
    return match
  })

  // Process only text segments: split on tags, apply regex only to non-tag parts.
  return decoded.replace(/(<[^>]*>)|([^<]+)/g, (_, tag: string, text: string) => {
    if (tag) return tag
    if (state >= 1) text = text.replace(/[\u0591-\u05AF\u05C0]/g, '')
    if (state >= 2) {
      text = text.replace(/[\u05B0-\u05BD\u05C1\u05C2\u05C4\u05C5\u05C7]/g, '')
      text = text.replace(/[!?]/g, '.')
      // Replace standalone semicolons (punctuation) but not ones that are part of
      // HTML entities like &thinsp; or &nbsp; — those have the form &word; or &#N;
      text = text.replace(/(?<!&[^;\s]{0,10});/g, ',')
    }
    return text
  })
}

/** Strip all Hebrew diacritics for search matching. */
export function removeDiacriticsForSearch(text: string): string {
  return text.replace(/[\u0591-\u05C7]/g, '')
}

/**
 * Strip HTML tags and collapse each HTML entity to a single null-byte sentinel,
 * then remove Hebrew diacritics — producing a flat string where each character
 * position corresponds 1:1 with the position counted by the entity-aware
 * mark-injection walkers in the renderers.
 *
 * Use this instead of `content.replace(/<[^>]*>/g, '')` anywhere the result is
 * used to locate match positions that will be mapped back into original HTML by
 * a walker that skips tags and treats entities as single atomic characters.
 */
export function stripHtmlForSearch(html: string): string {
  let result = ''
  let inTag = false
  let i = 0
  while (i < html.length) {
    const ch = html[i]!
    if (ch === '<') { inTag = true; i++; continue }
    if (ch === '>') { inTag = false; i++; continue }
    if (inTag) { i++; continue }

    // Only treat & as an entity start if there is a ; within 12 chars with no whitespace.
    if (ch === '&') {
      let entityEnd = -1
      for (let j = i + 1; j < html.length && j <= i + 12; j++) {
        const c = html[j]!
        if (c === ';') { entityEnd = j; break }
        if (c === ' ' || c === '\t' || c === '\n' || c === '<') break
      }
      if (entityEnd !== -1) {
        // Valid entity — collapse to sentinel and skip past the `;`.
        result += '\x00'
        i = entityEnd + 1
        continue
      }
      // Bare & (not a real entity) — treat as a regular character.
      result += ch
      i++
      continue
    }

    if (!/[\u0591-\u05C7]/.test(ch)) result += ch
    i++
  }
  return removeDiacriticsForSearch(result)
}
