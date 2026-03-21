import { ref, computed, watch } from 'vue'
import { removeDiacriticsForSearch } from '@/utils/hebrewTextProcessing'
import type { CommentaryGroup } from './useCommentary'

export interface CommentaryMatch {
  flatIndex: number
  occurrenceInLine: number
}

export function useCommentarySearch(groups: () => CommentaryGroup[]) {
  const query = ref('')
  const currentMatchIdx = ref(0)

  const matches = computed<CommentaryMatch[]>(() => {
    const q = removeDiacriticsForSearch(query.value.trim())
    if (!q) return []
    const results: CommentaryMatch[] = []
    let flatIndex = 0
    for (const g of groups()) {
      flatIndex++ // header
      for (const line of g.lines) {
        const stripped = removeDiacriticsForSearch(line.content.replace(/<[^>]*>/g, ''))
        let idx = 0, occ = 0
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

  // keep backward-compat: unique flat indices that have matches
  const matchFlatIndices = computed(() => [...new Set(matches.value.map(m => m.flatIndex))])

  watch(matches, () => { currentMatchIdx.value = 0 })

  const matchCount = computed(() => matches.value.length)
  const currentMatch = computed(() => matches.value[currentMatchIdx.value] ?? null)
  const currentMatchFlatIndex = computed(() => currentMatch.value?.flatIndex ?? -1)
  const currentMatchOccurrence = computed(() => currentMatch.value?.occurrenceInLine ?? 0)

  function next() { if (matchCount.value) currentMatchIdx.value = (currentMatchIdx.value + 1) % matchCount.value }
  function prev() { if (matchCount.value) currentMatchIdx.value = (currentMatchIdx.value - 1 + matchCount.value) % matchCount.value }
  function clear() { query.value = '' }

  return { query, matchCount, currentMatchIdx, currentMatchFlatIndex, currentMatchOccurrence, matchFlatIndices, next, prev, clear }
}
