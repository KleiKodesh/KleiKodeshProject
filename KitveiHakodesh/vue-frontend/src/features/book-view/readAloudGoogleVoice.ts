/**
 * The Google voice (online): Google Translate's speech, played through an <audio> element -
 * a media request needs no CORS. Each request takes at most 200 characters, so a line goes
 * chunk by chunk; the next chunk is fetched while this one plays, so there is no gap.
 *
 * Google gives no word timings: the word shown is estimated from how far the audio is
 * through its chunk. Any failure (offline, blocked) rejects, and the caller reads on with
 * the Windows voice.
 */
export const GOOGLE_VOICE_URI = 'google-online'
export const GOOGLE_MAX_CHUNK_CHARS = 200

const ttsUrl = (text: string) =>
  `https://translate.google.com/translate_tts?ie=UTF-8&tl=iw&client=tw-ob&q=${encodeURIComponent(text)}`

/** Word starts of a chunk, for the estimated word. */
function wordStarts(text: string): { start: number; length: number }[] {
  return [...text.matchAll(/\S+/g)].map((m) => ({ start: m.index!, length: m[0].length }))
}

export function createGoogleVoice() {
  let current: HTMLAudioElement | null = null
  /** Ends the playing line at once (stop / skip). */
  let abortCurrent: (() => void) | null = null
  /** Held by pause(): a chunk that comes up meanwhile waits for resume(). */
  let paused = false

  /**
   * Google answers 404 to a request that carries a Referer, and <audio> has no
   * referrerpolicy of its own: the audio elements are made in a hidden frame whose
   * document sends no referrer, so the rest of the app keeps its own policy.
   */
  let frame: HTMLIFrameElement | null = null
  function noReferrerWindow(): Window & typeof globalThis {
    if (!frame?.isConnected || !frame.contentWindow) {
      frame = document.createElement('iframe')
      frame.hidden = true
      frame.setAttribute('aria-hidden', 'true')
      document.body.appendChild(frame)
      const doc = frame.contentDocument!
      doc.open()
      doc.write('<meta name="referrer" content="no-referrer">')
      doc.close()
    }
    return frame.contentWindow as Window & typeof globalThis
  }

  function load(text: string): HTMLAudioElement {
    const audio = new (noReferrerWindow().Audio)()
    audio.preload = 'auto'
    audio.src = ttsUrl(text)
    return audio
  }

  /**
   * Plays the chunks in order; `onWord` gets each estimated word (char index in its chunk).
   * Resolves when all are played or stop() was called; rejects when a chunk fails to load.
   */
  function speak(
    chunks: string[],
    rate: number,
    onWord: (chunkIndex: number, charIndex: number, charLength: number) => void,
  ): Promise<void> {
    abortCurrent?.()
    return new Promise<void>((resolve, reject) => {
      let stopped = false
      let frame = 0
      const done = (error?: unknown) => {
        if (stopped) return
        stopped = true
        cancelAnimationFrame(frame)
        current?.pause()
        current = null
        abortCurrent = null
        if (error) reject(error)
        else resolve()
      }
      abortCurrent = () => done()
      const audios: (HTMLAudioElement | null)[] = chunks.map(() => null)
      const audioAt = (i: number) => (audios[i] ??= load(chunks[i]!))

      const play = (i: number) => {
        if (stopped) return
        if (i >= chunks.length) return done()
        const audio = audioAt(i)
        current = audio
        audio.playbackRate = rate
        const words = wordStarts(chunks[i]!)
        let lastWord = -1
        // The word from the audio's progress, once per frame while it plays.
        const track = () => {
          if (stopped || current !== audio) return
          if (audio.duration > 0 && words.length) {
            const w = Math.min(words.length - 1, Math.floor((audio.currentTime / audio.duration) * words.length))
            if (w !== lastWord) {
              lastWord = w
              onWord(i, words[w]!.start, words[w]!.length)
            }
          }
          frame = requestAnimationFrame(track)
        }
        audio.onplaying = () => {
          cancelAnimationFrame(frame)
          track()
          // The next chunk loads while this one plays.
          if (i + 1 < chunks.length) audioAt(i + 1)
        }
        audio.onended = () => play(i + 1)
        audio.onerror = () => done(new Error('google voice unavailable'))
        if (paused) return
        audio.play().catch((e: unknown) => {
          // A pause() racing play() is not a failure.
          if ((e as { name?: string })?.name !== 'AbortError') done(e)
        })
      }
      play(0)
    })
  }

  return {
    speak,
    pause: () => {
      paused = true
      current?.pause()
    },
    resume: () => {
      paused = false
      void current?.play().catch(() => {})
    },
    stop: () => {
      paused = false
      abortCurrent?.()
    },
    /** Stops and removes the hidden frame (the book view closed). */
    dispose: () => {
      paused = false
      abortCurrent?.()
      frame?.remove()
      frame = null
    },
  }
}

export type GoogleVoice = ReturnType<typeof createGoogleVoice>
