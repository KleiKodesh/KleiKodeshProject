import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { OcrScript } from '@/features/pdf-viewer/usePdfOcrSelection'

export const usePdfOcrStore = defineStore('pdfOcr', () => {
  const isActive = ref(false)
  const script = ref<OcrScript>('mixed')
  const skipExistingText = ref(false)

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
