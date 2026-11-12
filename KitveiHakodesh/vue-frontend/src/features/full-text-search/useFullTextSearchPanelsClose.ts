import { computed, type Ref } from 'vue'
import { useMediaQuery } from '@vueuse/core'
import { useDropdownClose } from '@/composables/useDropdownClose'

/** A panel element, as useDropdownClose takes it. */
type PanelTarget = Parameters<typeof useDropdownClose>[0]

/** What the bar exposes: the two buttons that toggle the panels. */
interface PanelToggles {
  filterBtnRef?: HTMLElement | null
  optionsBtnRef?: HTMLElement | null
}

/** Outside-click closing of a full-text search page's filter and options panels. */
export function useFullTextSearchPanelsClose(refs: {
  searchBarRef: Ref<PanelToggles | null>
  filterPanelRef: PanelTarget
  optionsPanelRef: PanelTarget
  isFilterOpen: Ref<boolean>
  isOptionsOpen: Ref<boolean>
}) {
  const { searchBarRef, filterPanelRef, optionsPanelRef, isFilterOpen, isOptionsOpen } = refs
  // The filter panel closes on outside-click only in narrow "overlay" mode, where
  // it floats over the results. In wide mode (≥520px) it's a persistent side panel
  // beside the results, so clicking the results should not dismiss it.
  // Breakpoint mirrors the `@media (min-width: 520px)` layout switch in FullTextSearchLayout.
  const isOverlayMode = useMediaQuery('(max-width: 519.98px)')

  useDropdownClose(
    filterPanelRef,
    () => {
      if (isFilterOpen.value) isFilterOpen.value = false
    },
    {
      toggleButton: computed(() => searchBarRef.value?.filterBtnRef ?? null),
      enabled: isOverlayMode,
      // A full-height side panel, not a popup: the viewport fit would cap it 8px short
      // of the bottom edge (same opt-out as SidePanel).
      fitToViewport: false,
    },
  )

  // The options panel is a popup over the results in every size, so unlike the filter
  // panel it closes on outside-click unconditionally — no overlay-mode gate.
  //
  // The toggle button has to be named, or clicking it while the panel is open would close
  // it and then reopen it: onClickOutside runs on the CLICK, capture-phase on window, so
  // it fires before the button's own bubble-phase handler (@click.stop can't cancel a
  // capture listener that already ran). Naming the button makes the composable return
  // without closing, leaving the single close to the button's toggle. Nothing reads the
  // returned `justClosed` — that guard is for toggles that set the flag themselves.
  useDropdownClose(
    optionsPanelRef,
    () => {
      if (isOptionsOpen.value) isOptionsOpen.value = false
    },
    { toggleButton: computed(() => searchBarRef.value?.optionsBtnRef ?? null) },
  )
}
