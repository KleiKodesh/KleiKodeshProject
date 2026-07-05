/**
 * Side panel state for the book view.
 *
 * Manages which panel (TOC or commentary tree) is open, plus the derived
 * visibility flags and the toggle-button element reference used by
 * useDropdownClose in each panel.
 */
import { ref, computed } from 'vue'
import type { SidePanelMode } from './bookViewTypes'

type ToolbarInstance = { tocBtnRef: HTMLElement | null }
type CommentaryViewInstance = { getFilterButtonEl?: () => HTMLElement | null }

export function useBookViewSidePanel(
  toolbarRef: () => ToolbarInstance | null,
  commentaryViewRef: () => CommentaryViewInstance | null,
  commentaryVisible: import('vue').Ref<boolean>,
  loadAltTocSections: () => void,
  ensureStaticFilterGroupsLoaded: () => void,
) {
  const sidePanelMode = ref<SidePanelMode | null>(null)

  const tocVisible = computed(() => sidePanelMode.value === 'toc')
  const commentaryTreeVisible = computed(() => sidePanelMode.value === 'commentary-tree')
  const sidePanelVisible = computed(() => sidePanelMode.value !== null)

  const sidePanelToggleButtonEl = computed(() =>
    sidePanelMode.value === 'commentary-tree'
      ? commentaryViewRef()?.getFilterButtonEl?.() ?? null
      : toolbarRef()?.tocBtnRef ?? null,
  )

  function toggleTocPanel() {
    sidePanelMode.value = sidePanelMode.value === 'toc' ? null : 'toc'
    if (sidePanelMode.value === 'toc') loadAltTocSections()
  }

  function toggleCommentaryTreePanel() {
    if (!commentaryVisible.value) return
    sidePanelMode.value = sidePanelMode.value === 'commentary-tree' ? null : 'commentary-tree'
    if (sidePanelMode.value === 'commentary-tree') ensureStaticFilterGroupsLoaded()
  }

  function closeSidePanel() {
    sidePanelMode.value = null
  }

  return {
    sidePanelMode,
    tocVisible,
    commentaryTreeVisible,
    sidePanelVisible,
    sidePanelToggleButtonEl,
    toggleTocPanel,
    toggleCommentaryTreePanel,
    closeSidePanel,
  }
}
