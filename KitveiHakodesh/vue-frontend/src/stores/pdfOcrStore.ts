import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { OcrScript } from '@/features/pdf-reader/pdfOcrTypes'

export const usePdfOcrStore = defineStore('pdfOcr', () => {
  const isActive = ref(false)
  const script = ref<OcrScript>('mixed')
  // Force (OCR the image even where the PDF has a text layer): on by default.
  const skipExistingText = ref(true)

  function toggle() {
    isActive.value = !isActive.value
  }

  function deactivate() {
    isActive.value = false
  }

  function setScript(value: OcrScript) {
    script.value = value
  }

  function toggleSkipExistingText() {
    skipExistingText.value = !skipExistingText.value
  }

  return { isActive, script, skipExistingText, toggle, deactivate, setScript, toggleSkipExistingText }
})
