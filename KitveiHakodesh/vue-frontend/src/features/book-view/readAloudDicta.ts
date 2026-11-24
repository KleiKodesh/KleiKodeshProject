import type { ReadAloudText } from './readAloudSkip'

/**
 * Prepares the text read aloud with Dicta, so the voice reads it better:
 *  1. Abbreviations (ראשי תיבות) are spoken in full - Dicta's abbreviation expander.
 *  2. Unpointed Hebrew gets nikkud - Dicta's Nakdan, as in the Word add-in.
 * Both are tasks of the same Dicta endpoint; each can be switched off on its own.
 *
 * Built to never get in the reading's way:
 *  - Failure is silent: any error, timeout or blocked request means the line is read as it is.
 *  - Offline, or a recent failure, returns at once: no request is made until a pause has passed.
 *  - Economical: only upcoming lines that need it are sent (no Hebrew, already pointed, or -
 *    for abbreviations - no abbreviation mark, is skipped), in a few batched requests, one at
 *    a time, never the same line twice.
 *  - Balanced with speed: how far ahead it looks grows with the speed, so faster reading
 *    still finds its lines ready; a line that is not ready in a moment is read plain.
 *  - What Dicta gave for a line is dropped as soon as the line is done, and everything on stop.
 *
 * Nothing is written to disk: the prepared text lives in memory for the lines ahead only.
 */

const ENDPOINTS = [
  'https://nakdan-u2-1a.loadbalancer.dicta.org.il/api',
  'https://nakdan-u1-0.loadbalancer.dicta.org.il/api',
  'https://nakdan-5-1.loadbalancer.dicta.org.il/api',
]

/** Settings for our corpus: the seforim - rabbinic Hebrew. Morphology isn't needed. */
const NAKDAN_SETTINGS = {
  task: 'nakdan',
  genre: 'rabbinic',
  addmorph: false,
  keepmetagim: true,
  useTokenization: true,
  keepqq: false,
  nodageshdefmem: false,
  patachma: false,
  matchpartial: true,
  userData: 'gave permission',
}

const ABBREVIATION_SETTINGS = {
  task: 'abbrexp',
  genre: 'rabbinic',
  useTokenization: true,
  userData: 'gave permission',
}

/** What to prepare; both can be on. */
export interface DictaOptions {
  abbreviations: boolean
  nikkud: boolean
}

/** Roughly how many characters a voice reads per second at speed 1. */
const CHARS_PER_SECOND = 14
/** How many seconds of reading to keep prepared ahead. */
const LOOKAHEAD_SECONDS = 25
const MAX_REQUEST_CHARS = 4000
const REQUEST_TIMEOUT_MS = 8000
/** After a failed request, none is tried for this long. */
const BACKOFF_MS = 60_000
/** How long a line about to be read waits for its preparation before reading plain. */
const WAIT_FOR_LINE_MS = 150
/** Reading starts at once: the first line is read as it is if Dicta has not answered yet. */
export const WAIT_FOR_FIRST_LINE_MS = 0

const HEBREW_LETTER = /[א-ת]/g
const MARK = /[֑-ׇ]/g
const IS_MARK = /[֑-ׇ]/
/** Geresh, gershayim, apostrophe, quote: what an abbreviation is written with. */
const ABBREVIATION_MARK = /[׳״'"]/

/** Whether the text is Hebrew with (almost) no points - worth sending. */
function needsVocalizing(text: string): boolean {
  const letters = text.match(HEBREW_LETTER)?.length ?? 0
  if (letters < 2 || text.includes('\n')) return false
  const marks = text.match(MARK)?.length ?? 0
  return marks < letters * 0.3
}

function stripMarks(text: string): string {
  return text.replace(MARK, '')
}

async function postToDicta(settings: object, text: string, signal: AbortSignal): Promise<unknown[] | null> {
  const body = JSON.stringify({ ...settings, data: text })
  for (const url of ENDPOINTS) {
    if (signal.aborted) return null
    try {
      const response = await fetch(url, {
        method: 'POST',
        // A "simple" request on purpose: Dicta's server fails the CORS preflight that a JSON
        // content type triggers (500), so the browser would block every call. It reads the
        // JSON body just the same as text, as Dicta's own site sends it.
        headers: { 'Content-Type': 'text/plain;charset=UTF-8' },
        body,
        signal: AbortSignal.any([signal, AbortSignal.timeout(REQUEST_TIMEOUT_MS)]),
      })
      if (!response.ok) continue
      const data = (await response.json())?.data
      if (Array.isArray(data)) return data
    } catch {
      if (signal.aborted) return null
      if (typeof navigator !== 'undefined' && navigator.onLine === false) return null
    }
  }
  return null
}

interface DictaToken {
  str?: string
  pStr?: string
  abbreviation?: { word?: string; options?: string[] }
  nakdan?: { options?: { w?: string }[] }
}

/** The tokens of a batch sent as lines joined by newlines, grouped back per line. */
function tokensPerLine(data: unknown[], lineCount: number): DictaToken[][] | null {
  const groups: DictaToken[][] = [[]]
  for (const item of data as DictaToken[]) {
    const str = item?.str ?? ''
    if (!str.includes('\n')) {
      groups[groups.length - 1]!.push(item)
      continue
    }
    // Dicta glues punctuation and spaces to the newline (". \n"): split the token there,
    // each side keeping its own plain text.
    const parts = str.split('\n')
    parts.forEach((part, i) => {
      if (i > 0) groups.push([])
      if (part) groups[groups.length - 1]!.push({ str: part, pStr: part })
    })
  }
  return groups.length === lineCount ? groups : null
}

export const tokensPerLineForTest = tokensPerLine

/** Nikkud text of a line's tokens: the first option of each word. */
function pointedTextOf(tokens: DictaToken[]): string {
  let out = ''
  for (const token of tokens) {
    const first = token.nakdan?.options?.[0]?.w
    if (first) out += first.replace(/\|/g, '').replace(/ֽ/g, '')
    else out += token.str ?? ''
  }
  return out
}

/** A stretch of a line's spoken text, and what it is spoken as (an abbreviation in full, a number by its letters). */
export interface Expansion {
  start: number
  end: number
  text: string
}

/** Shortened words Dicta ranks badly, with what they stand for. */
const KNOWN_SHORTENINGS: Record<string, string> = {
  'וגו': 'וגומר',
  'וכו': 'וכולי',
}

const FINAL_TO_BASE: Record<string, string> = { 'ך': 'כ', 'ם': 'מ', 'ן': 'נ', 'ף': 'פ', 'ץ': 'צ' }

/** The abbreviation's letters, without its geresh / gershayim, finals as plain letters. */
function lettersOf(abbreviation: string): string {
  return [...abbreviation.replace(/[׳״'"]/g, '')].map((ch) => FINAL_TO_BASE[ch] ?? ch).join('')
}

/** Whether the words begin with the letters in order, each word taking one or more of them (הקב"ה = הקדוש ברוך הוא). */
function wordsMatchLetters(letters: string, words: string[]): boolean {
  if (words.length === 0) return letters.length === 0
  const word = [...words[0]!].map((ch) => FINAL_TO_BASE[ch] ?? ch).join('')
  for (let k = 1; k <= letters.length; k++) {
    if (!word.startsWith(letters.slice(0, k))) break
    if (wordsMatchLetters(letters.slice(k), words.slice(1))) return true
  }
  return false
}

/**
 * What an abbreviation is spoken as, or null to read it as written.
 * Dicta's first option is taken unless it is the abbreviation itself - its way of saying
 * "not sure". Then the first option whose
 * words begin with the abbreviation's letters is taken (רשב"י = רבי שמעון בר יוחאי).
 * A few shortened words Dicta ranks badly come from a short list (וגו' = וגומר).
 */
export function chooseExpansion(abbreviation: string, options: string[]): string | null {
  const letters = lettersOf(abbreviation)
  const isSelf = (option: string) => lettersOf(option) === letters
  const known = KNOWN_SHORTENINGS[letters]
  if (known && /[׳']$/.test(abbreviation)) return known
  const first = options[0]
  if (!first) return null
  if (!isSelf(first)) return first
  return options.find((option) => !isSelf(option) && wordsMatchLetters(letters, option.trim().split(/\s+/))) ?? null
}

/** The expansions Dicta chose for a line; null if its tokens do not match the line. */
function expansionsOf(line: string, tokens: DictaToken[]): Expansion[] | null {
  const found: Expansion[] = []
  let pos = 0
  for (const token of tokens) {
    const str = token.str ?? ''
    if (!line.startsWith(str, pos)) return null
    const best = token.abbreviation ? chooseExpansion(str, token.abbreviation.options ?? []) : null
    if (best) {
      // וגו' / וכו' close a quotation: a comma after them gives the voice its pause,
      // unless the text already has a mark there.
      const pause = KNOWN_SHORTENINGS[lettersOf(str)] && !/^[.,:;?!]/.test(line.slice(pos + str.length)) ? ',' : ''
      found.push({ start: pos, end: pos + str.length, text: best + pause })
    }
    pos += str.length
  }
  return pos === line.length ? found : null
}

/**
 * The text with its abbreviations in full. Each letter of an expansion maps to a letter of
 * the abbreviation it came from, so the highlight stays on the word shown in the row.
 */
export function withExpansions(text: ReadAloudText, expansions: Expansion[]): ReadAloudText {
  if (expansions.length === 0) return text
  let spoken = ''
  const toFull: number[] = []
  let pos = 0
  const copyUntil = (end: number) => {
    for (; pos < end; pos++) {
      spoken += text.spoken[pos]
      toFull.push(text.toFull[pos]!)
    }
  }
  for (const e of expansions) {
    copyUntil(e.start)
    const span = e.end - e.start
    for (let j = 0; j < e.text.length; j++) {
      spoken += e.text[j]
      toFull.push(text.toFull[e.start + Math.min(j, span - 1)]!)
    }
    pos = e.end
  }
  copyUntil(text.spoken.length)
  toFull.push(text.toFull[text.spoken.length]!)
  return { full: text.full, spoken, toFull }
}

/**
 * The line's spoken text with the points in it, and the map back to the full text kept:
 * a point maps to the letter it stands on. Null when Dicta's letters differ from ours.
 */
export function withVocalization(text: ReadAloudText, pointed: string): ReadAloudText | null {
  if (stripMarks(pointed) !== stripMarks(text.spoken)) return null
  const toFull: number[] = []
  let source = 0
  let spoken = ''
  for (const ch of pointed) {
    if (IS_MARK.test(ch)) {
      // A mark Dicta added (or one already there) rides on the letter before it.
      toFull.push(toFull[toFull.length - 1] ?? text.toFull[0]!)
      if (source < text.spoken.length && text.spoken[source] === ch) source++
    } else {
      // A mark of ours that Dicta dropped is passed over, so the letter keeps its place.
      while (source < text.spoken.length && IS_MARK.test(text.spoken[source]!)) source++
      toFull.push(text.toFull[source]!)
      source++
    }
    spoken += ch
  }
  toFull.push(text.toFull[text.spoken.length]!)
  return { full: text.full, spoken, toFull }
}

/** What Dicta gave for a line, and which options it was asked with. */
interface Prepared {
  options: DictaOptions
  expansions: Expansion[]
  /** The expanded text with nikkud; absent when nikkud was not asked for. */
  pointed?: string
}

const sameOptions = (a: DictaOptions, b: DictaOptions) => a.abbreviations === b.abbreviations && a.nikkud === b.nikkud

export function createReadAloudDicta() {
  /** spoken text -> what Dicta gave for it. */
  const ready = new Map<string, Prepared>()
  /** Lines in a request now in flight, with that request's end. */
  const inFlight = new Map<string, Promise<void>>()
  /** Lines Dicta could not prepare, not asked again. */
  const failed = new Set<string>()
  let controller = new AbortController()
  let blockedUntil = 0
  let busy = false

  function available(): boolean {
    if (typeof navigator !== 'undefined' && navigator.onLine === false) return false
    return Date.now() >= blockedUntil
  }

  function worthPreparing(line: string, options: DictaOptions): boolean {
    return (options.nikkud && needsVocalizing(line)) || (options.abbreviations && ABBREVIATION_MARK.test(line) && needsVocalizing(line))
  }

  /** Stage 1: the abbreviations of each line that has any mark. Null if Dicta could not be reached. */
  async function expandAbbreviations(lines: string[], signal: AbortSignal): Promise<Expansion[][] | null> {
    const marked = lines.map((line) => ABBREVIATION_MARK.test(line))
    const toSend = lines.filter((_, i) => marked[i])
    const result: Expansion[][] = lines.map(() => [])
    if (toSend.length === 0) return result
    const data = await postToDicta(ABBREVIATION_SETTINGS, toSend.join('\n'), signal)
    if (!data) return null
    const groups = tokensPerLine(data, toSend.length)
    if (!groups) return result // an answer that does not line up: read the lines as they are
    let k = 0
    lines.forEach((line, i) => {
      if (marked[i]) result[i] = expansionsOf(line, groups[k++]!) ?? []
    })
    return result
  }

  /** Stage 2: nikkud for each line's expanded text. Null if Dicta could not be reached. */
  async function addNikkud(expandedLines: string[], signal: AbortSignal): Promise<(string | undefined)[] | null> {
    const data = await postToDicta(NAKDAN_SETTINGS, expandedLines.join('\n'), signal)
    if (!data) return null
    const groups = tokensPerLine(data, expandedLines.length)
    if (!groups) return expandedLines.map(() => undefined)
    return expandedLines.map((line, i) => {
      const pointed = pointedTextOf(groups[i]!)
      return stripMarks(pointed) === stripMarks(line) ? pointed : undefined
    })
  }

  async function send(lines: string[], options: DictaOptions, signal: AbortSignal) {
    try {
      const expansions = options.abbreviations ? await expandAbbreviations(lines, signal) : lines.map(() => [] as Expansion[])
      if (signal.aborted) return
      if (!expansions) throw new Error('unreachable')
      let pointed: (string | undefined)[] = lines.map(() => undefined)
      if (options.nikkud) {
        const expanded = lines.map((line, i) => withExpansions({ full: line, spoken: line, toFull: [...line].map((_, k) => k).concat(line.length) }, expansions[i]!).spoken)
        const result = await addNikkud(expanded, signal)
        if (signal.aborted) return
        if (!result) throw new Error('unreachable')
        pointed = result
      }
      lines.forEach((line, i) => ready.set(line, { options, expansions: expansions[i]!, pointed: pointed[i] }))
    } catch {
      // Not reachable: the lines are read as they are, and Dicta is left alone for a while.
      for (const line of lines) failed.add(line)
      blockedUntil = Date.now() + BACKOFF_MS
    }
  }

  /**
   * Asks for the upcoming lines (in reading order) that are not yet prepared, as many as
   * the reading speed needs for the next while. One request at a time.
   */
  /** The last prefetch call, so a finished request can carry straight on with the next lines. */
  let lastAsk: { upcoming: string[]; rate: number; options: DictaOptions } | null = null

  function prefetch(upcoming: string[], rate: number, options: DictaOptions) {
    lastAsk = { upcoming, rate, options }
    if (busy || !available() || (!options.abbreviations && !options.nikkud)) return
    // Look ahead by reading time, but always at least the line being read and the next one,
    // however long they are - otherwise long lines are only asked for when already due.
    const budget = Math.max(
      CHARS_PER_SECOND * LOOKAHEAD_SECONDS * Math.max(0.5, rate),
      (upcoming[0]?.length ?? 0) + (upcoming[1]?.length ?? 0),
    )
    const lines: string[] = []
    let chars = 0
    let requestChars = 0
    for (const line of upcoming) {
      if (chars >= budget) break
      chars += line.length
      const have = ready.get(line)
      if ((have && sameOptions(have.options, options)) || failed.has(line) || inFlight.has(line)) continue
      if (!worthPreparing(line, options)) continue
      // One request stays a sensible size; the rest goes in the next one, right after.
      if (lines.length > 0 && requestChars + line.length > MAX_REQUEST_CHARS) break
      lines.push(line)
      requestChars += line.length
    }
    if (lines.length === 0) return
    busy = true
    const mine = controller
    const request = send(lines, options, mine.signal).finally(() => {
      // After a stop the state belongs to a newer run; leave it alone.
      if (mine !== controller) return
      busy = false
      for (const line of lines) inFlight.delete(line)
      if (lastAsk) prefetch(lastAsk.upcoming, lastAsk.rate, lastAsk.options)
    })
    for (const line of lines) inFlight.set(line, request)
  }

  /** The line as it should be spoken, if Dicta's answer is ready - or soon: waits a moment for a request already on its way. */
  async function prepared(text: ReadAloudText, options: DictaOptions, waitMs = WAIT_FOR_LINE_MS): Promise<ReadAloudText> {
    try {
      const key = text.spoken
      const pending = inFlight.get(key)
      if (pending && !ready.has(key)) {
        await Promise.race([pending, new Promise((resolve) => setTimeout(resolve, waitMs))])
      }
      const have = ready.get(key)
      if (!have || !sameOptions(have.options, options)) return text
      const expanded = withExpansions(text, have.expansions)
      return (have.pointed && withVocalization(expanded, have.pointed)) || expanded
    } catch {
      return text
    }
  }

  /** The line is done: what Dicta gave for it is dropped. */
  function release(text: ReadAloudText) {
    ready.delete(text.spoken)
    failed.delete(text.spoken)
  }

  /** Reading stopped: everything is dropped and requests on their way are cancelled. */
  function clear() {
    controller.abort()
    controller = new AbortController()
    ready.clear()
    inFlight.clear()
    failed.clear()
    busy = false
    lastAsk = null
  }

  return { prefetch, prepared, release, clear }
}

export type ReadAloudDicta = ReturnType<typeof createReadAloudDicta>
