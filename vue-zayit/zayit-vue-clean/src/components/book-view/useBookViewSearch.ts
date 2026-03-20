import { ref, computed, watch } from 'vue'
import { removeDiacriticsForSearch } from '@/utils/hebrewTextProcessing'
import type { LineItem } from './useLines'

export function useBookViewSearch(lines: () => LineItem[]) {
    const query = ref('')
    const currentMatchIdx = ref(0)

    const matchLineIndices = computed(() => {
        const q = removeDiacriticsForSearch(query.value.trim())
        if (!q) return []
        const results: number[] = []
        for (const line of lines()) {
            if (line.content === null) continue
            const stripped = removeDiacriticsForSearch(line.content.replace(/<[^>]*>/g, ''))
            if (stripped.includes(q)) results.push(line.lineIndex)
        }
        return results
    })

    watch(matchLineIndices, () => { currentMatchIdx.value = 0 })

    const matchCount = computed(() => matchLineIndices.value.length)
    const currentMatchLineIndex = computed(() => matchLineIndices.value[currentMatchIdx.value] ?? -1)

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

    return { query, matchCount, currentMatchIdx, currentMatchLineIndex, matchLineIndices, next, prev, clear }
}
