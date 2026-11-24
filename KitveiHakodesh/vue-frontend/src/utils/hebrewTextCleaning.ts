import { stripNikkudFromHtml, CLEAN_TEXT_LEGACY } from './hebrewTextProcessing'
import {
  PUNCTUATION_MASK, MARK_MODE, markMode, MARK_COLON, MARK_PASSAGE_END_COLON, MARK_PERIOD, MARK_QUOTES,
} from './punctuationModes'

const CODE_HEBREW_START = 0x05D0
const CODE_HEBREW_END = 0x05EA
const CODE_DIGIT_START = 0x30
const CODE_DIGIT_END = 0x39
const CODE_TAB = 0x09
const CODE_LF = 0x0A
const CODE_CR = 0x0D
const CODE_SPACE = 0x20

function isHebrewLetter(ch: string | undefined): boolean {
  if (ch === undefined) return false
  const code = ch.charCodeAt(0)
  return code >= CODE_HEBREW_START && code <= CODE_HEBREW_END
}

// A colon flanked on both sides by these is a citation separator (chapter:verse,
// e.g. ט:יג / ח:טו / 12:5), never a vowel-pointing artifact.
function isCitationChar(ch: string | undefined): boolean {
  if (ch === undefined) return false
  const code = ch.charCodeAt(0)
  return (
    (code >= CODE_HEBREW_START && code <= CODE_HEBREW_END) ||
    (code >= CODE_DIGIT_START && code <= CODE_DIGIT_END)
  )
}

/** Marks and invisible direction marks sit between letters without ending the word. */
function isTransparent(ch: string | undefined): boolean {
  if (ch === undefined) return false
  const code = ch.charCodeAt(0)
  return (code >= 0x0591 && code <= 0x05C7 && code !== 0x05BE && code !== 0x05C0 && code !== 0x05C3) ||
    code === 0x200D || code === 0x200E || code === 0x200F
}

/** The nearest character before `index` that is not a mark (see isTransparent). */
function visibleBefore(source: string, index: number): string | undefined {
  let i = index - 1
  while (i >= 0 && isTransparent(source[i])) i--
  return i >= 0 ? source[i] : undefined
}

/** Index of the nearest character at or after `index` that is not a mark. */
function visibleIndexFrom(source: string, index: number, length: number): number {
  let i = index
  while (i < length && isTransparent(source[i])) i++
  return i
}

/** The nearest visible character before `index`, looking through tags ("<b>9</b>:<b>13</b>"). */
function visibleBeforeThroughTags(source: string, index: number): string | undefined {
  let i = index
  for (;;) {
    const ch = visibleBefore(source, i)
    if (ch !== '>') return ch
    let j = i - 1
    while (j >= 0 && isTransparent(source[j])) j--
    const lt = source.lastIndexOf('<', j)
    if (lt === -1) return ch
    // A citation never spans lines.
    if (BLOCK_TAG.test(source.slice(lt, j + 1))) return undefined
    i = lt
  }
}

/** The nearest visible character at or after `index`, looking through tags. */
function visibleFromThroughTags(source: string, index: number, length: number): string | undefined {
  let i = index
  for (;;) {
    i = visibleIndexFrom(source, i, length)
    if (i >= length) return undefined
    if (source[i] !== '<') return source[i]
    const gt = source.indexOf('>', i)
    if (gt === -1 || BLOCK_TAG.test(source.slice(i, gt + 1))) return undefined
    i = gt + 1
  }
}

/** A citation neighbour may also be a geresh/gershayim (L\u05F3:L\u05F3) or a quote. */
function isCitationNeighbour(ch: string | undefined): boolean {
  return isCitationChar(ch) || ch === '\u05F3' || ch === '\u05F4' || ch === "'" || ch === '"'
}

/**
 * What may end a sentence before its colon: a letter, digit, closing bracket, geresh or
 * quote, or a tag end (a footnote mark "...</sup>: "). "word : word" is an artifact.
 */
function endsSentence(ch: string | undefined): boolean {
  return isCitationNeighbour(ch) || ch === ')' || ch === ']' || ch === '>'
}

function isWhitespace(ch: string | undefined): boolean {
  if (ch === undefined) return false
  const code = ch.charCodeAt(0)
  return code === CODE_SPACE || code === CODE_TAB || code === CODE_LF || code === CODE_CR
}

// Shared by both quote-boundary checks except for the '>' / '<' asymmetry.
function isCommonQuoteBoundary(ch: string | undefined): boolean {
  if (ch === undefined) return false
  return ch === ',' || ch === '.' || ch === '"' || ch === '\u05F4' || isWhitespace(ch)
}

/**
 * True when the quote at `quoteIndex` marks \u05E8\u05D0\u05E9\u05D9 \u05EA\u05D9\u05D1\u05D5\u05EA rather than a real quotation.
 *
 * Being flanked by Hebrew letters is not enough: in \u05E8\u05D0\u05E9\u05D9 \u05EA\u05D9\u05D1\u05D5\u05EA the gershayim
 * always sits between the last two letters of the word (\u05E8\u05E9\u05D1"\u05D0, \u05E9\u05DC\u05D9\u05D8"\u05D0). A mark
 * anywhere earlier belongs to a quoted particle \u2014 \u05D5\u05F4\u05D0\u05D9\u05DF \u05D3\u05E7\u05D0\u05DE\u05E8\u05D9 \u05DC\u05D9\u05D4 \u05E7\u05D5\u05E9\u05D8\u05D0 \u05D4\u05D5\u05D0 \u2014
 * where the quote opens a citation and the following letters are a whole word,
 * not an acronym tail. Those must survive cleaning untouched.
 *
 * So the mark qualifies only when a Hebrew letter precedes it and exactly one
 * Hebrew letter follows before the word ends.
 */
function isAbbreviationQuote(source: string, quoteIndex: number, length: number, quoteLength = 1): boolean {
  // Vowel points and direction marks (U+200E after the gershayim) are not letters but
  // do not end the word either, so the neighbours are looked for past them.
  if (!isHebrewLetter(visibleBefore(source, quoteIndex))) return false
  const after = visibleIndexFrom(source, quoteIndex + quoteLength, length)
  if (!isHebrewLetter(after < length ? source[after] : undefined)) return false
  // Exactly one trailing letter => the next visible position must end the word.
  const next = visibleIndexFrom(source, after + 1, length)
  return !isHebrewLetter(next < length ? source[next] : undefined)
}

const BLOCK_TAG = /^<\/?(?:div|p|br|li|tr|td|th|h[1-6]|section|article|blockquote)\b/i

/**
 * Whether the line ends here: only spaces and closing inline tags (</b>, </sup>) before
 * a block tag or the end of the string.
 */
function isAtLineBreak(source: string, from: number, length: number): boolean {
  let i = from
  for (;;) {
    while (i < length && source[i] === ' ') i++
    if (i >= length) return true
    if (source[i] !== '<') return false
    const gt = source.indexOf('>', i)
    if (gt === -1) return true
    const tag = source.slice(i, gt + 1)
    if (BLOCK_TAG.test(tag)) return true
    if (!tag.startsWith('</')) return false
    i = gt + 1
  }
}

// charCode -> 1 lookup table for characters that trigger special handling:
// tag delimiters, colon, both quote forms, the entity-quote opener '&',
// period, and space. A single typed-array read replaces the chain of
// string-equality checks a naive per-character loop would otherwise need,
// and — unlike a regex .exec() per match — costs no per-character-class
// allocation, which matters because real text breaks (the common case,
// average ~3-4 chars in practice) are too short for jump-scanning to pay
// for engine call + match-object overhead.
const MAX_SPECIAL_CODE = 0x05F4
const SPECIAL_TABLE = new Uint8Array(MAX_SPECIAL_CODE + 1)
for (const ch of ['<', '>', ':', '"', '&', '.', ' ', '(', ')', '[', ']', '\u05F4']) {
  SPECIAL_TABLE[ch.charCodeAt(0)] = 1
}

/**
 * Clean Hebrew book text for display and copy.
 *
 * Applied by both the book-view renderer (הסר ניקוד state) and the copy
 * action (העתק טקסט נקי). Both paths must produce identical output — this
 * is the single source of truth for all Hebrew text cleaning.
 *
 * Display passes one line per call while copy passes many joined together, so any
 * per-line rule here must key on block tags rather than on the call boundary (see
 * the dibbur hamatchil latch in stripNikkudFromHtml) or the two paths will drift.
 *
 * Operates directly on the HTML string without DOM parsing — safe to call
 * on every render cycle. Tags are passed through unchanged; only text nodes
 * are transformed.
 *
 * `flags` picks the toggles (CLEAN_TEXT) and the per-mark punctuation modes
 * (punctuationModes.ts); copy-clean-text uses the default. With every mark on "keep" the
 * scan below is skipped. Brackets and citation colons (chapter:verse) are never touched,
 * and neither is anything inside round or square brackets.
 *
 * Scan rules (on top of stripNikkudFromHtml):
 *   - Colon at the end of a line (only spaces and closing tags before a block tag or the
 *     end): MARK_PASSAGE_END_COLON - keep, or a period.
 *   - Colon inside a line: MARK_COLON - keep as is; convert turns a colon ending a
 *     sentence (a word, bracket or footnote tag right before, a space after: 27% of the
 *     colons in a seforim.db sample) into a period and drops loose artifact colons;
 *     remove drops it.
 *   - Period inside a line: MARK_PERIOD remove drops it; the line-end one stays.
 *   - MARK_QUOTES remove: double quotes are kept only as acronyms
 *   - Double-quotes are kept only as ראשי תיבות — flanked by Hebrew letters
 *     with exactly one letter following, so the mark sits between the word's
 *     last two letters (רשב"א). Every other quote is dropped, including one
 *     opening a quoted particle mid-word (ו״אין), which is not an acronym
 *   - Multiple consecutive spaces collapsed to one
 *   - A space inserted after a period when the immediately following character
 *     is a Hebrew letter and the period does not follow a single-letter word
 */
export function cleanHebrewText(html: string, flags: number = CLEAN_TEXT_LEGACY): string {
  if (!flags || !html || html === ' ') return html
  const source = stripNikkudFromHtml(html, flags)
  if (!(flags & PUNCTUATION_MASK)) return source
  const colonMode = markMode(flags, MARK_COLON)
  const passageEndColonMode = markMode(flags, MARK_PASSAGE_END_COLON)
  const removePeriods = markMode(flags, MARK_PERIOD) === MARK_MODE.REMOVE
  const keepQuotes = markMode(flags, MARK_QUOTES) === MARK_MODE.KEEP
  // How many brackets are open here - marks inside them (citations) are left alone.
  let parenDepth = 0
  const length = source.length
  const output: string[] = []

  let insideTag = false
  let lastOutputChar: string | null = null
  let pos = 0
  let runStart = 0

  const pushSlice = (start: number, end: number): void => {
    if (end > start) {
      output.push(source.slice(start, end))
      lastOutputChar = source[end - 1] ?? null
    }
  }

  while (pos < length) {
    if (insideTag) {
      const gt = source.indexOf('>', pos)
      if (gt === -1) {
        // Malformed/truncated tag: consume to end of string; final
        // pushSlice(runStart, length) after the loop flushes it verbatim.
        pos = length
        break
      }
      insideTag = false
      pos = gt + 1
      // Tag interior needs no transformation — leave runStart untouched so
      // it stays folded into whatever run is already open.
      continue
    }

    const code = source.charCodeAt(pos)
    if (code > MAX_SPECIAL_CODE || SPECIAL_TABLE[code] === 0) {
      pos++
      continue
    }

    const specialIndex = pos
    pushSlice(runStart, specialIndex)
    const character = source[specialIndex]!

    // ── Tag open / stray tag close ────────────────────────────────────────
    if (character === '<') {
      insideTag = true
      output.push('<')
      lastOutputChar = '<'
      pos = specialIndex + 1
      runStart = pos
      continue
    }
    if (character === '>') {
      // insideTag is already false here (stray '>' outside a tag) — verbatim.
      output.push('>')
      lastOutputChar = '>'
      pos = specialIndex + 1
      runStart = pos
      continue
    }

    // ── Parentheses - verbatim, only counted ─────────────────────────────────
    if (character === '(' || character === ')' || character === '[' || character === ']') {
      parenDepth = character === '(' || character === '[' ? parenDepth + 1 : Math.max(0, parenDepth - 1)
      output.push(character)
      lastOutputChar = character
      pos = specialIndex + 1
      runStart = pos
      continue
    }

    // ── Colon ─────────────────────────────────────────────────────────────
    if (character === ':') {
      // A citation (9:13, also with gereshim or across tags) and anything in brackets stay.
      const before = visibleBefore(source, specialIndex)
      const isCitation =
        isCitationNeighbour(visibleBeforeThroughTags(source, specialIndex)) &&
        isCitationNeighbour(visibleFromThroughTags(source, specialIndex + 1, length))
      let replacement = ':'
      if (!isCitation && parenDepth === 0) {
        if (isAtLineBreak(source, specialIndex + 1, length)) {
          if (passageEndColonMode === MARK_MODE.CONVERT) replacement = '.'
        } else if (colonMode === MARK_MODE.REMOVE) {
          replacement = ''
        } else if (colonMode === MARK_MODE.CONVERT) {
          // A colon ending a sentence becomes a period; a loose one (" : ", ":word") goes.
          replacement = endsSentence(before) && isWhitespace(source[specialIndex + 1]) ? '.' : ''
        }
      }
      if (replacement) {
        output.push(replacement)
        lastOutputChar = replacement
      }
      pos = specialIndex + 1
      runStart = pos
      continue
    }

    // ── Entity-quote opener / ordinary '&' ──────────────────────────────────
    if (character === '&') {
      if (source.startsWith('&quot;', specialIndex)) {
        const quoteEnd = specialIndex + 6
        const previousCharacter = specialIndex > 0 ? source[specialIndex - 1] : undefined
        const nextCharacter = quoteEnd < length ? source[quoteEnd] : undefined

        const precededByBoundary =
          previousCharacter === undefined ||
          previousCharacter === '>' ||
          isCommonQuoteBoundary(previousCharacter)
        const followedByBoundary =
          nextCharacter === undefined ||
          nextCharacter === '<' ||
          isCommonQuoteBoundary(nextCharacter)

        // The entity spans 6 chars, so its neighbours are not adjacent in
        // `source` the way a literal quote's are — check them directly.
        const isAbbreviation = isAbbreviationQuote(source, specialIndex, length, 6)

        if (keepQuotes) {
          output.push('&quot;')
          lastOutputChar = ';'
        } else if (!(precededByBoundary || followedByBoundary) && isAbbreviation) {
          output.push('"')
          lastOutputChar = '"'
        }
        pos = quoteEnd
        runStart = pos
        continue
      }
      // Not an &quot; entity — ordinary character, kept verbatim.
      output.push('&')
      lastOutputChar = '&'
      pos = specialIndex + 1
      runStart = pos
      continue
    }

    // ── Literal double-quote ─────────────────────────────────────────────
    if (character === '"' || character === '\u05F4') {
      const quoteEnd = specialIndex + 1
      const previousCharacter = specialIndex > 0 ? source[specialIndex - 1] : undefined
      const nextCharacter = quoteEnd < length ? source[quoteEnd] : undefined

      const precededByBoundary =
        previousCharacter === undefined ||
        previousCharacter === '>' ||
        isCommonQuoteBoundary(previousCharacter)
      const followedByBoundary =
        nextCharacter === undefined ||
        nextCharacter === '<' ||
        isCommonQuoteBoundary(nextCharacter)

      if (
        keepQuotes ||
        (!(precededByBoundary || followedByBoundary) && isAbbreviationQuote(source, specialIndex, length))
      ) {
        output.push(character)
        lastOutputChar = character
      }
      pos = quoteEnd
      runStart = pos
      continue
    }

    // ── Period — insert trailing space when needed ──────────────────────────
    if (character === '.') {
      if (removePeriods && parenDepth === 0 && !isAtLineBreak(source, specialIndex + 1, length)) {
        pos = specialIndex + 1
        runStart = pos
        continue
      }
      output.push('.')
      lastOutputChar = '.'

      const nextCharacter = specialIndex < length - 1 ? source[specialIndex + 1] : undefined
      if (isHebrewLetter(nextCharacter)) {
        const previousCharacter = specialIndex > 0 ? source[specialIndex - 1] : undefined
        const twoBack = specialIndex > 1 ? source[specialIndex - 2] : undefined
        const precedingIsSingleLetter =
          isHebrewLetter(previousCharacter) &&
          (twoBack === undefined || isWhitespace(twoBack) || twoBack === '>')

        if (!precedingIsSingleLetter) {
          output.push(' ')
          lastOutputChar = ' '
        }
      }
      pos = specialIndex + 1
      runStart = pos
      continue
    }

    // ── Collapse multiple spaces ─────────────────────────────────────────────
    // character === ' ' (the only remaining case the table lookup can match)
    if (lastOutputChar !== ' ') {
      output.push(' ')
      lastOutputChar = ' '
    }
    pos = specialIndex + 1
    runStart = pos
  }

  pushSlice(runStart, length)

  return output.join('')
}
