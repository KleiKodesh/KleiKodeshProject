import { stripNikkudFromHtml } from './hebrewTextProcessing'

const CODE_HEBREW_START = 0x05D0
const CODE_HEBREW_END = 0x05EA
const CODE_TAB = 0x09
const CODE_LF = 0x0A
const CODE_CR = 0x0D
const CODE_SPACE = 0x20

function isHebrewLetter(ch: string | undefined): boolean {
  if (ch === undefined) return false
  const code = ch.charCodeAt(0)
  return code >= CODE_HEBREW_START && code <= CODE_HEBREW_END
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
for (const ch of ['<', '>', ':', '"', '&', '.', ' ', '\u05F4']) {
  SPECIAL_TABLE[ch.charCodeAt(0)] = 1
}

/**
 * Clean Hebrew book text for display and copy.
 *
 * Applied by both the book-view renderer (הסר ניקוד state) and the copy
 * action (העתק טקסט נקי). Both paths must produce identical output — this
 * is the single source of truth for all Hebrew text cleaning.
 *
 * Operates directly on the HTML string without DOM parsing — safe to call
 * on every render cycle. Tags are passed through unchanged; only text nodes
 * are transformed.
 *
 * Transformations (on top of stripNikkudFromHtml):
 *   - Colons kept only when at end-of-line (followed by a tag or end of string);
 *     mid-sentence colons (vowel-pointing artifacts) are dropped
 *   - Stray double-quotes ("  ״) not between two words are dropped
 *   - Multiple consecutive spaces collapsed to one
 *   - A space inserted after a period when the immediately following character
 *     is a Hebrew letter and the period does not follow a single-letter word
 */
export function cleanHebrewText(html: string): string {
  const source = stripNikkudFromHtml(html)
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

    // ── Colon ─────────────────────────────────────────────────────────────
    if (character === ':') {
      let lookahead = specialIndex + 1
      while (lookahead < length && source[lookahead] === ' ') lookahead++
      // Keep the colon when it is followed by a tag (end-of-sentence before markup)
      // or when it is at the very end of the string (last character of the line).
      if (lookahead >= length || source[lookahead] === '<') {
        output.push(':')
        lastOutputChar = ':'
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

        if (!(precededByBoundary || followedByBoundary)) {
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

      if (!(precededByBoundary || followedByBoundary)) {
        output.push(character)
        lastOutputChar = character
      }
      pos = quoteEnd
      runStart = pos
      continue
    }

    // ── Period — insert trailing space when needed ──────────────────────────
    if (character === '.') {
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
