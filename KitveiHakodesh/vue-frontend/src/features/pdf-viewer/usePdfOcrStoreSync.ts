import { watch } from 'vue'
import { usePdfOcrStore } from '@/stores/pdfOcrStore'
import type { usePdfOcrSelection } from './usePdfOcrSelection'

/**
 * Keeps one iframe's OCR selection in step with the global pdfOcrStore (the title-bar OCR
 * button and the toolbar's script / force choices). Used by every PDF.js host that offers
 * OCR - the PDF tab and the book view's side-by-side HebrewBooks edition.
 */
export function usePdfOcrStoreSync(ocr: ReturnType<typeof usePdfOcrSelection>) {
  const pdfOcrStore = usePdfOcrStore()

  watch(pdfOcrStore, () => {
    if (pdfOcrStore.isActive !== ocr.isActive.value) {
      pdfOcrStore.isActive ? ocr.activate() : ocr.deactivate()
    }
    if (pdfOcrStore.script !== ocr.script.value) {
      ocr.setScript(pdfOcrStore.script)
    }
    if (pdfOcrStore.skipExistingText !== ocr.forceOcr.value) {
      ocr.setForceOcr(pdfOcrStore.skipExistingText)
    }
  })

  // Deactivate store when composable deactivates (e.g. after selection)
  watch(ocr.isActive, (active) => {
    if (!active && pdfOcrStore.isActive) pdfOcrStore.deactivate()
  })
}
