/**
 * The word being read aloud, marked in its rendered line like a search match:
 * <mark class="kh-read-aloud"> around it. A word is found by counting LETTERS (\p{L}\p{N}),
 * the way the read-aloud position counts them in the line's plain text, so markup, vowel
 * points and punctuation in the row never shift it.
 *
 * When the spoken word is the shown word with nikkud added (Dicta), the word is written
 * with that nikkud, the points in their own spans (.kh-read-aloud-points). Those spans are
 * not selectable, so a copy carries the book's own text only. (They cannot be drawn gray:
 * Chromium paints a combining mark in its letter's color - tested, red spans drew black.)
 */
export interface ReadAloudWordMark {
  lineIndex: number
  /** How many letters of the line come before the word. */
  letterStart: number
  letterCount: number
  /** The word as spoken - with nikkud when Dicta added it. */
  spokenWord: string
}

const LETTER = /[\p{L}\p{N}]/u
const MARK = /\p{M}/u
const OPEN = '<mark class="kh-read-aloud">'
const CLOSE = '</mark>'

/** The end of the entity at `i` ('&...;'), or -1 when it is a bare '&'. */
function entityEnd(html: string, i: number): number {
  for (let j = i + 1; j < html.length && j <= i + 12; j++) {
    const c = html[j]!
    if (c === ';') return j
    if (c === ' ' || c === '\t' || c === '\n' || c === '<') return -1
  }
  return -1
}

const stripMarks = (s: string) => s.replace(/\p{M}/gu, '')

/** The spoken word with its points in spans: letters as they are, each run of points wrapped. */
function pointedWordHtml(word: string): string {
  let out = ''
  let points = ''
  const flush = () => {
    if (points) out += `<span class="kh-read-aloud-points">${points}</span>`
    points = ''
  }
  for (const ch of word) {
    if (MARK.test(ch)) points += ch
    else {
      flush()
      out += ch
    }
  }
  flush()
  return out
}

/** Finds the word's [start, end) in the HTML: from its first letter to past its last letter's points. */
function wordRange(html: string, letterStart: number, letterCount: number): { start: number; end: number } | null {
  let seen = 0
  let start = -1
  let inTag = false
  for (let i = 0; i < html.length; i++) {
    const ch = html[i]!
    if (ch === '<') { inTag = true; continue }
    if (ch === '>') { inTag = false; continue }
    if (inTag) continue
    if (ch === '&') {
      const end = entityEnd(html, i)
      if (end !== -1) { i = end; continue }
    }
    if (!LETTER.test(ch)) continue
    if (seen === letterStart) start = i
    if (seen === letterStart + letterCount - 1) {
      let end = i + 1
      while (end < html.length && MARK.test(html[end]!)) end++
      return start < 0 ? null : { start, end }
    }
    seen++
  }
  return null
}

export function markReadAloudWord(html: string, word: ReadAloudWordMark): string {
  if (word.letterCount <= 0) return html
  const range = wordRange(html, word.letterStart, word.letterCount)
  if (!range) return html
  const inner = html.slice(range.start, range.end)
  const before = html.slice(0, range.start)
  const after = html.slice(range.end)
  // Letters and points only: the voice's word can carry punctuation the mark does not cover.
  const spoken = word.spokenWord.replace(/[^\p{L}\p{M}\p{N}]/gu, '')
  const plain = !/[<&]/.test(inner)
  // The same letters with nikkud: write the pointed word in.
  if (plain && spoken && spoken !== inner && /\p{M}/u.test(spoken) && stripMarks(spoken) === stripMarks(inner)) {
    return `${before}${OPEN}${pointedWordHtml(spoken)}${CLOSE}${after}`
  }
  if (plain) return `${before}${OPEN}${inner}${CLOSE}${after}`
  // Markup inside the word: the mark is closed around each tag, so the HTML stays well formed.
  const wrapped = `${OPEN}${inner.replace(/<[^>]*>/g, (tag) => `${CLOSE}${tag}${OPEN}`)}${CLOSE}`
  return before + wrapped.split(`${OPEN}${CLOSE}`).join('') + after
}
