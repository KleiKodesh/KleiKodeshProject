import { onScopeDispose, ref } from 'vue'
import { getLinesPaged } from '@/webview-host/seforimApi'

/** The line's plain text, vowel points kept - they are what lets the voice read it right. */
const htmlParser = new DOMParser()
function lineText(html: string): string {
  return htmlParser.parseFromString(html, 'text/html').body.textContent ?? ''
}

/**
 * Reads the book aloud with the browser's own speech (Windows voices).
 *
 * Reads the selected text when there is one; otherwise the book from the given line on,
 * fetching its own small batches so it never holds more than one batch of text.
 *
 * Text goes to the voice a sentence at a time: Chromium cuts off one long utterance part
 * way through, and short ones also let stop() take effect at once.
 */
const BATCH_SIZE = 20
const MAX_CHUNK_CHARS = 200

function hebrewVoice(): SpeechSynthesisVoice | null {
  return speechSynthesis.getVoices().find((v) => v.lang.toLowerCase().startsWith('he')) ?? null
}

/** The voice list arrives late on first use; waits for it once. */
function waitForVoices(): Promise<void> {
  if (speechSynthesis.getVoices().length > 0) return Promise.resolve()
  return new Promise((resolve) => {
    speechSynthesis.addEventListener('voiceschanged', () => resolve(), { once: true })
    setTimeout(resolve, 1500)
  })
}

function splitIntoChunks(text: string): string[] {
  const chunks: string[] = []
  for (const sentence of text.split(/(?<=[.:;?!])\s+/)) {
    let rest = sentence.trim()
    while (rest.length > MAX_CHUNK_CHARS) {
      const cut = rest.lastIndexOf(' ', MAX_CHUNK_CHARS)
      const at = cut > 0 ? cut : MAX_CHUNK_CHARS
      chunks.push(rest.slice(0, at))
      rest = rest.slice(at).trim()
    }
    if (rest) chunks.push(rest)
  }
  return chunks
}

export function useBookViewReadAloud() {
  const isReading = ref(false)
  const isSupported = typeof window !== 'undefined' && 'speechSynthesis' in window
  /** Bumped by stop(); a run that sees a newer generation ends. */
  let generation = 0

  function speak(text: string, voice: SpeechSynthesisVoice | null): Promise<void> {
    return new Promise((resolve) => {
      const utterance = new SpeechSynthesisUtterance(text)
      utterance.lang = voice?.lang ?? 'he-IL'
      if (voice) utterance.voice = voice
      utterance.onend = () => resolve()
      utterance.onerror = () => resolve()
      speechSynthesis.speak(utterance)
    })
  }

  async function speakText(text: string, voice: SpeechSynthesisVoice | null, gen: number): Promise<boolean> {
    for (const chunk of splitIntoChunks(text)) {
      if (gen !== generation) return false
      await speak(chunk, voice)
    }
    return gen === generation
  }

  async function start(bookId: number, versionId: number | null, fromLine: number, totalLines: number) {
    if (!isSupported) return
    stop()
    const gen = generation
    isReading.value = true
    try {
      await waitForVoices()
      const voice = hebrewVoice()
      const selected = window.getSelection()?.toString().trim() ?? ''
      if (selected) {
        await speakText(selected, voice, gen)
        return
      }
      for (let offset = Math.max(0, fromLine); offset < totalLines; offset += BATCH_SIZE) {
        const rows = await getLinesPaged(bookId, BATCH_SIZE, offset, versionId ?? 0)
        if (gen !== generation || rows.length === 0) return
        for (const row of rows) {
          const text = lineText(row.content ?? '')
          if (text.trim() && !(await speakText(text, voice, gen))) return
        }
      }
    } catch {
      // A failed fetch ends the reading; the button returns to its idle state.
    } finally {
      if (gen === generation) isReading.value = false
    }
  }

  function stop() {
    generation++
    isReading.value = false
    if (isSupported) speechSynthesis.cancel()
  }

  onScopeDispose(stop)

  return { isReading, isSupported, start, stop }
}
