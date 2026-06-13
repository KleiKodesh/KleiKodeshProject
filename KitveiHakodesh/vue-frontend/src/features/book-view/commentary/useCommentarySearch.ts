import { ref, computed, watch } from 'vue'
import { refDebounced } from '@vueuse/core'
import { removeDiacriticsForSearch, stripHtmlForSearch } from '@/utils/hebrewTextProcessing'
import type { CommentaryGroup } from './useCommentary'

export interface CommentaryMatch {
  flatIndex: number
  occurrenceInLine: number
}

export function useCommentarySearch(
  groups: () => CommentaryGroup[],
  currentFlatIndex: () => number = () => 0,
) {
  const query = ref('')
  // Debounce the scan — avoids re-scanning all commentary lines on every keystroke.
  const debouncedQuery = refDebounced(query, 150)
  const currentMatchIdx = ref(0)

  const matches = computed<CommentaryMatch[]>(() => {
    const q = removeDiacriticsForSearch(debouncedQuery.value.trim())
    if (!q) return []
    const results: CommentaryMatch[] = []
    let flatIndex = 0
    for (const g of groups()) {
      flatIndex++ // header
      for (const line of g.lines) {
        const stripped = stripHtmlForSearch(line.content)
        let idx = 0,
          occ = 0
        while ((idx = stripped.indexOf(q, idx)) !== -1) {
          results.push({ flatIndex, occurrenceInLine: occ })
          occ++
          idx++
        }
        flatIndex++
      }
    }
    return results
  })

  // keep backward-compat: unique flat indices that have matches — removed (unused)

  watch(
    matches,
    (newMatches) => {
      if (!newMatches.length) {
        currentMatchIdx.value = 0
        return
      }
      const cur = currentFlatIndex()
      const nearestIdx = newMatches.findIndex((m) => m.flatIndex >= cur)
      currentMatchIdx.value = nearestIdx === -1 ? 0 : nearestIdx
    },
    { flush: 'sync' },
  )

  const matchCount = computed(() => matches.value.length)
  const currentMatch = computed(() => matches.value[currentMatchIdx.value] ?? null)
  const currentMatchFlatIndex = computed(() => currentMatch.value?.flatIndex ?? -1)
  const currentMatchOccurrence = computed(() => currentMatch.value?.occurrenceInLine ?? 0)

  function gotoNearestMatch() {
    const newMatches = matches.value
    if (!newMatches.length) return
    const cur = currentFlatIndex()
    const nearestIdx = newMatches.findIndex((m) => m.flatIndex >= cur)
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
    currentMatchFlatIndex,
    currentMatchOccurrence,
    next,
    prev,
    clear,
    gotoNearestMatch,
  }
}
