/**
 * Hebrew text processing utilities.
 *
 * Operates directly on the HTML string with regex — no DOM parsing — so it is
 * safe to call on every render cycle without layout/GC cost.
 * Tag content (< ... >) is skipped so attribute values are never mutated.
 * HTML entities encoding Hebrew diacritics (&#xNNNN; or &#NNNNN;) are resolved
 * before filtering so they are caught by the unicode range patterns.
 */
import {
  PUNCTUATION_PRESETS, MARK_MODE, markMode,
  MARK_QUESTION_EXCLAMATION, MARK_SEMICOLON, MARK_COMMA, MARK_DASH,
} from './punctuationModes'

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
 * The independent clean-text toggles, as bit flags (bits 0-7). Punctuation is set per mark
 * in the bits above (punctuationModes.ts). The book view's clean-text combo picks any
 * combination; the copy-clean-text paths use CLEAN_TEXT_LEGACY.
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
  /**
   * Not a user option: the caller knows the text is commentary, so a short PLAIN heading
   * (no bold) before the dash also counts as a dibbur hamatchil. Only read with
   * DIBBUR_HAMATCHIL_DASH.
   */
  PLAIN_DIBBUR_HAMATCHIL: 64,
} as const

/**
 * The user's clean-text flags for text known to be commentary: a short plain heading then
 * counts as a dibbur hamatchil too. Used for rendering AND annotation offsets, which must agree.
 */
export function cleanTextFlagsFor(flags: number, isCommentary: boolean): number {
  return isCommentary && flags & CLEAN_TEXT.DIBBUR_HAMATCHIL_DASH ? flags | CLEAN_TEXT.PLAIN_DIBBUR_HAMATCHIL : flags
}

/** What "remove nikkud" always did in one go - still what copy-clean-text applies. */
export const CLEAN_TEXT_LEGACY =
  CLEAN_TEXT.TEAMIM | CLEAN_TEXT.NIKKUD | CLEAN_TEXT.DIBBUR_HAMATCHIL_DASH |
  PUNCTUATION_PRESETS.find((p) => p.key === 'classic')!.value

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
 * attributes are preserved. The punctuation rules that need to scan across characters
 * (colons, periods, quotes, spacing) live in cleanHebrewText, which calls this first.
 *
 * Transformations applied to text nodes only (tags are passed through unchanged):
 *   - TEAMIM / NIKKUD: the marks above
 *   - DIBBUR_HAMATCHIL_DASH: see separateDibburHamatchil
 *   - MAQAF, and the context-free punctuation marks (finishPunctuation)
 */
export function stripNikkudFromHtml(html: string, flags: number = CLEAN_TEXT_LEGACY): string {
  // Entity-encoded nikkud would otherwise survive the one option whose whole job is
  // removing it.
  let source = decodeDiacriticEntities(html)
  if (flags & CLEAN_TEXT.DIBBUR_HAMATCHIL_DASH) {
    source = separateDibburHamatchil(source, (flags & CLEAN_TEXT.PLAIN_DIBBUR_HAMATCHIL) !== 0)
  }
  return source.replace(/(<[^>]*>)|([^<]+)/g, (_, tag: string, text: string) => {
    if (tag) return tag
    // A paseq stands between spaces; removing it alone would leave two.
    if (flags & CLEAN_TEXT.TEAMIM) text = text.replace(/ \u05C0 /g, ' ').replace(TEAMIM_MARKS, '')
    if (flags & CLEAN_TEXT.NIKKUD) text = text.replace(NIKKUD_MARKS, '')
    return finishPunctuation(text, flags)
  })
}

/** A dash used as punctuation, standing before a space. All of them occur as the separator. */
const SPACED_DASH = /(^|\s)[-‒–—―−] /
/** Leading numbering a heading may carry: "(1)", "12.", "1)" - Hebrew letters too. */
const HEADING_NUMBERING = /^\s*(?:[([]?[א-ת\d'"׳״]{1,5}[)\].]\s*)?$/
/** What may follow the bold heading before the dash: only its own closing mark. */
const HEADING_TAIL = /^\s*[.:]?\s*$/
/** Plain-heading limit, measured on seforim.db: most real headings are 6 words or fewer. */
const PLAIN_HEADING_MAX_WORDS = 6

/**
 * DIBBUR_HAMATCHIL_DASH. A commentary line opens with heading words (the dibbur hamatchil)
 * and a dash; that dash becomes a full stop. Elsewhere a dash is a pause in the sentence
 * (vocalized Talmud, Berakhot 13a: "...if he directed his heart - he fulfilled") and is
 * left alone.
 *
 * Measured on a labelled seforim.db sample (line_dh = ground truth): the old "first dash
 * after any text" rule put a wrong full stop on 43% of dash lines. The heading test:
 *   - the text before the line's first spaced dash is ONE bold span, optionally after
 *     numbering and before its own "." or ":" (precision 0.93, recall 0.86);
 *   - with `plainHeadingOk` (the caller knows the text is commentary), also up to
 *     PLAIN_HEADING_MAX_WORDS plain words with no inner punctuation (recall 0.99).
 * The dash character itself cannot tell the two apart: both use all three dash types.
 *
 * A heading already ending in "." or ":" keeps it and the dash just goes. Only the first
 * spaced dash of a line is judged; later ones are punctuation. A dash with nothing
 * visible in front of it opens no sentence: it stays, and the next one is judged.
 * Lines are separated by block tags (copy paths join many lines in one string).
 */
function separateDibburHamatchil(html: string, plainHeadingOk: boolean): string {
  let linePrefix = ''
  let judged = false
  return html.replace(/(<[^>]*>)|([^<]+)/g, (_, tag: string, text: string) => {
    if (tag) {
      if (BLOCK_BOUNDARY_TAG.test(tag)) {
        linePrefix = ''
        judged = false
      } else if (!judged) {
        linePrefix += tag
      }
      return tag
    }
    if (judged) return text
    let out = ''
    for (;;) {
      const match = SPACED_DASH.exec(text)
      if (!match) {
        linePrefix += text
        return out + text
      }
      const dashStart = match.index + (match[1] ?? '').length
      const before = text.slice(0, dashStart)
      const after = text.slice(match.index + match[0].length)
      const visible = (linePrefix + before).replace(/<[^>]*>/g, '')
      if (!/\S/.test(visible)) {
        // Nothing to close yet (a list or table cell opening with a dash): keep the dash
        // and judge the next one.
        const kept = text.slice(0, match.index + match[0].length)
        out += kept
        // Left out of the prefix, so the heading after it is still judged on its own.
        linePrefix += before
        text = after
        continue
      }
      judged = true
      if (!isDibburHamatchil(linePrefix + before, plainHeadingOk)) return out + text
      // The full stop hugs the heading; one already there is kept instead.
      const head = before.replace(/\s+$/, '')
      return out + (/[.:]\s*$/.test(visible) ? head : head + '.') + ' ' + after
    }
  })
}

/** Is `prefix` (the line's HTML before its first dash) a dibbur hamatchil? */
function isDibburHamatchil(prefix: string, plainHeadingOk: boolean): boolean {
  // Empty tags carry nothing: Vilna Rashi is "<b><b></b><i></i>words</b>".
  let html = prefix
  for (let prev = ''; prev !== html; ) {
    prev = html
    html = html.replace(/<([a-z][a-z0-9]*)\b[^>]*>\s*<\/\1>/gi, '')
  }
  // The text outside bold, before and after it, and the number of top-level bold spans.
  let depth = 0
  let spans = 0
  let outsideBefore = ''
  let outsideAfter = ''
  for (const [, tag, text] of html.matchAll(/(<[^>]*>)|([^<]+)/g)) {
    if (tag) {
      const bold = /^<(\/?)(?:b|strong)\b/i.exec(tag)
      if (!bold) continue
      if (bold[1]) depth = Math.max(0, depth - 1)
      else if (depth++ === 0) spans++
    } else if (depth === 0) {
      if (spans === 0) outsideBefore += text
      else outsideAfter += text
    }
  }
  // A bold span still open at the dash is a heading only in commentary: base texts that
  // bold a whole line have the dash inside the bold.
  if (spans === 1 && depth > 0 && !plainHeadingOk) return false
  if (spans === 1) return HEADING_NUMBERING.test(outsideBefore) && HEADING_TAIL.test(outsideAfter)
  if (spans > 1 || !plainHeadingOk) return false
  const words = outsideBefore.trim().replace(/[.:]$/, '').trim()
  return !/[,.:;!?()[\]]/.test(words) && words.split(/\s+/).length <= PLAIN_HEADING_MAX_WORDS
}

/** Semicolons, but not the one closing an HTML entity like &nbsp; */
const SEMICOLON = /(?<!&[^;\s]{0,10});/g
/** A long dash glued between two words: their separator. */
const GLUED_LONG_DASH = /(?<=\S)[\u2012\u2013\u2014\u2015\u2212](?=\S)/g
/** Punctuation dashes: the long ones anywhere, a hyphen only standing alone. */
const PUNCTUATION_DASH = /[\u2012\u2013\u2014\u2015\u2212]|(?<!\S)-(?!\S)/g

/** The per-text-node parts of MAQAF and the context-free punctuation marks. */
function finishPunctuation(text: string, flags: number): string {
  // A hyphen joining two Hebrew words is a maqaf typed on the keyboard (437 in a 21k-line
  // sample). Digit ranges (615-567) and a hyphen inside a Latin word stay.
  if (flags & CLEAN_TEXT.MAQAF) text = text.replace(/\u05BE|(?<=[\u05D0-\u05EA][\u0591-\u05C7]*)-(?=[\u05D0-\u05EA])/g, ' ')
  // A run like ?! is one mark, so it becomes a single period.
  const questionExclamation = markMode(flags, MARK_QUESTION_EXCLAMATION)
  if (questionExclamation !== MARK_MODE.KEEP) text = text.replace(/[!?]+/g, questionExclamation === MARK_MODE.CONVERT ? '.' : '')
  const semicolon = markMode(flags, MARK_SEMICOLON)
  if (semicolon !== MARK_MODE.KEEP) text = text.replace(SEMICOLON, semicolon === MARK_MODE.CONVERT ? ',' : '')
  if (markMode(flags, MARK_COMMA) === MARK_MODE.REMOVE) text = text.replace(/,/g, '')
  if (markMode(flags, MARK_DASH) === MARK_MODE.REMOVE) {
    text = text.replace(GLUED_LONG_DASH, ' ').replace(PUNCTUATION_DASH, '')
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
