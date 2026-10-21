import { computed } from 'vue'
import { useSettingsStore } from '@/stores/settingsStore'
import { useSplitViewAvailable } from '@/layout/useSplitViewAvailable'

/**
 * Whether a HebrewBooks edition picked in this pane's book view opens side by side (in the
 * left side panel) or in a new tab.
 *
 * Side by side needs the same room as split view - never in the VSTO task pane, and only on
 * a wide window - so it shares that rule. Where it is not available the toggle is hidden and
 * every pick opens a new tab, whatever the persisted toggle says.
 */
export function useHebrewBooksSideEditionMode(paneId: 1 | 2) {
  const settingsStore = useSettingsStore()
  const isSideBySideAvailable = useSplitViewAvailable()

  const isToggleOn = computed({
    get: () => settingsStore.getHbEditionInSidePanel(paneId),
    set: (value: boolean) => settingsStore.setHbEditionInSidePanel(paneId, value),
  })

  const opensSideBySide = computed(() => isSideBySideAvailable.value && isToggleOn.value)

  return { isSideBySideAvailable, isToggleOn, opensSideBySide }
}
