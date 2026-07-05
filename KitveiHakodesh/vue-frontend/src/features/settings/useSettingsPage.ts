import { watch } from 'vue'
import { storeToRefs } from 'pinia'
import { useSettingsStore } from '@/stores/settingsStore'
import { useTabStore } from '@/stores/tabStore'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { useSearchCacheStore } from '@/stores/searchCacheStore'
import { resetHostApp, resetSearchIndex as bridgeResetSearchIndex, resetDocumentLocatorIndex as bridgeResetDocumentLocatorIndex, setTheme } from '@/webview-host/bridge'

export function useSettings() {
  const settings = useSettingsStore()
  const tabStore = useTabStore()
  const paneNavigation = usePaneNavigation()
  const searchCache = useSearchCacheStore()

  const {
    censorDivineNames,
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

  async function resetAll() {
    await tabStore.resetAll()
    await resetHostApp()
  }

  async function resetSearchIndexAction() {
    await searchCache.clear()
    await bridgeResetSearchIndex()
    paneNavigation.updateActiveTab({ route: '/search', title: 'חיפוש' })
  }

  async function resetDocumentLocatorIndexAction() {
    await bridgeResetDocumentLocatorIndex()
    paneNavigation.updateActiveTab({ route: '/file-search', title: 'חיפוש קבצים' })
  }

  function resetSettings() {
    settings.reset()
    // Reset the title bar to light mode — settings reset implies reverting to
    // defaults, which includes light theme. setTheme(false) also saves false to
    // the registry so the next startup loads light mode correctly.
    setTheme(false)
  }

  return {
    censorDivineNames,
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
    resetAll,
  }
}
