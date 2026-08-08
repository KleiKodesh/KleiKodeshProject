/**
 * Side panel state for the book view: which panel (the TOC or a commentary filter
 * tree) is open, and - for the filter tree - WHICH commentary panel it belongs to.
 *
 * There are two commentary panels but only one filter tree component. It is reused
 * and simply bound to the state of whichever panel's filter button was pressed
 * (`commentaryTreeSlot`): pressing that button again closes it, pressing the other
 * panel's button re-targets it at that panel.
 */
import { ref, computed } from 'vue'
import type { CommentarySlot, SidePanelMode } from './bookViewTypes'

type ToolbarInstance = { tocBtnRef: HTMLElement | null }
type CommentaryViewInstance = { getFilterButtonEl?: () => HTMLElement | null }

export function useBookViewSidePanel(
  toolbarRef: () => ToolbarInstance | null,
  commentaryViewRefs: Record<CommentarySlot, () => CommentaryViewInstance | null>,
  commentaryVisible: Record<CommentarySlot, import('vue').Ref<boolean>>,
  loadAltTocSections: () => void,
  ensureStaticFilterGroupsLoaded: () => void,
) {
  const sidePanelMode = ref<SidePanelMode | null>(null)
  /** Which commentary panel the open filter tree is showing. */
  const commentaryTreeSlot = ref<CommentarySlot | null>(null)

  const tocVisible = computed(() => sidePanelMode.value === 'toc')
  const commentaryTreeVisible = computed(() => sidePanelMode.value === 'commentary-tree')
  const sidePanelVisible = computed(() => sidePanelMode.value !== null)

  /** True when the filter tree is open AND bound to this panel. */
  function isCommentaryTreeOpenFor(slot: CommentarySlot): boolean {
    return commentaryTreeVisible.value && commentaryTreeSlot.value === slot
  }

  // The element useDropdownClose must ignore, so that clicking the button which
  // opened the panel closes it instead of being swallowed as a click-outside.
  const sidePanelToggleButtonEl = computed(() => {
    const slot = commentaryTreeVisible.value ? commentaryTreeSlot.value : null
    if (slot) return commentaryViewRefs[slot]()?.getFilterButtonEl?.() ?? null
    return toolbarRef()?.tocBtnRef ?? null
  })

  function toggleTocPanel() {
    const opening = sidePanelMode.value !== 'toc'
    sidePanelMode.value = opening ? 'toc' : null
    commentaryTreeSlot.value = null
    if (opening) loadAltTocSections()
  }

  function toggleCommentaryTreePanel(slot: CommentarySlot) {
    if (!commentaryVisible[slot].value) return
    if (isCommentaryTreeOpenFor(slot)) {
      closeSidePanel()
      return
    }
    sidePanelMode.value = 'commentary-tree'
    commentaryTreeSlot.value = slot
    ensureStaticFilterGroupsLoaded()
  }

  function closeSidePanel() {
    sidePanelMode.value = null
    commentaryTreeSlot.value = null
  }

  /** A commentary panel closed: drop its filter tree, leaving the other's alone. */
  function closeCommentaryTreeFor(slot: CommentarySlot) {
    if (isCommentaryTreeOpenFor(slot)) closeSidePanel()
  }

  return {
    sidePanelMode,
    commentaryTreeSlot,
    tocVisible,
    commentaryTreeVisible,
    sidePanelVisible,
    sidePanelToggleButtonEl,
    isCommentaryTreeOpenFor,
    toggleTocPanel,
    toggleCommentaryTreePanel,
    closeSidePanel,
    closeCommentaryTreeFor,
  }
}
