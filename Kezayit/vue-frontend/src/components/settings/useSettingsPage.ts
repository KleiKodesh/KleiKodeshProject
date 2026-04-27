import { watch } from 'vue'
import { storeToRefs } from 'pinia'
import { useSettingsStore } from '@/stores/settingsStore'
import { useTabStore } from '@/stores/tabStore'
import { useSearchCacheStore } from '@/stores/searchCacheStore'

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
    // Schedule IDB wipe on next boot, then clear C# state and reload.
    tabStore.resetAll()
    if (typeof window.__webviewAction === 'function') {
      await window.__webviewAction('DeleteBloomIndex', {}).catch(() => {})
      await window.__webviewAction('resetSettings', {}).catch(() => {})
      window.__webviewAction('reload', {}).catch(() => window.location.reload())
    } else {
      window.location.reload()
    }
  }

  async function resetSearchIndex() {
    await searchCache.clear()
    await window.__webviewAction?.('ResetSearchIndex')
  }

  async function resetSettings() {
    // Frontend display/reading settings only — no tabs, no cache, no C# side.
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
    resetSearchIndex,
    resetAll,
  }
}
