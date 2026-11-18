import { computed } from 'vue'
import { useSettingsStore, type HbEditionSide } from '@/stores/settingsStore'
import type { CommentarySlot } from './bookViewTypes'
import { useSplitViewAvailable } from '@/layout/useSplitViewAvailable'

/**
 * Whether a HebrewBooks edition picked in this pane's book view opens side by side (in the
 * side panel the reader chose - left or right, one setting for all books and panes) or in
 * a new tab.
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

  /** The physical side the edition shows in: 'left' or 'right'. */
  const side = computed({
    get: () => settingsStore.hbEditionSide,
    set: (value: HbEditionSide) => { settingsStore.hbEditionSide = value },
  })
  /** The book view's commentary slot that side is: 'side' hugs the right edge. */
  const slot = computed<CommentarySlot>(() => (side.value === 'right' ? 'side' : 'side-left'))

  return { isSideBySideAvailable, isToggleOn, opensSideBySide, side, slot }
}
