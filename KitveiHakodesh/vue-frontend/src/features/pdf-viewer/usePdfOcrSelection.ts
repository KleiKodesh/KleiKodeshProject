import { ref, onUnmounted } from 'vue'
import type { Worker } from 'tesseract.js'
import { PDF_OCR_INJECTED_SCRIPT } from './pdfOcrInjectedScript'

import type { OcrScript, OcrSelectionResult } from './pdfViewerTypes'

export type { OcrScript, OcrSelectionResult }

const LANG_FILES: Record<OcrScript, string> = {
  hebrew: 'heb',
  rashi: 'heb_rashi',
  mixed: 'heb+heb_rashi',
}

export function usePdfOcrSelection(getIframe: () => HTMLIFrameElement | null) {
  const isActive = ref(false)
  const isProcessing = ref(false)
  const result = ref<OcrSelectionResult | null>(null)
  const script = ref<OcrScript>('hebrew')
  const processingProgress = ref(0)
  const forceOcr = ref(false)

  const workers: Partial<Record<OcrScript, Worker>> = {}
  const workerReady: Partial<Record<OcrScript, boolean>> = {}
  // In-flight spawns, keyed by script. `workers` only tells us about FINISHED ones, and a
  // spawn takes seconds (WASM core + traineddata) — two callers racing that window would
  // both spawn, and the second assignment would orphan the first worker beyond the reach of
  // onUnmounted's sweep. Callers await the same promise instead.
  const workerSpawns: Partial<Record<OcrScript, Promise<void>>> = {}

  // ── Tesseract workers ──────────────────────────────────────────────────────

  // Tesseract is imported dynamically on first use so it does not add to the
  // initial JS parse cost — it's only needed when the user opens a PDF tab and
  // activates OCR mode.
  // Set once the composable's owner has torn down. createWorker is awaited, so a worker can
  // finish spawning AFTER onUnmounted has already swept `workers` — without this it would
  // never be terminated and its WASM heap would leak for every tab closed mid-load.
  let disposed = false

  function initWorker(targetScript: OcrScript): Promise<void> {
    if (workers[targetScript]) return Promise.resolve()
    const existing = workerSpawns[targetScript]
    if (existing) return existing // a spawn is already in flight — share it

    const spawn = (async () => {
      const { createWorker } = await import('tesseract.js')
      const worker = await createWorker(LANG_FILES[targetScript], 1, {
        langPath: '/tesseract/',
        gzip: false,
        workerPath: '/tesseract/worker.min.js',
        corePath: '/tesseract/tesseract-core.wasm.js',
      })
      // Torn down while we were spawning: onUnmounted's sweep has already run and will not
      // run again, so this worker is ours to terminate.
      if (disposed) {
        void worker.terminate()
        return
      }
      workers[targetScript] = worker
      workerReady[targetScript] = true
    })()

    workerSpawns[targetScript] = spawn
    // Clear the slot either way, so a failed spawn can be retried rather than the rejection
    // being replayed to every later caller. The catch is on this bookkeeping chain only —
    // `spawn` itself is returned untouched, so callers still see the rejection.
    spawn.catch(() => {}).finally(() => { delete workerSpawns[targetScript] })
    return spawn
  }

  onUnmounted(() => {
    disposed = true
    for (const worker of Object.values(workers)) worker?.terminate()
    window.removeEventListener('message', onMessage)
  })

  // ── Inject script into iframe ──────────────────────────────────────────────

  function ensureInjected() {
    const iframe = getIframe()
    if (!iframe?.contentWindow) return false
    const win = iframe.contentWindow as any
    if (!win.__kitveiHakodeshOcrTool) {
      try {
        win.eval(PDF_OCR_INJECTED_SCRIPT)
      } catch (error) {
        console.error('[OcrSelection] Failed to inject script:', error)
        return false
      }
    }
    return true
  }

  // ── postMessage handler ────────────────────────────────────────────────────

  async function onMessage(event: MessageEvent) {
    if (event.data?.type === 'kitvei-hakodesh-ocr-result') {
      const cleanText = event.data.text
        .split('\n')
        .map((line: string) => line.trim())
        .filter((line: string) => line.length > 0)
        .join('\n')
        .replace(/\s+/g, ' ')
      result.value = { text: cleanText, isOcr: event.data.isOcr }
      isProcessing.value = false
    } else if (event.data?.type === 'kitvei-hakodesh-ocr-canvas') {
      // Show popup immediately with processing state
      result.value = { text: '', isOcr: true }
      isProcessing.value = true
      processingProgress.value = 0
      
      // Run Tesseract on the canvas data URL received from the iframe
      let progressInterval: ReturnType<typeof setInterval> | null = null
      try {
        const targetScript = script.value
        if (!workerReady[targetScript]) await initWorker(targetScript)
        // Torn down while the worker was spawning — initWorker already terminated it.
        if (disposed || !workers[targetScript]) return

        // Simulate progress updates during OCR
        progressInterval = setInterval(() => {
          if (processingProgress.value < 0.9) {
            processingProgress.value += Math.random() * 0.3
          }
        }, 200)

        const { data } = await workers[targetScript]!.recognize(event.data.dataUrl)
        clearInterval(progressInterval)
        progressInterval = null
        processingProgress.value = 1
        
        const cleanText = data.text
          .split('\n')
          .map((line: string) => line.trim())
          .filter((line: string) => line.length > 0)
          .join('\n')
          .replace(/\s+/g, ' ')
        result.value = { text: cleanText.trim(), isOcr: true }
      } catch (error) {
        console.error('[OcrSelection] OCR failed:', error)
        result.value = { text: '', isOcr: true }
      } finally {
        // recognize() throwing (or initWorker failing) used to leave this 200ms timer
        // running for the life of the page, writing into component state forever.
        if (progressInterval != null) clearInterval(progressInterval)
        isProcessing.value = false
        processingProgress.value = 0
      }
    } else if (event.data?.type === 'kitvei-hakodesh-ocr-deactivated') {
      isActive.value = false
    }
  }

  window.addEventListener('message', onMessage)

  // ── Toggle ─────────────────────────────────────────────────────────────────

  function activate() {
    if (!ensureInjected()) return
    const win = (getIframe()?.contentWindow as any)
    win.__kitveiHakodeshOcrTool.activate(LANG_FILES[script.value], forceOcr.value)
    isActive.value = true
  }

  function deactivate() {
    const win = (getIframe()?.contentWindow as any)
    win?.__kitveiHakodeshOcrTool?.deactivate()
    isActive.value = false
    result.value = null
  }

  function toggle() {
    isActive.value ? deactivate() : activate()
  }

  function dismissResult() {
    result.value = null
  }

  function setScript(value: OcrScript) {
    script.value = value
    initWorker(value).catch(() => {})
    // Update lang in iframe if active
    const win = (getIframe()?.contentWindow as any)
    if (win?.__kitveiHakodeshOcrTool?.isActive) {
      win.__kitveiHakodeshOcrTool.langFile = LANG_FILES[value]
    }
  }

  function setForceOcr(value: boolean) {
    forceOcr.value = value
    // Update flag in iframe if active so next selection immediately respects the change
    const win = (getIframe()?.contentWindow as any)
    if (win?.__kitveiHakodeshOcrTool?.isActive) {
      win.__kitveiHakodeshOcrTool.forceOcr = value
    }
  }

  return {
    isActive,
    isProcessing,
    processingProgress,
    result,
    script,
    forceOcr,
    toggle,
    activate,
    deactivate,
    dismissResult,
    setScript,
    setForceOcr,
  }
}
