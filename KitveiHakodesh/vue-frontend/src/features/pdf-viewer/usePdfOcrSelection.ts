import { ref, onUnmounted } from 'vue'
import { createWorker, type Worker } from 'tesseract.js'
import { PDF_OCR_INJECTED_SCRIPT } from './pdfOcrInjectedScript'

export type OcrScript = 'hebrew' | 'rashi' | 'mixed'

export interface OcrSelectionResult {
  text: string
  isOcr: boolean
}

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

  const workers: Partial<Record<OcrScript, Worker>> = {}
  const workerReady: Partial<Record<OcrScript, boolean>> = {}

  // ── Tesseract workers ──────────────────────────────────────────────────────

  async function initWorker(targetScript: OcrScript) {
    if (workers[targetScript]) return
    workers[targetScript] = await createWorker(LANG_FILES[targetScript], 1, {
      langPath: '/tesseract/',
      gzip: false,
      corePath: 'https://cdn.jsdelivr.net/npm/tesseract.js-core@v5/tesseract-core.wasm.js',
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
      // Check if user wants to skip OCR for existing text
      if (event.data.hasExistingText && !event.data.forceOcr) {
        // Text layer exists and user didn't force OCR, so skip
        isProcessing.value = false
        return
      }
      
      // Show popup immediately with processing state
      result.value = { text: '', isOcr: true }
      isProcessing.value = true
      processingProgress.value = 0
      
      // Run Tesseract on the canvas data URL received from the iframe
      try {
        const targetScript = script.value
        if (!workerReady[targetScript]) await initWorker(targetScript)
        
        // Simulate progress updates during OCR
        const progressInterval = setInterval(() => {
          if (processingProgress.value < 0.9) {
            processingProgress.value += Math.random() * 0.3
          }
        }, 200)
        
        const { data } = await workers[targetScript]!.recognize(event.data.dataUrl)
        clearInterval(progressInterval)
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
    win.__kitveiHakodeshOcrTool.activate(LANG_FILES[script.value])
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

  return {
    isActive,
    isProcessing,
    processingProgress,
    result,
    script,
    toggle,
    activate,
    deactivate,
    dismissResult,
    setScript,
  }
}
