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
    console.log(
      `[BookViewSearch] matches recomputed: query="${debouncedQuery.value.trim()}" rawQuery="${query.value.trim()}" linesLoaded=${debouncedLines.value.length} matchCount=${results.length}`,
    )
    return results
  })

  // keep backward-compat: unique line indices that have matches — removed (unused)

  watch(
    matches,
    (newMatches) => {
      if (!newMatches.length) {
        currentMatchIdx.value = 0
        console.log(`[BookViewSearch] matches watcher: no matches, reset to 0`)
        return
      }
      const cur = currentLineIndex()
      const nearestIdx = newMatches.findIndex((m) => m.lineIndex >= cur)
      const chosen = nearestIdx === -1 ? 0 : nearestIdx
      console.log(
        `[BookViewSearch] matches watcher: ${newMatches.length} matches, currentLine=${cur}, nearestIdx=${nearestIdx}, chosen=${chosen} (lineIndex=${newMatches[chosen]?.lineIndex})`,
      )
      currentMatchIdx.value = chosen
    },
    { flush: 'sync' },
  )

  const matchCount = computed(() => matches.value.length)
  const currentMatch = computed(() => matches.value[currentMatchIdx.value] ?? null)
  const currentMatchLineIndex = computed(() => currentMatch.value?.lineIndex ?? -1)
  const currentMatchOccurrence = computed(() => currentMatch.value?.occurrenceInLine ?? 0)

  function gotoNearestMatch() {
    const newMatches = matches.value
    const cur = currentLineIndex()
    console.log(
      `[BookViewSearch] gotoNearestMatch: matchCount=${newMatches.length} currentLine=${cur} debouncedQuery="${debouncedQuery.value.trim()}" rawQuery="${query.value.trim()}" linesLoaded=${debouncedLines.value.length}`,
    )
    if (!newMatches.length) return
    const nearestIdx = newMatches.findIndex((m) => m.lineIndex >= cur)
    const chosen = nearestIdx === -1 ? 0 : nearestIdx
    console.log(
      `[BookViewSearch] gotoNearestMatch: nearestIdx=${nearestIdx}, chosen=${chosen}, target lineIndex=${newMatches[chosen]?.lineIndex}`,
    )
    currentMatchIdx.value = chosen
  }

  function next() {
    if (matchCount.value) {
      const before = currentMatchIdx.value
      currentMatchIdx.value = (currentMatchIdx.value + 1) % matchCount.value
      console.log(`[BookViewSearch] next: ${before} → ${currentMatchIdx.value} (lineIndex=${matches.value[currentMatchIdx.value]?.lineIndex})`)
    }
  }
  function prev() {
    if (matchCount.value) {
      const before = currentMatchIdx.value
      currentMatchIdx.value = (currentMatchIdx.value - 1 + matchCount.value) % matchCount.value
      console.log(`[BookViewSearch] prev: ${before} → ${currentMatchIdx.value} (lineIndex=${matches.value[currentMatchIdx.value]?.lineIndex})`)
    }
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
