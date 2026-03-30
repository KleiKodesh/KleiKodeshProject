import { ref, computed, watch } from 'vue'
import { removeDiacriticsForSearch } from '@/utils/hebrewTextProcessing'
import type { LineItem } from './useLines'

export interface BookViewMatch {
  lineIndex: number
  occurrenceInLine: number
}

export function useBookViewSearch(
  lines: () => LineItem[],
  currentLineIndex: () => number = () => 0,
) {
  const query = ref('')
  const currentMatchIdx = ref(0)

  const matches = computed<BookViewMatch[]>(() => {
    const q = removeDiacriticsForSearch(query.value.trim())
    if (!q) return []
    const results: BookViewMatch[] = []
    for (const line of lines()) {
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

  // keep backward-compat: unique line indices that have matches
  const matchLineIndices = computed(() => [...new Set(matches.value.map((m) => m.lineIndex))])

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
    matchLineIndices,
    next,
    prev,
    clear,
  }
}
