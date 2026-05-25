import { ref, computed, watch } from 'vue'
import { refDebounced } from '@vueuse/core'
import { removeDiacriticsForSearch } from '@/utils/hebrewTextProcessing'
import type { LineItem } from './lines/useBookViewLinesTable'

export interface BookViewMatch {
  lineIndex: number
  occurrenceInLine: number
}

export function useBookViewSearch(
  lines: () => LineItem[],
  currentLineIndex: () => number = () => 0,
) {
  const query = ref('')
  // Debounce the query — avoids re-scanning on every keystroke.
  // Also debounce lines so chunk-by-chunk arrivals during book load don't trigger
  // a full re-scan on every chunk when a search query is already active.
  const debouncedQuery = refDebounced(query, 150)
  const debouncedLines = refDebounced(
    computed(() => (debouncedQuery.value ? lines() : [])),
    150,
  )
  const currentMatchIdx = ref(0)

  const matches = computed<BookViewMatch[]>(() => {
    const q = removeDiacriticsForSearch(debouncedQuery.value.trim())
    if (!q) return []
    const results: BookViewMatch[] = []
    for (const line of debouncedLines.value) {
      if (line.content === null) continue
      const stripped = removeDiacriticsForSearch(line.content.replace(/<[^>]*>/g, ''))
      let idx = 0,
        occ = 0
      while ((idx = stripped.indexOf(q, idx)) !== -1) {
        results.push({ lineIndex: line.lineIndex, occurrenceInLine: occ })
        occ++
        idx++
      }
    }
    return results
  })

  // keep backward-compat: unique line indices that have matches — removed (unused)

  watch(
    matches,
    (newMatches) => {
      if (!newMatches.length) {
        currentMatchIdx.value = 0
        return
      }
      const cur = currentLineIndex()
      const nearestIdx = newMatches.findIndex((m) => m.lineIndex >= cur)
      currentMatchIdx.value = nearestIdx === -1 ? 0 : nearestIdx
    },
    { flush: 'sync' },
  )

  const matchCount = computed(() => matches.value.length)
  const currentMatch = computed(() => matches.value[currentMatchIdx.value] ?? null)
  const currentMatchLineIndex = computed(() => currentMatch.value?.lineIndex ?? -1)
  const currentMatchOccurrence = computed(() => currentMatch.value?.occurrenceInLine ?? 0)

  function gotoNearestMatch() {
    const newMatches = matches.value
    if (!newMatches.length) return
    const cur = currentLineIndex()
    const nearestIdx = newMatches.findIndex((m) => m.lineIndex >= cur)
    currentMatchIdx.value = nearestIdx === -1 ? 0 : nearestIdx
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
