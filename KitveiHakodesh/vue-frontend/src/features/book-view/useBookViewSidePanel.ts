/**
 * The book view's side panel: the table of contents.
 *
 * It used to host the commentary filter trees too, which forced two compromises -
 * only one panel's tree could be open at a time, and opening a tree closed the
 * TOC. Each commentary panel now renders its own tree (see useCommentaryPanelSlot),
 * so this owns nothing but the TOC.
 */
import { ref, computed } from 'vue'

type ToolbarInstance = { tocBtnRef: HTMLElement | null }

export function useBookViewSidePanel(toolbarRef: () => ToolbarInstance | null) {
  const tocVisible = ref(false)

  // The element useDropdownClose must ignore, so that clicking the button which
  // opened the panel closes it instead of being swallowed as a click-outside.
  const sidePanelToggleButtonEl = computed(() => toolbarRef()?.tocBtnRef ?? null)

  function toggleTocPanel() {
    tocVisible.value = !tocVisible.value
  }

  function closeSidePanel() {
    tocVisible.value = false
  }

  return {
    tocVisible,
    /** The panel is the TOC and nothing else, so this tracks it exactly. */
    sidePanelVisible: tocVisible,
    sidePanelToggleButtonEl,
    toggleTocPanel,
    closeSidePanel,
  }
}
