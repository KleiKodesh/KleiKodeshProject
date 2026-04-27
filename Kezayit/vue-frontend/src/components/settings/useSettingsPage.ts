import { watch } from 'vue'
import { storeToRefs } from 'pinia'
import { useSettingsStore } from '@/stores/settingsStore'
import { useTabStore } from '@/stores/tabStore'
import { useSearchCacheStore } from '@/stores/searchCacheStore'
import { resetHostApp, resetSearchIndex as bridgeResetSearchIndex } from '@/host/bridge'

export function useSettingsPage() {
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
    tabStore.resetAll()
    await resetHostApp()
  }

  async function resetSearchIndexAction() {
    await searchCache.clear()
    await bridgeResetSearchIndex()
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
