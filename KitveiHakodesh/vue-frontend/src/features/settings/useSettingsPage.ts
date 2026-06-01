import { watch } from 'vue'
import { storeToRefs } from 'pinia'
import { useSettingsStore } from '@/stores/settingsStore'
import { useTabStore } from '@/stores/tabStore'
import { useSearchCacheStore } from '@/stores/searchCacheStore'
import { resetHostApp, resetSearchIndex as bridgeResetSearchIndex } from '@/webview-host/bridge'

export function useSettings() {
  const settings = useSettingsStore()
  const tabStore = useTabStore()
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
    // Navigate to the search page so the indexing overlay is visible while the
    // index rebuilds. The search page mounts useFullTextSearchIndexingStatus which
    // polls GetFtsIndexingProgress and subscribes to ftsIndexProgress events.
    tabStore.updateActiveTab({ route: '/search', title: 'חיפוש' })
  }

  function resetSettings() {
    settings.reset()
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
    resetAll,
  }
}
