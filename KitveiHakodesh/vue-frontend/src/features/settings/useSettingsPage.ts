import { watch } from 'vue'
import { storeToRefs } from 'pinia'
import { useSettingsStore } from '@/stores/settingsStore'
import { useSearchCacheStore } from '@/stores/searchCacheStore'
import { resetSearchIndex as bridgeResetSearchIndex, resetDocumentLocatorIndex as bridgeResetDocumentLocatorIndex, resetCatalogTocIndex as bridgeResetCatalogTocIndex, setTheme } from '@/webview-host/bridge'
import { clearCatalogTocCache } from '@/features/book-catalog/bookCatalogTocSearchCache'

export function useSettings() {
  const settings = useSettingsStore()
  const searchCache = useSearchCacheStore()

  const {
    divineNameMode,
    headerFont,
    textFont,
    fontSize,
    fontWeight,
    linePadding,
    commentaryHeaderFont,
    commentaryTextFont,
    commentaryFontSize,
    commentaryFontWeight,
    commentaryLinePadding,
    useSeparateCommentarySettings,
    appZoom,
    newTabPage,
    resumeLastRead,
  } = storeToRefs(settings)

  watch([useSeparateCommentarySettings, headerFont, textFont, fontSize, fontWeight, linePadding], () => {
    if (!useSeparateCommentarySettings.value) {
      commentaryHeaderFont.value = headerFont.value
      commentaryTextFont.value = textFont.value
      commentaryFontSize.value = fontSize.value
      commentaryFontWeight.value = fontWeight.value
      commentaryLinePadding.value = linePadding.value
    }
  })

  async function resetSearchIndexAction() {
    await searchCache.clear()
    await bridgeResetSearchIndex()
  }

  async function resetDocumentLocatorIndexAction() {
    await bridgeResetDocumentLocatorIndex()
  }

  /**
   * Clear the frontend's TOC result cache alongside the service-side Lucene index —
   * otherwise a cached hit keeps serving results from the index we just wiped, which
   * would make the reset look like it did nothing.
   */
  async function resetCatalogTocIndexAction() {
    await clearCatalogTocCache()
    await bridgeResetCatalogTocIndex()
  }

  // Awaited: reset() only clears localStorage after the persist watchers have flushed, so
  // a caller that returned early could reload the page before the keys were gone.
  async function resetSettings() {
    await settings.reset()
    // Reset the title bar to light mode — settings reset implies reverting to
    // defaults, which includes light theme. setTheme(false) also saves false to
    // the registry so the next startup loads light mode correctly.
    setTheme(false)
  }

  return {
    divineNameMode,
    headerFont,
    textFont,
    fontSize,
    fontWeight,
    linePadding,
    commentaryHeaderFont,
    commentaryTextFont,
    commentaryFontSize,
    commentaryFontWeight,
    commentaryLinePadding,
    useSeparateCommentarySettings,
    appZoom,
    newTabPage,
    resumeLastRead,
    resetSettings,
    resetSearchIndex: resetSearchIndexAction,
    resetDocumentLocatorIndex: resetDocumentLocatorIndexAction,
    resetCatalogTocIndex: resetCatalogTocIndexAction,
  }
}
