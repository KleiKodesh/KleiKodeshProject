import { defineStore } from 'pinia'
import { computed } from 'vue'
import { useTabStore } from './tabStore'

export const usePdfStore = defineStore('pdf', () => {
  const tabStore = useTabStore()

  const blobUrl = computed(() => tabStore.activeTab.pdfBlobUrl ?? null)
  const fileName = computed(() => tabStore.activeTab.pdfFileName ?? null)

  function openPdf(url: string, name: string) {
    const prev = tabStore.activeTab.pdfBlobUrl
    if (prev) URL.revokeObjectURL(prev)
    tabStore.updateActiveTab({ route: '/pdf-view', title: name, pdfBlobUrl: url, pdfFileName: name })
  }

  function clear() {
    const prev = tabStore.activeTab.pdfBlobUrl
    if (prev) URL.revokeObjectURL(prev)
    tabStore.updateActiveTab({ pdfBlobUrl: undefined, pdfFileName: undefined })
  }

  return { blobUrl, fileName, openPdf, clear }
})
