/**
 * Hebrew text processing utilities.
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

/**
 * The independent clean-text options, as bit flags. The book view's clean-text combo
 * picks any set of them; the copy-clean-text paths use CLEAN_TEXT_LEGACY.
 */
export const CLEAN_TEXT = {
  /** Cantillation U+0591\u2013U+05AF and paseq U+05C0. */
  TEAMIM: 1,
  /** Vowel points (NIKKUD_MARKS). */
  NIKKUD: 2,
  /** Maqaf U+05BE becomes a space, as Otzaria does. */
  MAQAF: 4,
  /** The first dash-space of a line (after a dibbur hamatchil) becomes a full stop; later ones go. */
  DIBBUR_HAMATCHIL_DASH: 8,
  /** !? \u2192 .   ; \u2192 ,   stray colons and non-acronym quotes dropped, spaces tidied. */
  NORMALIZE_PUNCTUATION: 16,
  /**
   * Issue #334 / Otzaria's removePunctuation: , ; ! ? and dashes dropped, quotes kept only
   * as acronyms, and . : kept only at the end of a line or inside parentheses.
   */
  REMOVE_PUNCTUATION: 32,
} as const

/** What "remove nikkud" always did in one go - still what copy-clean-text applies. */
export const CLEAN_TEXT_LEGACY =
  CLEAN_TEXT.TEAMIM | CLEAN_TEXT.NIKKUD | CLEAN_TEXT.DIBBUR_HAMATCHIL_DASH | CLEAN_TEXT.NORMALIZE_PUNCTUATION

/**
 * Nikkud U+05B0\u2013U+05BD, U+05BF, U+05C1, U+05C2, U+05C4, U+05C5, U+05C7. U+05BF RAFE is
 * included: it draws a bar over the letter and would read as a stray dash. U+05BE MAQAF
 * is excluded \u2014 it is a separator, not a mark.
 */
const NIKKUD_MARKS = /[\u05B0-\u05BD\u05BF\u05C1\u05C2\u05C4\u05C5\u05C7]/g
const TEAMIM_MARKS = /[\u0591-\u05AF\u05C0]/g

/**
 * Tags that end one line and start the next, so the dibbur hamatchil latch resets.
 * Inline tags (b, i, span, a…) are deliberately absent: a lemma is often wrapped in
 * one, and the dash that follows it still belongs to the line it opened.
 */
const BLOCK_BOUNDARY_TAG = /^<\/?(?:div|p|br|li|tr|td|th|h[1-6]|section|article|blockquote)\b/i

/**
 * Apply the per-text-node clean-text options (CLEAN_TEXT) to an HTML string. Tag
 * attributes are preserved. The options that need to scan across characters
 * (NORMALIZE_PUNCTUATION's colon/quote/space rules, REMOVE_PUNCTUATION's period/colon
 * rule) live in cleanHebrewText, which calls this first.
 *
 * Transformations applied to text nodes only (tags are passed through unchanged):
 *   - TEAMIM / NIKKUD: the marks above
 *   - DIBBUR_HAMATCHIL_DASH: dash types followed by a space: the FIRST one in each line becomes a full
 *     stop, since it separates a dibbur hamatchil (the lemma opening a
 *     Rashi/Tosafot comment) from the body. Which dash is used there is
 *     inconsistent across the corpus, so all of them convert. Every later
 *     dash-space is removed as decoration, leaving its space as the word gap:
 *       hyphen-minus U+002D (-), en dash U+2013 (–), em dash U+2014 (—),
 *       figure dash U+2012, horizontal bar U+2015 (―), minus sign U+2212 (−)
 *       A dash between two letters is preserved. The divine-name censor separator
 *       is U+2011 (non-breaking hyphen, e.g. א‑ל) and is never in this class at all.
 *   - MAQAF, and the context-free parts of the two punctuation options (finishPunctuation)
 */
export function stripNikkudFromHtml(html: string, flags: number = CLEAN_TEXT_LEGACY): string {
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
  // Entity-encoded nikkud would otherwise survive the one option whose whole job is
  // removing it.
  return decodeDiacriticEntities(html).replace(/(<[^>]*>)|([^<]+)/g, (_, tag: string, text: string) => {
    if (tag) {
      if (BLOCK_BOUNDARY_TAG.test(tag)) {
        dhSeparatorDone = false
        lemmaSeen = false
      }
      return tag
    }
    if (flags & CLEAN_TEXT.TEAMIM) text = text.replace(TEAMIM_MARKS, '')
    if (!(flags & CLEAN_TEXT.DIBBUR_HAMATCHIL_DASH)) {
      if (flags & CLEAN_TEXT.NIKKUD) text = text.replace(NIKKUD_MARKS, '')
      return finishPunctuation(text, flags)
    }
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
    if (flags & CLEAN_TEXT.NIKKUD) text = text.replace(NIKKUD_MARKS, '')
    return finishPunctuation(text, flags)
  })
}

/** The per-text-node parts of MAQAF and the two punctuation options. */
function finishPunctuation(text: string, flags: number): string {
  if (flags & CLEAN_TEXT.MAQAF) text = text.replace(/\u05BE/g, ' ')
  if (flags & CLEAN_TEXT.REMOVE_PUNCTUATION) {
    // Semicolons, but not the one closing an HTML entity like &nbsp;
    text = text.replace(/[,!?]|(?<!&[^;\s]{0,10});/g, '')
    // Dashes that are punctuation: the long ones anywhere, a hyphen only standing alone
    return text.replace(/[\u2012\u2013\u2014\u2015\u2212]|(?<!\S)-(?!\S)/g, '')
  }
  if (flags & CLEAN_TEXT.NORMALIZE_PUNCTUATION) {
    // A run like ?! is one mark of punctuation, so it collapses to a single dot
    text = text.replace(/[!?]+/g, '.')
    // Replace standalone semicolons but not ones inside HTML entities like &nbsp;
    text = text.replace(/(?<!&[^;\s]{0,10});/g, ',')
  }
  return text
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

/**
 * Abbreviation marks come in many spellings — the reader types ASCII " or ',
 * the text may hold gershayim U+05F4 / geresh U+05F3, curly quotes, or an
 * &quot; entity. Search folds every one to a single canonical character
 * (one char for one char, so match positions stay aligned with the HTML).
 */
const SEARCH_DOUBLE_QUOTES = /["״“”„]/g
const SEARCH_SINGLE_QUOTES = /['׳‘’‚`]/g
const SEARCH_QUOTE_ENTITIES: Record<string, string> = {
  '&quot;': '"', '&#34;': '"', '&#x22;': '"',
  '&apos;': "'", '&#39;': "'", '&#x27;': "'",
}

function foldQuotesForSearch(text: string): string {
  return text.replace(SEARCH_DOUBLE_QUOTES, '"').replace(SEARCH_SINGLE_QUOTES, "'")
}

/** Strip Hebrew diacritics for search matching, keeping the maqaf as a space. */
export function removeDiacriticsForSearch(text: string): string {
  return foldQuotesForSearch(text.replace(SEARCH_IGNORED_MARKS, '').split(MAQAF).join(' '))
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
  // Plain runs between tags and entities are folded in bulk (one regex pass each),
  // not char by char: a whole-book scan strips tens of MB, and per-char calls made
  // that the slowest part of the in-book search.
  let result = ''
  let i = 0
  const length = html.length
  while (i < length) {
    const ch = html.charCodeAt(i)
    if (ch === 0x3C /* < */) {
      const close = html.indexOf('>', i + 1)
      if (close === -1) break
      i = close + 1
      continue
    }
    if (ch === 0x3E /* > */) { i++; continue }   // a stray '>' outside a tag: dropped, as before
    if (ch === 0x26 /* & */) {
      // Only treat & as an entity start if there is a ; within 12 chars with no whitespace.
      let entityEnd = -1
      for (let j = i + 1; j < length && j <= i + 12; j++) {
        const c = html[j]!
        if (c === ';') { entityEnd = j; break }
        if (c === ' ' || c === '\t' || c === '\n' || c === '<') break
      }
      if (entityEnd !== -1) {
        // Valid entity — collapse to one char and skip past the `;`. Quote
        // entities become the folded quote so abbreviations match.
        result += SEARCH_QUOTE_ENTITIES[html.slice(i, entityEnd + 1).toLowerCase()] ?? '\x00'
        i = entityEnd + 1
        continue
      }
      // Bare & (not a real entity) — a regular character.
      result += '&'
      i++
      continue
    }
    // A run of plain text up to the next tag or entity.
    let end = i + 1
    while (end < length) {
      const c = html.charCodeAt(end)
      if (c === 0x3C || c === 0x26 || c === 0x3E) break
      end++
    }
    result += foldPlainRunForSearch(html.slice(i, end))
    i = end
  }
  return result
}

/**
 * One run of plain text as the search sees it: diacritics removed, the maqaf kept as a
 * space (it still occupies one position for the walkers that map positions back into
 * the HTML), quotes folded.
 */
function foldPlainRunForSearch(run: string): string {
  return foldQuotesForSearch(run.replace(SEARCH_IGNORED_MARKS, '').split(MAQAF).join(' '))
}
