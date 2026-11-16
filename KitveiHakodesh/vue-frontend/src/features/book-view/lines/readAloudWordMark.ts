/**
 * The sentence being read aloud, marked in its rendered line like a search match:
 * <mark class="kh-read-aloud"> around it. It is found by counting LETTERS (\p{L}\p{N}),
 * the way the read-aloud position counts them in the line's plain text, so markup, vowel
 * points and punctuation in the row never shift it. The book's text is shown as it is.
 */
export interface ReadAloudWordMark {
  lineIndex: number
  /** How many letters of the line come before the sentence. */
  letterStart: number
  letterCount: number
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
  if (!/</.test(inner)) return `${before}${OPEN}${inner}${CLOSE}${after}`
  // Markup inside the word: the mark is closed around each tag, so the HTML stays well formed.
  const wrapped = `${OPEN}${inner.replace(/<[^>]*>/g, (tag) => `${CLOSE}${tag}${OPEN}`)}${CLOSE}`
  return before + wrapped.split(`${OPEN}${CLOSE}`).join('') + after
}
