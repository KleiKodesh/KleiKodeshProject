import { watch } from 'vue'
import { storeToRefs } from 'pinia'
import { useSettingsStore } from '@/stores/settingsStore'
import { useTabStore } from '@/stores/tabStore'

export function useSettingsPage() {
  const settings = useSettingsStore()
  const tabStore = useTabStore()

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

  function resetAll() {
    tabStore.resetAll()
    // Clear all settings (including setupDone) so the setup wizard shows after reload
    settings.reset()
    if (typeof window.__webviewAction === 'function') {
      window.__webviewAction('DeleteBloomIndex', {}).catch(() => {})
      window.__webviewAction('resetSettings', {}).catch(() => {})
      window.__webviewAction('reload', {}).catch(() => window.location.reload())
    } else {
      window.location.reload()
    }
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
    resetAll,
  }
}
