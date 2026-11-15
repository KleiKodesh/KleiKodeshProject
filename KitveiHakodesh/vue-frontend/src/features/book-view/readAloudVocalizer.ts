import type { ReadAloudText } from './readAloudSkip'

/**
 * Vocalizes (adds nikkud to) the text read aloud, so the voice reads unpointed Hebrew as if
 * it had nikkud. Uses Dicta's Nakdan, the same service and endpoints as the Word add-in.
 *
 * Built to never get in the reading's way:
 *  - Failure is silent: any error, timeout or blocked request means the line is read as it is.
 *  - Offline, or a recent failure, returns at once: no request is made until a pause has passed.
 *  - Economical: only upcoming lines that need it are sent (no Hebrew, or already pointed,
 *    is skipped), in a few batched requests, one at a time, never the same line twice.
 *  - Balanced with speed: how far ahead it looks grows with the speed, so faster reading
 *    still finds its lines ready; a line that is not ready in a moment is read plain.
 *  - Nikkud is dropped as soon as a line is done, and everything on stop.
 *
 * Nothing is written to disk: the pointed text lives in memory for the lines ahead only.
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

/** Roughly how many characters a voice reads per second at speed 1. */
const CHARS_PER_SECOND = 14
/** How many seconds of reading to keep vocalized ahead. */
const LOOKAHEAD_SECONDS = 25
const MAX_REQUEST_CHARS = 1800
const REQUEST_TIMEOUT_MS = 8000
/** After a failed request, none is tried for this long. */
const BACKOFF_MS = 60_000
/** How long a line about to be read waits for its vocalization before reading plain. */
const WAIT_FOR_LINE_MS = 400

const HEBREW_LETTER = /[א-ת]/g
const MARK = /[֑-ׇ]/g
const IS_MARK = /[֑-ׇ]/

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

/** Dicta's answer as plain pointed text: the first option of each word. */
function pointedTextOf(data: unknown): string | null {
  if (!Array.isArray(data)) return null
  let out = ''
  for (const item of data) {
    const str: unknown = item?.str
    const first: unknown = item?.nakdan?.options?.[0]?.w
    if (typeof first === 'string' && first) out += first.replace(/\|/g, '').replace(/ֽ/g, '')
    else if (typeof str === 'string') out += str
  }
  return out
}

async function postToDicta(text: string, signal: AbortSignal): Promise<string | null> {
  const body = JSON.stringify({ ...NAKDAN_SETTINGS, data: text })
  for (const url of ENDPOINTS) {
    if (signal.aborted) return null
    try {
      const timeout = AbortSignal.timeout(REQUEST_TIMEOUT_MS)
      const response = await fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body,
        signal: AbortSignal.any([signal, timeout]),
      })
      if (!response.ok) continue
      const pointed = pointedTextOf((await response.json())?.data)
      if (pointed) return pointed
    } catch {
      if (signal.aborted) return null
      if (typeof navigator !== 'undefined' && navigator.onLine === false) return null
    }
  }
  return null
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
      toFull.push(toFull[toFull.length - 1] ?? text.toFull[0])
      if (source < text.spoken.length && text.spoken[source] === ch) source++
    } else {
      toFull.push(text.toFull[source])
      source++
    }
    spoken += ch
  }
  toFull.push(text.toFull[text.spoken.length])
  return { full: text.full, spoken, toFull }
}

export function createReadAloudVocalizer() {
  /** spoken text -> pointed text. */
  const ready = new Map<string, string>()
  /** Lines in a request now in flight, with that request's end. */
  const inFlight = new Map<string, Promise<void>>()
  /** Lines Dicta could not point, not asked again. */
  const failed = new Set<string>()
  let controller = new AbortController()
  let blockedUntil = 0
  let busy = false

  function available(): boolean {
    if (typeof navigator !== 'undefined' && navigator.onLine === false) return false
    return Date.now() >= blockedUntil
  }

  async function send(lines: string[], signal: AbortSignal) {
    try {
      const pointed = await postToDicta(lines.join('\n'), signal)
      if (signal.aborted) return
      const parts = pointed?.split('\n')
      if (!parts || parts.length !== lines.length) {
        // The whole batch failed; a bad request or no connection pauses further tries.
        for (const line of lines) failed.add(line)
        if (!pointed) blockedUntil = Date.now() + BACKOFF_MS
        return
      }
      lines.forEach((line, i) => {
        if (stripMarks(parts[i]) === stripMarks(line)) ready.set(line, parts[i])
        else failed.add(line)
      })
    } catch {
      for (const line of lines) failed.add(line)
      blockedUntil = Date.now() + BACKOFF_MS
    }
  }

  /**
   * Asks for the upcoming lines (in reading order) that are not yet pointed, as many as
   * the reading speed needs for the next while. One request at a time.
   */
  function prefetch(upcoming: string[], rate: number) {
    if (busy || !available()) return
    const budget = Math.min(MAX_REQUEST_CHARS, CHARS_PER_SECOND * LOOKAHEAD_SECONDS * Math.max(0.5, rate))
    const lines: string[] = []
    let chars = 0
    for (const line of upcoming) {
      if (chars >= budget) break
      if (ready.has(line) || failed.has(line) || inFlight.has(line)) { chars += line.length; continue }
      if (!needsVocalizing(line)) continue
      if (lines.length > 0 && chars + line.length > MAX_REQUEST_CHARS) break
      lines.push(line)
      chars += line.length
    }
    if (lines.length === 0) return
    busy = true
    const signal = controller.signal
    const request = send(lines, signal).finally(() => {
      busy = false
      for (const line of lines) inFlight.delete(line)
    })
    for (const line of lines) inFlight.set(line, request)
  }

  /** The line pointed, if it is ready - or soon: waits a moment for a request already on its way. */
  async function pointed(text: ReadAloudText): Promise<ReadAloudText> {
    try {
      const key = text.spoken
      const pending = inFlight.get(key)
      if (pending && !ready.has(key)) {
        await Promise.race([pending, new Promise((resolve) => setTimeout(resolve, WAIT_FOR_LINE_MS))])
      }
      const value = ready.get(key)
      return (value && withVocalization(text, value)) || text
    } catch {
      return text
    }
  }

  /** The line is done: its nikkud is dropped. */
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
  }

  return { prefetch, pointed, release, clear }
}

export type ReadAloudVocalizer = ReturnType<typeof createReadAloudVocalizer>
