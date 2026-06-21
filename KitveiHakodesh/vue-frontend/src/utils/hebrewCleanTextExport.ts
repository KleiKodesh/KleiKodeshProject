import { applyDiacriticsFilter } from './hebrewTextProcessing'

const HEBREW_LETTER = /[\u05D0-\u05EA]/
const WHITESPACE = /[\t\n\r ]/

/**
 * Produces a clean plain-text export of an HTML string.
 *
 * Rules applied on top of applyDiacriticsFilter state 2
 * (which already strips nikkud, teamim, em dash, and normalises ! ? ;):
 *
 *  - Colons stripped unless immediately preceding an HTML tag
 *  - Double-quotes (literal '"' U+0022, Hebrew ״ U+05F4, and HTML entity &quot;)
 *    stripped when at a word boundary (start or end of word); mid-word kept
 *    so that gershayim like ר"ל and abbreviations like רמב״ם are preserved
 *  - A space is inserted after '.' when it is followed directly by a Hebrew
 *    letter, unless it is preceded by a single Hebrew letter (e.g. ב. א.)
 *    which signals a structural label, not a sentence boundary
 *  - Multiple consecutive spaces collapsed to one
 *
 * All rules are applied in a single character-by-character pass over the
 * string returned by applyDiacriticsFilter, skipping tag content entirely.
 */
export function cleanTextForExport(html: string): string {
  const source = applyDiacriticsFilter(html, 2)
  const length = source.length
  const output: string[] = []

  let insideTag = false

  for (let index = 0; index < length; index++) {
    const character = source[index]!

    // ── Tag tracking — never process content inside < … > ────────────────────
    if (character === '<') {
      insideTag = true
      output.push(character)
      continue
    }
    if (character === '>') {
      insideTag = false
      output.push(character)
      continue
    }
    if (insideTag) {
      output.push(character)
      continue
    }

    // ── Colon ─────────────────────────────────────────────────────────────────
    // Strip ':' unless the next non-whitespace character opens an HTML tag.
    if (character === ':') {
      let lookahead = index + 1
      while (lookahead < length && source[lookahead] === ' ') lookahead++
      if (lookahead < length && source[lookahead] === '<') {
        output.push(character)
      }
      // Otherwise drop — no continue needed, fall through handled by not pushing
      continue
    }

    // ── Double-quote ──────────────────────────────────────────────────────────
    // Strip ASCII '"' (U+0022) and Hebrew gershayim ״ (U+05F4) at word boundaries,
    // including when the ASCII form is encoded as the HTML entity &quot;.
    // A quote is at a boundary when preceded OR followed by whitespace / tag edge /
    // start or end of string. Mid-word quotes (non-space on both sides) are kept
    // so that gershayim like ר"ל and abbreviations like רמב״ם survive.
    const isLiteralQuote = character === '"' || character === '\u05F4'
    const isEntityQuote = !isLiteralQuote &&
      source.startsWith('&quot;', index)

    if (isLiteralQuote || isEntityQuote) {
      const quoteEnd = isEntityQuote ? index + 6 : index + 1

      const previousCharacter = index > 0 ? source[index - 1]! : null
      const nextCharacter = quoteEnd < length ? source[quoteEnd]! : null

      const precededByBoundary =
        previousCharacter === null ||
        WHITESPACE.test(previousCharacter) ||
        previousCharacter === '>' ||
        previousCharacter === ',' ||
        previousCharacter === '.' ||
        previousCharacter === '"' ||
        previousCharacter === '\u05F4'

      const followedByBoundary =
        nextCharacter === null ||
        WHITESPACE.test(nextCharacter) ||
        nextCharacter === '<' ||
        nextCharacter === ',' ||
        nextCharacter === '.' ||
        nextCharacter === '"' ||
        nextCharacter === '\u05F4'

      if (precededByBoundary || followedByBoundary) {
        if (isEntityQuote) index += 5 // skip remaining 5 chars of &quot; (loop adds 1)
        continue
      }
      if (isEntityQuote) {
        output.push('"')
        index += 5
      } else {
        output.push(character)
      }
      continue
    }

    // ── Period — insert trailing space when needed ────────────────────────────
    // After stripping nikkud, sentences that ended with a period can be directly
    // concatenated with the next word (e.g. "אליעזר.וחכמים"). Insert a space
    // after '.' when the next character is a Hebrew letter, UNLESS the period
    // follows a single Hebrew letter (structural label like "ב." or "א.").
    if (character === '.') {
      output.push(character)

      const nextCharacter = index < length - 1 ? source[index + 1]! : null
      if (nextCharacter !== null && HEBREW_LETTER.test(nextCharacter)) {
        // Check whether the period follows a single Hebrew letter label
        const previousCharacter = index > 0 ? source[index - 1]! : null
        const twoBack = index > 1 ? source[index - 2]! : null
        const precedingIsSingleLetter =
          previousCharacter !== null &&
          HEBREW_LETTER.test(previousCharacter) &&
          (twoBack === null || WHITESPACE.test(twoBack) || twoBack === '>')

        if (!precedingIsSingleLetter) {
          output.push(' ')
        }
      }
      continue
    }

    // ── Collapse multiple spaces ───────────────────────────────────────────────
    // The em-dash stripped by state 2 can leave two adjacent spaces. Suppress
    // any space when the previous output character is already a space.
    if (character === ' ') {
      const lastOutput = output.length > 0 ? output[output.length - 1] : null
      if (lastOutput !== ' ') output.push(character)
      continue
    }

    output.push(character)
  }

  return output.join('')
}
