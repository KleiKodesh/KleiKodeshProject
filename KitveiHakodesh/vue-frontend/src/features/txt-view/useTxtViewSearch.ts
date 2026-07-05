import { ref, computed, watch } from 'vue'
import { refDebounced } from '@vueuse/core'
import { removeDiacriticsForSearch, stripHtmlForSearch } from '@/utils/hebrewTextProcessing'

export interface TxtViewMatch {
  lineIndex: number
  occurrenceInLine: number
}

const SCAN_CHUNK_SIZE = 2000

// Sentinel meaning "no match selected yet" — highlights are shown but none is current
const NO_SELECTION = -1

export function useTxtViewSearch(lines: () => string[], getCurrentLineIndex: () => number) {
  const query = ref('')
  const debouncedQuery = refDebounced(query, 150)

  // NO_SELECTION while typing — first Enter picks the closest match to scroll position
  const currentMatchIndex = ref(NO_SELECTION)
  const matches = ref<TxtViewMatch[]>([])

  let currentScanToken = 0

  async function runScan(scanQuery: string) {
    const token = ++currentScanToken
    const normalizedQuery = removeDiacriticsForSearch(scanQuery.trim())

    if (!normalizedQuery) {
      matches.value = []
      currentMatchIndex.value = NO_SELECTION
      return
    }

    const accumulated: TxtViewMatch[] = []
    const allLines = lines()
    let position = 0

    while (position < allLines.length) {
      await new Promise<void>((resolve) => setTimeout(resolve, 0))
      if (token !== currentScanToken) return

      const end = Math.min(position + SCAN_CHUNK_SIZE, allLines.length)
      for (let i = position; i < end; i++) {
        const normalizedLine = stripHtmlForSearch(allLines[i]!)
        let characterIndex = 0
        let occurrenceInLine = 0
        while ((characterIndex = normalizedLine.indexOf(normalizedQuery, characterIndex)) !== -1) {
          accumulated.push({ lineIndex: i, occurrenceInLine })
          occurrenceInLine++
          characterIndex++
        }
      }

      matches.value = [...accumulated]
      position = end
    }

    if (token !== currentScanToken) return
    // Stay unselected — user must press Enter to select the closest result
    currentMatchIndex.value = NO_SELECTION
  }

  watch(debouncedQuery, (newQuery) => runScan(newQuery))

  const matchCount = computed(() => matches.value.length)
  const currentMatch = computed(() =>
    currentMatchIndex.value === NO_SELECTION ? null : (matches.value[currentMatchIndex.value] ?? null),
  )
  const currentMatchLineIndex = computed(() => currentMatch.value?.lineIndex ?? -1)
  const currentMatchOccurrence = computed(() => currentMatch.value?.occurrenceInLine ?? -1)

  function findClosestMatchIndex(): number {
    if (!matches.value.length) return NO_SELECTION
    const scrollLineIndex = getCurrentLineIndex()
    // Find first match at or after the current scroll position
    const forwardIndex = matches.value.findIndex((m) => m.lineIndex >= scrollLineIndex)
    return forwardIndex !== -1 ? forwardIndex : 0
  }

  function next() {
    if (!matchCount.value) return
    if (currentMatchIndex.value === NO_SELECTION) {
      currentMatchIndex.value = findClosestMatchIndex()
    } else {
      currentMatchIndex.value = (currentMatchIndex.value + 1) % matchCount.value
    }
  }

  function previous() {
    if (!matchCount.value) return
    if (currentMatchIndex.value === NO_SELECTION) {
      currentMatchIndex.value = findClosestMatchIndex()
    } else {
      currentMatchIndex.value = (currentMatchIndex.value - 1 + matchCount.value) % matchCount.value
    }
  }

  function clear() {
    query.value = ''
    currentScanToken++
    matches.value = []
    currentMatchIndex.value = NO_SELECTION
  }

  return {
    query,
    matchCount,
    currentMatchIndex,
    currentMatchLineIndex,
    currentMatchOccurrence,
    next,
    previous,
    clear,
  }
}
