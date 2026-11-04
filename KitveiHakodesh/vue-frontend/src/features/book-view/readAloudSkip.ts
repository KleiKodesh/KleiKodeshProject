/**
 * What read aloud leaves out, and the text it speaks once that is gone.
 *
 * The line keeps its FULL text alongside the spoken one, and a map from each spoken
 * character back to its place in the full text: the highlight finds the word by counting
 * letters in the rendered row, which still shows everything that was skipped.
 */

export interface ReadAloudSkipOptions {
  /** Inline source links (Zayit's מראי מקום, and any other link in the text). */
  links: boolean
  /** Footnote and note markers - superscript numbers and letters. */
  noteMarkers: boolean
  /** Everything in round brackets ( ). */
  parentheses: boolean
  /** Everything in square brackets [ ]. */
  squareBrackets: boolean
  /** Words of a single letter - section numbering and the like. */
  singleLetterWords: boolean
  /** Heading lines - chapter and section titles. */
  headings: boolean
}

export const DEFAULT_SKIP_OPTIONS: ReadAloudSkipOptions = {
  links: true,
  noteMarkers: true,
  parentheses: false,
  squareBrackets: false,
  singleLetterWords: false,
  headings: false,
}

export const SKIP_OPTION_KEYS = Object.keys(DEFAULT_SKIP_OPTIONS) as (keyof ReadAloudSkipOptions)[]

export interface ReadAloudText {
  /** The line's whole plain text, as the row shows it. */
  full: string
  /** What is spoken. */
  spoken: string
  /** spoken[i] is full[toFull[i]]; one extra entry maps the end. */
  toFull: number[]
}

const htmlParser = new DOMParser()

/** A word of one letter (vowel points and cantillation riding on it count with it). */
const SINGLE_LETTER_WORD = /(?<![\p{L}\p{M}'"״׳])\p{L}\p{M}*(?![\p{L}\p{M}'"״׳])/gu

function maskMatches(text: string, keep: boolean[], pattern: RegExp) {
  for (const match of text.matchAll(pattern)) {
    for (let i = match.index!; i < match.index! + match[0].length; i++) keep[i] = false
  }
}

function build(full: string, keep: boolean[], options: ReadAloudSkipOptions | null): ReadAloudText {
  if (options?.parentheses) maskMatches(full, keep, /\([^()]*\)/g)
  if (options?.squareBrackets) maskMatches(full, keep, /\[[^[\]]*\]/g)
  if (options?.singleLetterWords) maskMatches(full, keep, SINGLE_LETTER_WORD)
  let spoken = ''
  const toFull: number[] = []
  for (let i = 0; i < full.length; i++) {
    if (!keep[i]) continue
    spoken += full[i]
    toFull.push(i)
  }
  toFull.push(full.length)
  return { full, spoken, toFull }
}

/** A line's HTML as read-aloud text. `options` null = skip nothing. */
export function readAloudTextOfLine(html: string, options: ReadAloudSkipOptions | null): ReadAloudText {
  const body = htmlParser.parseFromString(html, 'text/html').body
  let full = ''
  const keep: boolean[] = []
  const walker = document.createTreeWalker(body, NodeFilter.SHOW_TEXT)
  for (let node = walker.nextNode(); node; node = walker.nextNode()) {
    const parent = node.parentElement
    const skipped =
      !!options &&
      ((options.links && !!parent?.closest('a')) ||
        (options.noteMarkers && !!parent?.closest('sup')) ||
        (options.headings && !!parent?.closest('h1,h2,h3,h4,h5,h6')))
    const data = (node as Text).data
    full += data
    for (let i = 0; i < data.length; i++) keep.push(!skipped)
  }
  return build(full, keep, options)
}

/** Selected text (already plain) as read-aloud text. */
export function readAloudTextOfPlain(text: string, options: ReadAloudSkipOptions | null): ReadAloudText {
  return build(text, new Array<boolean>(text.length).fill(true), options)
}
