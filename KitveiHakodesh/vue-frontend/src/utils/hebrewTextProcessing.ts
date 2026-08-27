/**
 * Hebrew text processing utilities.
 * State 0: full diacritics (nikkud + cantillation)
 * State 1: remove cantillation only (U+0591–U+05AF, U+05C0)
 * State 2: remove nikkud, convert the dibbur hamatchil dash to a full stop and drop
 * the rest — delegates to stripNikkudFromHtml
 *
 * Operates directly on the HTML string with regex — no DOM parsing — so it is
 * safe to call on every render cycle without layout/GC cost.
 * Tag content (< ... >) is skipped so attribute values are never mutated.
 * HTML entities encoding Hebrew diacritics (&#xNNNN; or &#NNNNN;) are resolved
 * before filtering so they are caught by the unicode range patterns.
 */
/**
 * Decode numeric HTML entities holding Hebrew diacritics so the strip regexes,
 * which only ever match literal characters, can see them. Covers &#xNNNN; (hex)
 * and &#NNNNN; (decimal) only — named entities like &nbsp; are never diacritics.
 */
function decodeDiacriticEntities(html: string): string {
  return html.replace(/&#x([0-9a-fA-F]+);|&#([0-9]+);/g, (match, hex, dec) => {
    const codePoint = hex != null ? parseInt(hex, 16) : parseInt(dec, 10)
    // Only decode codepoints in the Hebrew diacritic ranges we care about
    if (codePoint >= 0x0591 && codePoint <= 0x05C7) return String.fromCodePoint(codePoint)
    return match
  })
}

export function applyDiacriticsFilter(html: string, state: number): string {
  if (state === 0 || !html || html === '\u00A0') return html

  const decoded = decodeDiacriticEntities(html)

  // stripNikkudFromHtml decodes on its own, so state 2 is correct whether callers
  // arrive through here or call it directly.
  if (state >= 2) return stripNikkudFromHtml(decoded)

  // state === 1: remove cantillation only
  return decoded.replace(/(<[^>]*>)|([^<]+)/g, (_, tag: string, text: string) => {
    if (tag) return tag
    return text.replace(/[\u0591-\u05AF\u05C0]/g, '')
  })
}

/**
 * Tags that end one line and start the next, so the dibbur hamatchil latch resets.
 * Inline tags (b, i, span, a…) are deliberately absent: a lemma is often wrapped in
 * one, and the dash that follows it still belongs to the line it opened.
 */
const BLOCK_BOUNDARY_TAG = /^<\/?(?:div|p|br|li|tr|td|th|h[1-6]|section|article|blockquote)\b/i

/**
 * Strip cantillation marks and nikkud, convert the dibbur hamatchil dash to a full
 * stop (dropping later dashes), and
 * normalize punctuation from an HTML string. Tag attributes are preserved.
 *
 * This is the canonical nikkud-stripping logic shared by the book-view renderer
 * (via cleanHebrewText in hebrewTextCleaning.ts). Any change to what
 * "remove nikkud" strips must be made here and nowhere else.
 *
 * Transformations applied to text nodes only (tags are passed through unchanged):
 *   - Cantillation marks U+0591–U+05AF, U+05C0 removed
 *   - Nikkud U+05B0–U+05BD, U+05BF, U+05C1, U+05C2, U+05C4, U+05C5, U+05C7 removed
 *     (U+05BF RAFE included: it draws a bar over the letter and would read as a
 *     stray dash. U+05BE MAQAF is excluded — it is a separator, not a mark.)
 *   - Dash types followed by a space: the FIRST one in each line becomes a full
 *     stop, since it separates a dibbur hamatchil (the lemma opening a
 *     Rashi/Tosafot comment) from the body. Which dash is used there is
 *     inconsistent across the corpus, so all of them convert. Every later
 *     dash-space is removed as decoration, leaving its space as the word gap:
 *       hyphen-minus U+002D (-), en dash U+2013 (–), em dash U+2014 (—),
 *       figure dash U+2012, horizontal bar U+2015 (―), minus sign U+2212 (−)
 *       A dash between two letters is preserved. The divine-name censor separator
 *       is U+2011 (non-breaking hyphen, e.g. א‑ל) and is never in this class at all.
 *   - ! → .   ? → .   ; → , (modern punctuation uncommon in older Hebrew texts)
 *     A run of ! and ? (e.g. ?! or !!) collapses to a single dot, not one per mark.
 */
export function stripNikkudFromHtml(html: string): string {
  // The first dash-space of a line separates the dibbur hamatchil from the body, so
  // it becomes a full stop; every later dash-space is dropped as decoration. Which
  // dash the corpus uses there is inconsistent (hyphen and em dash both occur), so
  // the whole dash class feeds this rule.
  //
  // The latch is per LINE, not per call: the render paths pass one line, but the
  // copy paths join many lines into a single string, and each of those still needs
  // its own dibbur hamatchil. It resets on a block boundary — never on an inline
  // tag, since "<b>lemma</b> - body" must still get its dot after the </b>.
  let dhSeparatorDone = false
  // Whether a lemma has appeared since the last block boundary. A chunk can open
  // with the dash ("<b>lemma</b> - body"), so what precedes it in this chunk is not
  // the test — the lemma may have been the previous chunk entirely.
  let lemmaSeen = false
  // Decoded here rather than only in applyDiacriticsFilter: cleanHebrewText calls
  // this directly for state 2, so entity-encoded nikkud would otherwise survive the
  // one mode whose whole job is removing it.
  return decodeDiacriticEntities(html).replace(/(<[^>]*>)|([^<]+)/g, (_, tag: string, text: string) => {
    if (tag) {
      if (BLOCK_BOUNDARY_TAG.test(tag)) {
        dhSeparatorDone = false
        lemmaSeen = false
      }
      return tag
    }
    text = text.replace(/[\u0591-\u05AF\u05C0]/g, '')
    // The dot hugs the lemma, so the DH match also swallows any horizontal space and period
    // already sitting in front of the dash. A later dash is only decoration: it
    // goes, but its leading space stays behind as the word gap.
    // Lines are separated by block tags, never by a bare newline: the copy paths wrap
    // each line in a tag, and txt-view's merged runs join with a space on purpose.
    text = text.replace(/(^|[^])([^\S\n]*)\.?[^\S\n]*[\u002D\u2012–—\u2015\u2212] /g, (_m: string, before: string, gap: string) => {
      if (before !== '' && !/\s/.test(before)) lemmaSeen = true
      // No lemma yet on this line means no sentence for the dot to close, so the
      // dash just goes; a leading full stop would read worse than the dash did.
      if (dhSeparatorDone || !lemmaSeen) return before + gap
      dhSeparatorDone = true
      return before + '. '
    })
    // A chunk with visible text leaves a lemma behind for a dash in the NEXT chunk,
    // which is how "<b>lemma</b> - body" survives the tag boundary.
    if (/\S/.test(text)) lemmaSeen = true
    // U+05BF RAFE draws a bar above the letter, so a leftover one reads as a stray
    // dash. Search already discards it (see SEARCH_IGNORED_MARKS); display matches.
    text = text.replace(/[\u05B0-\u05BD\u05BF\u05C1\u05C2\u05C4\u05C5\u05C7]/g, '')
    // A run like ?! is one mark of punctuation, so it collapses to a single dot
    text = text.replace(/[!?]+/g, '.')
    // Replace standalone semicolons but not ones inside HTML entities like &nbsp;
    text = text.replace(/(?<!&[^;\s]{0,10});/g, ',')
    return text
  })
}

/**
 * U+05BE MAQAF joins two words into one accented unit. It sits inside the
 * Hebrew mark block but is punctuation, not a diacritic: dropping it would fuse
 * the words it joins into a single token, so a two-word query could never match
 * a maqaf-joined phrase. It becomes a space instead — the same separator the
 * reader typed.
 */
export const MAQAF = '\u05BE'

/**
 * Every Hebrew mark that search discards outright: the U+0591–U+05C7 block
 * minus the maqaf, which is a separator (see MAQAF) rather than a mark.
 */
const SEARCH_IGNORED_MARKS = /[\u0591-\u05BD\u05BF-\u05C7]/g

/** True for a character search drops without advancing its position count. */
export function isSearchIgnoredMark(ch: string): boolean {
  const code = ch.charCodeAt(0)
  return code >= 0x0591 && code <= 0x05C7 && code !== 0x05BE
}

/** Strip Hebrew diacritics for search matching, keeping the maqaf as a space. */
export function removeDiacriticsForSearch(text: string): string {
  return text.replace(SEARCH_IGNORED_MARKS, '').split(MAQAF).join(' ')
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

    // The maqaf survives as a space so it still occupies one position — the
    // walkers that map positions back into the HTML count it the same way.
    if (ch === MAQAF) result += ' '
    else if (!isSearchIgnoredMark(ch)) result += ch
    i++
  }
  return result
}
