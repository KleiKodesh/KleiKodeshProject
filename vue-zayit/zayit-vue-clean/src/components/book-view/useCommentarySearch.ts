import { ref, computed, watch } from 'vue'
import { removeDiacriticsForSearch } from '@/utils/hebrewTextProcessing'
import type { CommentaryGroup } from './useCommentary'

export interface CommentaryMatch {
    flatIndex: number // index in flatItems array (line items only)
}

export function useCommentarySearch(groups: () => CommentaryGroup[]) {
    const query = ref('')
    const currentMatchIdx = ref(0)

    const matchFlatIndices = computed(() => {
        const q = removeDiacriticsForSearch(query.value.trim())
        if (!q) return []
        const results: number[] = []
        let flatIndex = 0
        for (const g of groups()) {
            flatIndex++ // header
            for (const line of g.lines) {
                const stripped = removeDiacriticsForSearch(line.content.replace(/<[^>]*>/g, ''))
                if (stripped.includes(q)) results.push(flatIndex)
                flatIndex++
            }
        }
        return results
    })

    watch(matchFlatIndices, () => { currentMatchIdx.value = 0 })

    const matchCount = computed(() => matchFlatIndices.value.length)
    const currentMatchFlatIndex = computed(() => matchFlatIndices.value[currentMatchIdx.value] ?? -1)

    function next() {
        if (!matchCount.value) return
        currentMatchIdx.value = (currentMatchIdx.value + 1) % matchCount.value
    }

    function prev() {
        if (!matchCount.value) return
        currentMatchIdx.value = (currentMatchIdx.value - 1 + matchCount.value) % matchCount.value
    }

    function clear() {
        query.value = ''
    }

    return { query, matchCount, currentMatchIdx, currentMatchFlatIndex, matchFlatIndices, next, prev, clear }
}
