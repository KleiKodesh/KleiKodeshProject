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

/**
 * Reads the book aloud with the browser's own speech (the voices installed in Windows).
 *
 * Reads the selected text when there is one; otherwise the book from a given line on,
 * fetching its own small batches so it never holds more than one batch of text.
 *
 * Text goes to the voice a sentence at a time: Chromium cuts off one long utterance part
 * way through, and short ones let stop / skip take effect at once.
 *
 * `position` says which line and which word is being spoken now, for the highlight and
 * for following along; it is null while reading a selection (that text has no line).
 */
const BATCH_SIZE = 20
const MAX_CHUNK_CHARS = 200

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
}

/**
 * The voices read aloud offers: Hebrew ones, except Hila - left out at the user's request.
 */
export function isOfferedVoice(voice: SpeechSynthesisVoice): boolean {
  return voice.lang.toLowerCase().startsWith('he') && !/hila/i.test(voice.name)
}

/** The voice list arrives late on first use; waits for it once. */
function waitForVoices(): Promise<void> {
  if (speechSynthesis.getVoices().length > 0) return Promise.resolve()
  return new Promise((resolve) => {
    speechSynthesis.addEventListener('voiceschanged', () => resolve(), { once: true })
    setTimeout(resolve, 1500)
  })
}

/** Splits text into sentence-sized chunks, each with its start offset in the text. */
function splitIntoChunks(text: string): { start: number; text: string }[] {
  const chunks: { start: number; text: string }[] = []
  const sentence = /[^.:;?!]+[.:;?!]*/g
  for (const match of text.matchAll(sentence)) {
    let start = match.index!
    let rest = match[0]
    while (rest.length > MAX_CHUNK_CHARS) {
      const cut = rest.lastIndexOf(' ', MAX_CHUNK_CHARS)
      const at = cut > 0 ? cut : MAX_CHUNK_CHARS
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
  const highlightWords = useStorage<boolean>('bookView.readAloud.highlight', true)
  /** Skipping on/off, and what it skips. */
  const skipEnabled = useStorage<boolean>('bookView.readAloud.skip', true)
  const skipOptions = useStorage<ReadAloudSkipOptions>('bookView.readAloud.skipOptions', { ...DEFAULT_SKIP_OPTIONS }, undefined, { mergeDefaults: true })
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

  function speak(text: string, voice: SpeechSynthesisVoice | null, onWord: (charIndex: number, charLength: number) => void): Promise<void> {
    return new Promise((resolve) => {
      const utterance = new SpeechSynthesisUtterance(text)
      utterance.lang = voice?.lang ?? 'he-IL'
      if (voice) utterance.voice = voice
      utterance.rate = rate.value
      utterance.onboundary = (e) => {
        if (e.name === 'word') onWord(e.charIndex, e.charLength ?? 0)
      }
      utterance.onend = () => resolve()
      utterance.onerror = () => resolve()
      speechSynthesis.speak(utterance)
    })
  }

  /** Speaks one line's text (or a selection, lineIndex -1); false once stopped. */
  async function speakText(text: ReadAloudText, lineIndex: number, voice: SpeechSynthesisVoice | null, gen: number): Promise<boolean> {
    const { full, spoken, toFull } = text
    if (lineIndex >= 0) position.value = { lineIndex, text: full, wordStart: -1, wordEnd: -1 }
    for (const chunk of splitIntoChunks(spoken)) {
      if (gen !== generation) return false
      await speak(chunk.text, voice, (charIndex, charLength) => {
        if (gen !== generation || lineIndex < 0) return
        const start = chunk.start + charIndex
        const end = charLength > 0 ? start + charLength : wordEndAt(spoken, start)
        // Back to the full text, where the row's letters are counted.
        const wordStart = toFull[start] ?? full.length
        const wordEnd = end > start ? (toFull[end - 1] ?? full.length - 1) + 1 : wordStart
        position.value = { lineIndex, text: full, wordStart, wordEnd }
      })
    }
    return gen === generation
  }

  async function run(fromLine: number, readSelection: boolean) {
    const gen = generation
    isReading.value = true
    isPaused.value = false
    try {
      await waitForVoices()
      refreshVoices()
      const voice = chosenVoice()
      const selected = readSelection ? window.getSelection()?.toString().trim() ?? '' : ''
      if (selected) {
        await speakText(readAloudTextOfPlain(selected, activeSkip()), -1, voice, gen)
        return
      }
      if (!source) return
      const { bookId, versionId, totalLines } = source
      for (let offset = Math.max(0, fromLine); offset < totalLines; offset += BATCH_SIZE) {
        const rows = await getLinesPaged(bookId, BATCH_SIZE, offset, versionId ?? 0)
        if (gen !== generation || rows.length === 0) return
        for (const row of rows) {
          const text = readAloudTextOfLine(row.content ?? '', activeSkip())
          if (text.spoken.trim() && !(await speakText(text, row.lineIndex, voice, gen))) return
        }
      }
    } catch {
      // A failed fetch ends the reading.
    } finally {
      if (gen === generation) {
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
    stop()
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
    const current = position.value?.lineIndex
    if (isReading.value && current != null) restartFrom(current)
  }

  /** Holds the voice mid-sentence; resume() carries on from the same word. */
  function pause() {
    if (!isReading.value || isPaused.value) return
    speechSynthesis.pause()
    isPaused.value = true
  }

  function resume() {
    if (!isPaused.value) return
    speechSynthesis.resume()
    isPaused.value = false
  }

  function stop() {
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
  }

  onScopeDispose(() => {
    stop()
    if (isSupported) speechSynthesis.removeEventListener('voiceschanged', refreshVoices)
  })

  return {
    isSupported, isReading, isPaused, position, voices,
    rate, voiceUri, highlightWords, skipEnabled, skipOptions,
    start, pause, resume, stop, skip, changeRate, applySettingsChange,
  }
}

export type BookViewReadAloud = ReturnType<typeof useBookViewReadAloud>
