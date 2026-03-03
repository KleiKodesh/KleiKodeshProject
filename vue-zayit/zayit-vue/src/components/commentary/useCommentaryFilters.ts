/**
 * Commentary Filters Composable
 * Handles filtering and categorization of commentary groups
 */

import { ref, computed, watch, nextTick, type ComputedRef } from 'vue'
import { useCategoryTreeStore } from '@/data/stores/categoryTreeStore'
import type { CommentaryLinkGroup } from '@/data/services/bookCommentaryService'
import type { ComboboxOption } from '@/components/shared/Combobox.vue'

export function useCommentaryFilters(sortedLinkGroups: ComputedRef<CommentaryLinkGroup[]>) {
    const selectedCategoryFilter = ref<string | null>(null)

    const filteredGroupOptions = computed<ComboboxOption[]>(() => {
        const categoryTreeStore = useCategoryTreeStore()

        if (selectedCategoryFilter.value) {
            const filtered = sortedLinkGroups.value
                .map((group, index) => {
                    const book = group.targetBookId ? categoryTreeStore.allBooks.find(b => b.id === group.targetBookId) : null
                    const period = book?.period || 'אחר'
                    return { group, index, period }
                })
                .filter(item => item.period === selectedCategoryFilter.value)

            filtered.sort((a, b) => a.group.groupName.localeCompare(b.group.groupName, 'he'))

            return filtered.map(({ group, index }) => ({
                label: group.groupName,
                value: index
            }))
        }

        const allItems = sortedLinkGroups.value.map((group, index) => ({
            label: group.groupName,
            value: index
        }))

        allItems.sort((a, b) => a.label.localeCompare(b.label, 'he'))

        return allItems
    })

    const availableCategories = computed(() => {
        const categoryTreeStore = useCategoryTreeStore()
        const categories = new Set<string>()

        sortedLinkGroups.value.forEach((group) => {
            const book = group.targetBookId ? categoryTreeStore.allBooks.find(b => b.id === group.targetBookId) : null
            const period = book?.period || 'אחר'
            categories.add(period)
        })

        const periodOrder = ['תנ"ך', 'ספרות חז"ל', 'גאונים', 'ראשונים', 'אחרונים', 'קבלה', 'מוסר וחסידות', 'הלכה', 'אחר']
        return periodOrder.filter(p => categories.has(p))
    })

    return {
        selectedCategoryFilter,
        filteredGroupOptions,
        availableCategories
    }
}
