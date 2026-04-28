import { ref, onUnmounted } from 'vue'
import { createWorker, type Worker } from 'tesseract.js'
import { PDF_OCR_INJECTED_SCRIPT } from './pdfOcrInjectedScript'

export type OcrScript = 'hebrew' | 'rashi'

export interface OcrSelectionResult {
  text: string
  isOcr: boolean
}

const LANG_FILES: Record<OcrScript, string> = {
  hebrew: 'heb',
  rashi: 'heb_rashi_fast',
}

export function usePdfOcrSelection(getIframe: () => HTMLIFrameElement | null) {
  const isActive = ref(false)
  const isProcessing = ref(false)
  const result = ref<OcrSelectionResult | null>(null)
  const script = ref<OcrScript>('hebrew')

  const workers: Partial<Record<OcrScript, Worker>> = {}
  const workerReady: Partial<Record<OcrScript, boolean>> = {}

  // ── Tesseract workers ──────────────────────────────────────────────────────

  async function initWorker(targetScript: OcrScript) {
    if (workers[targetScript]) return
    workers[targetScript] = await createWorker(LANG_FILES[targetScript], 1, {
      langPath: '/tesseract/',
      gzip: false,
    })
    workerReady[targetScript] = true
  }

  initWorker('hebrew').catch(() => {})

  onUnmounted(() => {
    for (const worker of Object.values(workers)) worker?.terminate()
    window.removeEventListener('message', onMessage)
  })

  // ── Inject script into iframe ──────────────────────────────────────────────

  function ensureInjected() {
    const iframe = getIframe()
    if (!iframe?.contentWindow) return false
    const win = iframe.contentWindow as any
    if (!win.__zayitOcrTool) {
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
    if (event.data?.type === 'zayit-ocr-result') {
      result.value = { text: event.data.text, isOcr: event.data.isOcr }
      isProcessing.value = false
    } else if (event.data?.type === 'zayit-ocr-canvas') {
      // Run Tesseract on the canvas data URL received from the iframe
      isProcessing.value = true
      try {
        const targetScript = script.value
        if (!workerReady[targetScript]) await initWorker(targetScript)
        const { data } = await workers[targetScript]!.recognize(event.data.dataUrl)
        result.value = { text: data.text.trim(), isOcr: true }
      } catch (error) {
        console.error('[OcrSelection] OCR failed:', error)
        result.value = { text: '', isOcr: true }
      } finally {
        isProcessing.value = false
      }
    } else if (event.data?.type === 'zayit-ocr-deactivated') {
      isActive.value = false
    }
  }

  window.addEventListener('message', onMessage)

  // ── Toggle ─────────────────────────────────────────────────────────────────

  function activate() {
    if (!ensureInjected()) return
    const win = (getIframe()?.contentWindow as any)
    win.__zayitOcrTool.activate(LANG_FILES[script.value])
    isActive.value = true
  }

  function deactivate() {
    const win = (getIframe()?.contentWindow as any)
    win?.__zayitOcrTool?.deactivate()
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
    if (win?.__zayitOcrTool?.isActive) {
      win.__zayitOcrTool.langFile = LANG_FILES[value]
    }
  }

  return {
    isActive,
    isProcessing,
    result,
    script,
    toggle,
    activate,
    deactivate,
    dismissResult,
    setScript,
  }
}
