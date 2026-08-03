import { watch } from 'vue'
import { storeToRefs } from 'pinia'
import { useSettingsStore } from '@/stores/settingsStore'
import { useSearchCacheStore } from '@/stores/searchCacheStore'
import { resetSearchIndex as bridgeResetSearchIndex, resetDocumentLocatorIndex as bridgeResetDocumentLocatorIndex, setTheme } from '@/webview-host/bridge'

export function useSettings() {
  const settings = useSettingsStore()
  const searchCache = useSearchCacheStore()

  const {
    divineNameMode,
    headerFont,
    textFont,
    fontSize,
    linePadding,
    commentaryHeaderFont,
    commentaryTextFont,
    commentaryFontSize,
    commentaryLinePadding,
    useSeparateCommentarySettings,
    appZoom,
    newTabPage,
    resumeLastRead,
  } = storeToRefs(settings)

  watch([useSeparateCommentarySettings, headerFont, textFont, fontSize, linePadding], () => {
    if (!useSeparateCommentarySettings.value) {
      commentaryHeaderFont.value = headerFont.value
      commentaryTextFont.value = textFont.value
      commentaryFontSize.value = fontSize.value
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

  function resetSettings() {
    settings.reset()
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
    linePadding,
    commentaryHeaderFont,
    commentaryTextFont,
    commentaryFontSize,
    commentaryLinePadding,
    useSeparateCommentarySettings,
    appZoom,
    newTabPage,
    resumeLastRead,
    resetSettings,
    resetSearchIndex: resetSearchIndexAction,
    resetDocumentLocatorIndex: resetDocumentLocatorIndexAction,
  }
}
