import { ref, computed, watch } from 'vue'
import { refDebounced } from '@vueuse/core'
import { removeDiacriticsForSearch } from '@/utils/hebrewTextProcessing'
import type { LineItem } from './lines/useBookViewLinesTable'

export interface BookViewMatch {
  lineIndex: number
  occurrenceInLine: number
}

// Lines scanned per async chunk before yielding to the event loop.
// 500 is fast enough to feel immediate on small books but yields often
// enough to keep the UI responsive on large books.
const SCAN_CHUNK_SIZE = 500

export function useBookViewSearch(
  lines: () => LineItem[],
  currentLineIndex: () => number = () => 0,
) {
  const query = ref('')
  // Debounce the query to avoid re-scanning on every keystroke.
  const debouncedQuery = refDebounced(query, 150)

  const currentMatchIdx = ref(0)
  const matches = ref<BookViewMatch[]>([])

  // Token used to cancel an in-flight scan when a new query arrives.
  let currentScanToken = 0

  async function runScan(scanQuery: string) {
    const token = ++currentScanToken
    const normalizedQuery = removeDiacriticsForSearch(scanQuery.trim())

    if (!normalizedQuery) {
      matches.value = []
      currentMatchIdx.value = 0
      return
    }

    const accumulated: BookViewMatch[] = []
    const allLines = lines()
    let linePosition = 0

    while (linePosition < allLines.length) {
      // Yield to the event loop between chunks so the UI stays responsive.
      await new Promise<void>((resolve) => setTimeout(resolve, 0))

      // A newer scan started while we were yielded — abort this one.
      if (token !== currentScanToken) return

      const end = Math.min(linePosition + SCAN_CHUNK_SIZE, allLines.length)
      for (let i = linePosition; i < end; i++) {
        const line = allLines[i]
        if (line.content === null) continue
        const stripped = removeDiacriticsForSearch(line.content.replace(/<[^>]*>/g, ''))
        let characterIndex = 0
        let occurrenceInLine = 0
        while ((characterIndex = stripped.indexOf(normalizedQuery, characterIndex)) !== -1) {
          accumulated.push({ lineIndex: line.lineIndex, occurrenceInLine })
          occurrenceInLine++
          characterIndex++
        }
      }

      // Publish partial results after each chunk so the counter updates live.
      matches.value = [...accumulated]
      linePosition = end
    }

    if (token !== currentScanToken) return

    // After the full scan completes, anchor the current match to the nearest
    // line at or after the current scroll position.
    if (accumulated.length) {
      const cursor = currentLineIndex()
      const nearestIndex = accumulated.findIndex((m) => m.lineIndex >= cursor)
      currentMatchIdx.value = nearestIndex === -1 ? 0 : nearestIndex
    } else {
      currentMatchIdx.value = 0
    }
  }

  // Re-run the scan whenever the debounced query changes.
  watch(debouncedQuery, (newQuery) => {
    runScan(newQuery)
  })

  // Also re-scan when new line chunks arrive while a query is active,
  // but debounce the lines getter to avoid re-triggering on every chunk.
  const debouncedLines = refDebounced(
    computed(() => (debouncedQuery.value ? lines() : [])),
    150,
  )
  watch(debouncedLines, () => {
    if (debouncedQuery.value) runScan(debouncedQuery.value)
  })

  const matchCount = computed(() => matches.value.length)
  const currentMatch = computed(() => matches.value[currentMatchIdx.value] ?? null)
  const currentMatchLineIndex = computed(() => currentMatch.value?.lineIndex ?? -1)
  const currentMatchOccurrence = computed(() => currentMatch.value?.occurrenceInLine ?? 0)

  function gotoNearestMatch() {
    const allMatches = matches.value
    if (!allMatches.length) return
    const cursor = currentLineIndex()
    const nearestIndex = allMatches.findIndex((m) => m.lineIndex >= cursor)
    currentMatchIdx.value = nearestIndex === -1 ? 0 : nearestIndex
  }

  function next() {
    if (matchCount.value) currentMatchIdx.value = (currentMatchIdx.value + 1) % matchCount.value
  }
  function prev() {
    if (matchCount.value)
      currentMatchIdx.value = (currentMatchIdx.value - 1 + matchCount.value) % matchCount.value
  }
  function clear() {
    query.value = ''
    currentScanToken++ // cancel any in-flight scan
    matches.value = []
    currentMatchIdx.value = 0
  }

  return {
    query,
    matchCount,
    currentMatchIdx,
    currentMatchLineIndex,
    currentMatchOccurrence,
    next,
    prev,
    clear,
    gotoNearestMatch,
  }
}
