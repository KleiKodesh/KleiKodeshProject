import { ref, watch, type Ref } from 'vue'
import { useIntervalFn } from '@vueuse/core'

const SEARCH_PLACEHOLDERS = [
  'חיפוש מהיר בכל המאגרים...',
  'לחץ אנטר לחיפוש תוכן במאגר',
  'הקלד חופשי לחיפוש ספר או קובץ',
  'כדי להקדים תוצאות מהיברו בוקס כתוב',
  'היברו בוקס: שבת',
  'או היברו: שבת',
  'או: \\ שבת',
  'כדי להקדים תוצאות מהמחשב כתוב',
  'קובץ: ברכות',
  'או מחשב: ברכות',
  'או: \\\\ ברכות',
]

const MINIMUM_QUERY_LENGTH = 2

/**
 * Types the hero search bar's placeholder out character by character, cycling
 * through the hint phrases. Pauses while the user has typed something.
 */
function useAnimatedPlaceholder(query: Ref<string>) {
  const placeholder = ref(SEARCH_PLACEHOLDERS[0]!)
  let phraseIndex = 0
  let charIndex = 0
  let pauseTicks = 0

  const { pause, resume } = useIntervalFn(() => {
    if (pauseTicks > 0) {
      pauseTicks--
      return
    }
    const target = SEARCH_PLACEHOLDERS[phraseIndex]!
    if (charIndex < target.length) {
      placeholder.value = target.slice(0, ++charIndex)
    } else {
      pauseTicks = 12
      phraseIndex = (phraseIndex + 1) % SEARCH_PLACEHOLDERS.length
      charIndex = 0
    }
  }, 80)

  watch(query, (value) => (value ? pause() : resume()))

  return placeholder
}

/**
 * Owns the hero search bar's dropdown: when it is open, where it is anchored,
 * and the keyboard handling on the input. Result fetching lives in
 * `useHomeSearch`; navigation on select lives in `useHomeSearchNavigation`.
 *
 * This composable deliberately knows nothing about navigation — it reports what
 * the user did (`onSubmit`, `onRequestDropdownFocus`) and lets the page decide.
 * Depending on `useHomeSearchNavigation` here would create a cycle, since that
 * composable needs `reset` from this one.
 *
 * The anchor is computed once on open rather than tracked reactively — reactive
 * tracking would update on every scroll and fight the dropdown's own scrollTop.
 */
export function useHomeSearchBar(options: {
  query: Ref<string>
  searchBarRef: Ref<HTMLElement | null>
  hasAnyResults: () => boolean
  isLoadingAny: () => boolean
  clearResults: () => void
  /** Called with the trimmed query when the user presses Enter or clicks search. */
  onSubmit: (query: string) => void
  /** Called when Arrow Up/Down should move focus into the open dropdown. */
  onRequestDropdownFocus: () => void
}) {
  const { query, searchBarRef, hasAnyResults, isLoadingAny, clearResults } = options

  const placeholder = useAnimatedPlaceholder(query)

  const isDropdownOpen = ref(false)
  const anchorTop = ref(0)
  const anchorLeft = ref(0)
  const anchorRight = ref(0)
  const maxHeight = ref(300)

  function computeAnchor() {
    if (!searchBarRef.value) return
    const rect = searchBarRef.value.getBoundingClientRect()
    anchorTop.value = rect.bottom + 6
    anchorLeft.value = rect.left
    anchorRight.value = window.innerWidth - rect.right
    maxHeight.value = Math.max(120, window.innerHeight - rect.bottom - 12)
  }

  function hasMinimumQuery() {
    return (query.value ?? '').trim().length >= MINIMUM_QUERY_LENGTH
  }

  /** Hide the dropdown, leaving the query and results intact. */
  function close() {
    isDropdownOpen.value = false
  }

  /** Hide the dropdown and clear both the query and the fetched results. */
  function reset() {
    close()
    clearResults()
    query.value = ''
  }

  function onFocus() {
    if (hasAnyResults()) {
      computeAnchor()
      isDropdownOpen.value = true
    }
  }

  function onInput() {
    const ready = hasMinimumQuery()
    if (ready) computeAnchor()
    isDropdownOpen.value = ready && (hasAnyResults() || isLoadingAny())
  }

  function submit() {
    const trimmed = query.value.trim()
    if (!trimmed) return
    // Capture before reset() blanks the query, then hand off to the page.
    reset()
    options.onSubmit(trimmed)
  }

  function onKeydown(event: KeyboardEvent) {
    if (event.code === 'Enter') {
      event.preventDefault()
      submit()
      return
    }
    if (event.code === 'Escape') {
      event.preventDefault()
      reset()
      return
    }
    if (!isDropdownOpen.value) return
    if (event.code === 'ArrowDown' || event.code === 'ArrowUp') {
      event.preventDefault()
      options.onRequestDropdownFocus()
    }
  }

  /**
   * Async sources (HebrewBooks, files) resolve after the debounce — open the
   * dropdown once they land, or close it if they all came back empty.
   * Pass the result refs whose arrival should reveal the dropdown.
   */
  function openWhenAsyncResultsArrive(sources: Array<Ref<unknown>>) {
    watch(sources, () => {
      if (!hasMinimumQuery()) return
      if (hasAnyResults()) isDropdownOpen.value = true
      else if (!isLoadingAny()) isDropdownOpen.value = false
    })
  }

  return {
    placeholder,
    isDropdownOpen,
    anchorTop,
    anchorLeft,
    anchorRight,
    maxHeight,
    computeAnchor,
    close,
    reset,
    submit,
    onFocus,
    onInput,
    onKeydown,
    openWhenAsyncResultsArrive,
  }
}
