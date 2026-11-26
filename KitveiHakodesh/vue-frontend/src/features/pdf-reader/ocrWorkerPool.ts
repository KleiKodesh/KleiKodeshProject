import type { Worker } from 'tesseract.js'
import type { OcrScript } from './pdfOcrTypes'

/**
 * Tesseract models per script, all from /tesseract/ beside the app (public/tesseract).
 * Rashi: Dicta's DictaRashi (CC BY-NC 4.0, see ThirdPartyNotices).
 */
const LANG_FILES: Record<OcrScript, string> = {
  hebrew: 'heb',
  rashi: 'DictaRashi',
  mixed: 'heb+DictaRashi',
  english: 'eng',
}

/** A worker nobody used for this long is terminated (its WASM heap and model are freed). */
const IDLE_TERMINATE_MS = 3 * 60_000

/**
 * One Tesseract worker per script for the whole app: every PDF view in every window shares them
 * (all windows run in one page). Created on first OCR use; terminated when idle or when the last
 * PDF view that uses OCR unmounts.
 */
const workers = new Map<OcrScript, Promise<Worker>>()
const idleTimers = new Map<OcrScript, ReturnType<typeof setTimeout>>()
let users = 0

function terminate(script: OcrScript) {
  const w = workers.get(script)
  workers.delete(script)
  const t = idleTimers.get(script)
  if (t != null) clearTimeout(t)
  idleTimers.delete(script)
  if (w) void w.then((worker) => worker.terminate()).catch(() => {})
}

function touch(script: OcrScript) {
  const t = idleTimers.get(script)
  if (t != null) clearTimeout(t)
  idleTimers.set(script, setTimeout(() => terminate(script), IDLE_TERMINATE_MS))
}

/** The script's worker, spawned once (concurrent callers share the spawn). */
export function ocrWorker(script: OcrScript): Promise<Worker> {
  let w = workers.get(script)
  if (!w) {
    w = (async () => {
      const { createWorker } = await import('tesseract.js')
      return createWorker(LANG_FILES[script], 1, {
        langPath: '/tesseract/',
        gzip: false,
        workerPath: '/tesseract/worker.min.js',
        corePath: '/tesseract/tesseract-core.wasm.js',
      })
    })()
    workers.set(script, w)
    w.catch(() => { if (workers.get(script) === w) workers.delete(script) })
  }
  touch(script)
  return w
}

/** Marks the script's worker as just used (restarts its idle countdown). */
export function ocrWorkerUsed(script: OcrScript) {
  if (workers.has(script)) touch(script)
}

/** A PDF view joins; the returned function leaves (the last one out terminates every worker). */
export function joinOcrWorkerPool(): () => void {
  users++
  let left = false
  return () => {
    if (left) return
    left = true
    users--
    if (users <= 0) {
      users = 0
      for (const s of [...workers.keys()]) terminate(s)
    }
  }
}

/** Live worker count (diagnostics / tests). */
export function ocrWorkerCount() {
  return workers.size
}
