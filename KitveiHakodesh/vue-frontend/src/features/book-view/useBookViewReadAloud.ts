import { onScopeDispose, ref, shallowRef } from 'vue'
import { useStorage } from '@vueuse/core'
import { getLinesPaged } from '@/webview-host/seforimApi'
import {
  DEFAULT_SKIP_OPTIONS,
  readAloudTextOfLine,
  readAloudTextOfPlain,
  type ReadAloudSkipOptions,
  type ReadAloudText,
} from './readAloudSkip'
import { createReadAloudDicta, WAIT_FOR_FIRST_LINE_MS } from './readAloudDicta'
import { withSpelledHeadingNumbers } from './readAloudHeadingNumbers'
import { withDivineNamesSpoken } from './readAloudDivineNames'
import { createGoogleVoice, GOOGLE_MAX_CHUNK_CHARS, GOOGLE_VOICE_URI } from './readAloudGoogleVoice'

/**
 * Reads the book aloud with the browser's own speech (the voices installed in Windows).
 *
 * Reads the selected text when there is one; otherwise the book from a given line on,
 * fetching its own small batches so it never holds more than one batch of text.
 *
 * Text goes to the voice a sentence at a time: Chromium cuts off one long utterance part
 * way through. All of a line's sentences are queued at once, so there is no pause between
 * them; stop / skip cancel the queue at once.
 *
 * The Google voice (online) is offered too; when it cannot be reached the Windows voice reads.
 *
 * `position` says which line and which word is being spoken now, for the highlight and
 * for following along; it is null while reading a selection (that text has no line).
 */
const BATCH_SIZE = 20
const MAX_CHUNK_CHARS = 200
/** Both Dicta preparations are always on; when Dicta is not reachable the text is read as it is. */
const DICTA_ALWAYS = { abbreviations: true, nikkud: true }

/** Speed steps: 0.1 apart, 0.5 to 3. */
export const READ_ALOUD_RATE_STEP = 0.1
export const READ_ALOUD_RATE_MIN = 0.5
export const READ_ALOUD_RATE_MAX = 3

export interface ReadAloudPosition {
  lineIndex: number
  /** The line's whole plain text, skipped parts included - what the row shows. */
  text: string
  /** The word being spoken, as a range in `text`; -1 before the first word boundary. */
  wordStart: number
  wordEnd: number
  /** The sentence being spoken (the voice's chunk), as a range in `text`; -1 before the first word. */
  sentenceStart: number
  sentenceEnd: number
}

/**
 * The voices read aloud offers: Hebrew ones, except Hila - left out at the user's request.
 */
export function isOfferedVoice(voice: SpeechSynthesisVoice): boolean {
  return voice.lang.toLowerCase().startsWith('he') && !/hila|הילה/i.test(`${voice.name} ${voice.voiceURI}`)
}

/** The voice list arrives late on first use; waits for it once. */
function waitForVoices(): Promise<void> {
  if (speechSynthesis.getVoices().length > 0) return Promise.resolve()
  return new Promise((resolve) => {
    speechSynthesis.addEventListener('voiceschanged', () => resolve(), { once: true })
    setTimeout(resolve, 1500)
  })
}

/** Where to cut a too-long sentence: after the last comma / semicolon within the limit, else at the last space. */
function cutPoint(text: string, max: number): number {
  const head = text.slice(0, max)
  const punct = Math.max(head.lastIndexOf(','), head.lastIndexOf(';'))
  if (punct > max / 4) return punct + 1
  const space = head.lastIndexOf(' ')
  return space > 0 ? space : max
}

/** Splits text into sentence-sized chunks, each with its start offset in the text. */
export function splitIntoChunks(text: string, max = MAX_CHUNK_CHARS): { start: number; text: string }[] {
  const chunks: { start: number; text: string }[] = []
  const sentence = /[^.:;?!]+[.:;?!]*/g
  for (const match of text.matchAll(sentence)) {
    let start = match.index!
    let rest = match[0]
    while (rest.length > max) {
      const at = cutPoint(rest, max)
      if (rest.slice(0, at).trim()) chunks.push({ start, text: rest.slice(0, at) })
      start += at
      rest = rest.slice(at)
    }
    if (rest.trim()) chunks.push({ start, text: rest })
  }
  return chunks
}

/** The end of the word starting at `start` - the next whitespace. */
function wordEndAt(text: string, start: number): number {
  const match = /\s/.exec(text.slice(start))
  return match ? start + match.index : text.length
}

export function useBookViewReadAloud() {
  const isSupported = typeof window !== 'undefined' && 'speechSynthesis' in window
  const isReading = ref(false)
  const isPaused = ref(false)
  const position = shallowRef<ReadAloudPosition | null>(null)
  const voices = shallowRef<SpeechSynthesisVoice[]>([])

  /** The user's choices, kept between sessions. An empty voice means the first Hebrew voice. */
  const rate = useStorage<number>('bookView.readAloud.rate', 1)
  const voiceUri = useStorage<string>('bookView.readAloud.voice', '')
  /** Skipping on/off, and what it skips. */
  const skipEnabled = useStorage<boolean>('bookView.readAloud.skip', true)
  const skipOptions = useStorage<ReadAloudSkipOptions>('bookView.readAloud.skipOptions', { ...DEFAULT_SKIP_OPTIONS }, undefined, { mergeDefaults: true })
  /** Dicta prepares the text before it is spoken, always when reachable: nikkud for unpointed Hebrew, abbreviations in full. */
  const dicta = createReadAloudDicta()
  const google = createGoogleVoice()
  /** Set once the Google voice failed: the Windows voice reads on without retrying each line, until the next start. */
  let googleFailed = false
  /** Ends the line being spoken at once (stop / skip): its promise settles. */
  let endLine: (() => void) | null = null
  const activeSkip = () => (skipEnabled.value ? skipOptions.value : null)

  /** What the last start() was for, so skip and a changed setting can restart from the line. */
  let source: { bookId: number; versionId: number | null; totalLines: number } | null = null
  /** Bumped by stop(); a run that sees a newer generation ends. */
  let generation = 0

  function refreshVoices() {
    if (!isSupported) return
    voices.value = speechSynthesis.getVoices()
  }
  if (isSupported) speechSynthesis.addEventListener('voiceschanged', refreshVoices)

  function chosenVoice(): SpeechSynthesisVoice | null {
    const all = speechSynthesis.getVoices()
    return (
      all.find((v) => v.voiceURI === voiceUri.value && isOfferedVoice(v)) ??
      all.find(isOfferedVoice) ??
      null
    )
  }

  const usesGoogle = () => voiceUri.value === GOOGLE_VOICE_URI && !googleFailed

  /**
   * Queues all chunks to the Windows voice at once - no gap between them - and resolves
   * when the last one ends, or at once when the line is ended (stop / skip).
   */
  function speakAll(chunks: string[], voice: SpeechSynthesisVoice | null, onWord: (chunkIndex: number, charIndex: number, charLength: number) => void): Promise<void> {
    return new Promise((resolve) => {
      let left = chunks.length
      const finish = () => {
        if (endLine === finish) endLine = null
        resolve()
      }
      endLine = finish
      chunks.forEach((text, i) => {
        const utterance = new SpeechSynthesisUtterance(text)
        utterance.lang = voice?.lang ?? 'he-IL'
        if (voice) utterance.voice = voice
        utterance.rate = rate.value
        utterance.onboundary = (e) => {
          if (e.name === 'word') onWord(i, e.charIndex, e.charLength ?? 0)
        }
        utterance.onend = utterance.onerror = () => {
          if (--left === 0) finish()
        }
        speechSynthesis.speak(utterance)
      })
    })
  }

  /** Speaks one line's text (or a selection, lineIndex -1); false once stopped. */
  async function speakText(text: ReadAloudText, lineIndex: number, voice: SpeechSynthesisVoice | null, gen: number, fromFullChar = 0): Promise<boolean> {
    const { full, spoken, toFull } = text
    if (lineIndex >= 0) position.value = { lineIndex, text: full, wordStart: -1, wordEnd: -1, sentenceStart: -1, sentenceEnd: -1 }
    // Where in the spoken text to begin: the first letter at or after `fromFullChar` of the full text.
    let fromSpoken = 0
    if (fromFullChar > 0) {
      const found = toFull.findIndex((f) => f >= fromFullChar)
      fromSpoken = found < 0 ? spoken.length : found
    }
    const chunksFor = (max: number) =>
      splitIntoChunks(spoken, max)
        .filter((c) => c.start + c.text.length > fromSpoken)
        .map((c) => (c.start < fromSpoken ? { start: fromSpoken, text: c.text.slice(fromSpoken - c.start) } : c))
        .filter((c) => c.text.trim())
    // Spoken range -> full-text range, where the row's letters are counted.
    const toFullRange = (start: number, end: number) => {
      const s = toFull[start] ?? full.length
      return [s, end > start ? (toFull[end - 1] ?? full.length - 1) + 1 : s] as const
    }
    const onWord = (chunks: { start: number; text: string }[]) => (chunkIndex: number, charIndex: number, charLength: number) => {
      if (gen !== generation || lineIndex < 0) return
      const chunk = chunks[chunkIndex]!
      const start = chunk.start + charIndex
      const end = charLength > 0 ? start + charLength : wordEndAt(spoken, start)
      const [wordStart, wordEnd] = toFullRange(start, end)
      const [sentenceStart, sentenceEnd] = toFullRange(chunk.start, chunk.start + chunk.text.trimEnd().length)
      const prev = position.value
      // Same sentence: only the word moves (kept for resuming), the mark is not redrawn.
      if (prev && prev.lineIndex === lineIndex && prev.sentenceStart === sentenceStart && prev.sentenceEnd === sentenceEnd) {
        prev.wordStart = wordStart
        prev.wordEnd = wordEnd
        return
      }
      position.value = { lineIndex, text: full, wordStart, wordEnd, sentenceStart, sentenceEnd }
    }
    if (gen !== generation) return false
    if (usesGoogle()) {
      const chunks = chunksFor(GOOGLE_MAX_CHUNK_CHARS)
      try {
        await google.speak(chunks.map((c) => c.text), rate.value, onWord(chunks))
        return gen === generation
      } catch {
        // Offline or blocked: the Windows voice reads this line and the rest.
        googleFailed = true
        if (gen !== generation) return false
      }
    }
    const chunks = chunksFor(MAX_CHUNK_CHARS)
    if (chunks.length) await speakAll(chunks.map((c) => c.text), voice, onWord(chunks))
    return gen === generation
  }

  /**
   * A line as read aloud: skipped parts out, holy names by their spoken substitutes, then
   * heading numbers and line-start markers spelled out by their letters (Dicta comes after).
   */
  function lineText(row: { content?: string | null }): ReadAloudText {
    const text = readAloudTextOfLine(row.content ?? '', activeSkip())
    return withSpelledHeadingNumbers(withDivineNamesSpoken(text), !!text.heading)
  }

  async function run(fromLine: number, readSelection: boolean, fromChar = 0) {
    const gen = generation
    isReading.value = true
    isPaused.value = false
    try {
      await waitForVoices()
      refreshVoices()
      const voice = chosenVoice()
      const selected = readSelection ? window.getSelection()?.toString().trim() ?? '' : ''
      if (selected) {
        await speakText(withDivineNamesSpoken(readAloudTextOfPlain(selected, activeSkip())), -1, voice, gen)
        return
      }
      if (!source) return
      const { bookId, versionId, totalLines } = source
      // The next batch is fetched while this one is read, so Dicta's preparation can see ahead.
      let nextRows: Awaited<ReturnType<typeof getLinesPaged>> | null = null
      let isFirstLine = true
      for (let offset = Math.max(0, fromLine); offset < totalLines; offset += BATCH_SIZE) {
        const rows = await getLinesPaged(bookId, BATCH_SIZE, offset, versionId ?? 0)
        if (gen !== generation || rows.length === 0) return
        nextRows = null
        if (offset + BATCH_SIZE < totalLines) {
          void getLinesPaged(bookId, BATCH_SIZE, offset + BATCH_SIZE, versionId ?? 0).then((r) => { nextRows = r }, () => {})
        }
        const texts = rows.map(lineText)
        for (let i = 0; i < rows.length; i++) {
          const row = rows[i]!
          let text = texts[i]!
          const char = row.lineIndex === fromLine ? fromChar : 0
          if (!text.spoken.trim()) continue
          {
            const ahead = texts.slice(i, i + 1 + 8).map((t) => t.spoken)
            const more = nextRows as typeof rows | null
            if (more) for (const r of more.slice(0, 8)) ahead.push(lineText(r).spoken)
            dicta.prefetch(ahead, rate.value, DICTA_ALWAYS)
            text = await dicta.prepared(text, DICTA_ALWAYS, isFirstLine ? WAIT_FOR_FIRST_LINE_MS : undefined)
            isFirstLine = false
            if (gen !== generation) return
          }
          const done = await speakText(text, row.lineIndex, voice, gen, char)
          if (!done) return
          dicta.release(texts[i]!)
        }
      }
    } catch {
      // A failed fetch ends the reading.
    } finally {
      if (gen === generation) {
        dicta.clear()
        isReading.value = false
        isPaused.value = false
        position.value = null
      }
    }
  }

  /** Starts reading: the selection if there is one, else the book from `fromLine`. */
  function start(bookId: number, versionId: number | null, fromLine: number, totalLines: number) {
    if (!isSupported) return
    stop()
    source = { bookId, versionId, totalLines }
    void run(fromLine, true)
  }

  /** Restarts from another line of the same book - skip, or a changed voice or speed. */
  function restartFrom(lineIndex: number) {
    if (!isSupported || !source) return
    halt()
    void run(Math.min(Math.max(0, lineIndex), source.totalLines - 1), false)
  }

  /** Back or forward one line from the line being read. */
  function skip(delta: number) {
    const current = position.value?.lineIndex
    if (current == null) return
    restartFrom(current + delta)
  }

  function changeRate(delta: number) {
    const next = Math.round((rate.value + delta) * 10) / 10
    rate.value = Math.min(READ_ALOUD_RATE_MAX, Math.max(READ_ALOUD_RATE_MIN, next))
    applySettingsChange()
  }

  /** A new voice, speed or skip choice applies from the start of the line being read. */
  function applySettingsChange() {
    const current = position.value
    if (!isReading.value || !current || !source) return
    // Carries on from the word that was being spoken, keeping a pause as it was.
    const wasPaused = isPaused.value
    const fromChar = Math.max(0, current.wordStart)
    halt()
    void run(Math.min(Math.max(0, current.lineIndex), source.totalLines - 1), false, fromChar)
    if (wasPaused) pause()
  }

  /** Holds the voice mid-sentence; resume() carries on from the same word. */
  function pause() {
    if (!isReading.value || isPaused.value) return
    speechSynthesis.pause()
    google.pause()
    isPaused.value = true
  }

  function resume() {
    if (!isPaused.value) return
    speechSynthesis.resume()
    google.resume()
    isPaused.value = false
  }

  /** Stop for good: the speech ends and everything Dicta prepared is dropped. */
  function stop() {
    halt()
    // The next start tries the Google voice again.
    googleFailed = false
    dicta.clear()
  }

  /** Stops the speech only; prepared lines stay, for a restart (speed, voice, skip). */
  function halt() {
    generation++
    isReading.value = false
    isPaused.value = false
    position.value = null
    // resume() first: Chromium keeps a paused engine paused through cancel(), and every
    // utterance after it would then wait in silence.
    if (isSupported) {
      speechSynthesis.resume()
      speechSynthesis.cancel()
    }
    google.stop()
    endLine?.()
  }

  onScopeDispose(() => {
    stop()
    google.dispose()
    if (isSupported) speechSynthesis.removeEventListener('voiceschanged', refreshVoices)
  })

  return {
    isSupported, isReading, isPaused, position, voices,
    rate, voiceUri, skipEnabled, skipOptions,
    start, pause, resume, stop, skip, changeRate, applySettingsChange,
  }
}

export type BookViewReadAloud = ReturnType<typeof useBookViewReadAloud>
