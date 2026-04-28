import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { OcrScript } from '@/features/pdf-viewer/usePdfOcrSelection'

export const usePdfOcrStore = defineStore('pdfOcr', () => {
  const isActive = ref(false)
  const script = ref<OcrScript>('hebrew')

  function toggle() {
    isActive.value = !isActive.value
  }

  function deactivate() {
    isActive.value = false
  }

  function setScript(value: OcrScript) {
    script.value = value
  }

  return { isActive, script, toggle, deactivate, setScript }
})
