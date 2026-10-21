import { watch } from 'vue'
import { storeToRefs } from 'pinia'
import { useSettingsStore } from '@/stores/settingsStore'
import { useSearchCacheStore } from '@/stores/searchCacheStore'
import { watchFullTextSearchIndexStatus } from '@/features/full-text-search/useFullTextSearchIndexingStatus'
import { resetSearchIndex as bridgeResetSearchIndex, resetDocumentLocatorIndex as bridgeResetDocumentLocatorIndex, resetCatalogTocIndex as bridgeResetCatalogTocIndex, setTheme } from '@/webview-host/bridge'

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
    appZoom,
    newTabPage,
    resumeLastRead,
  } = storeToRefs(settings)

  async function resetSearchIndexAction() {
    await searchCache.clear()
    await bridgeResetSearchIndex()
    // The app-wide status stream ended when the old index was ready — reopen it so a
    // failed rebuild still toasts with the full-text-search page closed.
    watchFullTextSearchIndexStatus()
  }

  async function resetDocumentLocatorIndexAction() {
    await bridgeResetDocumentLocatorIndex()
  }

  async function resetCatalogTocIndexAction() {
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
    appZoom,
    newTabPage,
    resumeLastRead,
    resetSettings,
    resetSearchIndex: resetSearchIndexAction,
    resetDocumentLocatorIndex: resetDocumentLocatorIndexAction,
    resetCatalogTocIndex: resetCatalogTocIndexAction,
  }
}
