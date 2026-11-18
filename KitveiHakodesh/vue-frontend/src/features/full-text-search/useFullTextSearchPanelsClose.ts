import { computed, type Ref } from 'vue'
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
  // Both panels are dropdowns under the search bar at every size, so both close on an
  // outside click - which also keeps them from being open together: the other panel's
  // button is outside.
  useDropdownClose(
    filterPanelRef,
    () => {
      if (isFilterOpen.value) isFilterOpen.value = false
    },
    { toggleButton: computed(() => searchBarRef.value?.filterBtnRef ?? null) },
  )

  // The toggle buttons have to be named, or clicking it while the panel is open would close
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
