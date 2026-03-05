/**
 * Commentary Filters Composable
 * Handles unified filtering and categorization of commentary groups
 */

import { ref, computed, type ComputedRef } from 'vue'
import { useCategoryTreeStore } from '@/data/stores/categoryTreeStore'
import { useConnectionTypesStore } from '@/data/stores/connectionTypesStore'
import type { CommentaryLinkGroup } from '@/data/services/bookCommentaryService'
import type { ComboboxOption } from '@/components/shared/Combobox.vue'
import type { Book } from '@/data/types/Book'

export function useCommentaryFilters(
    sortedLinkGroups: ComputedRef<CommentaryLinkGroup[]>,
    selectedConnectionTypeId: ComputedRef<number | undefined>,
    commentaryBookIds: ComputedRef<number[]>,
    book: ComputedRef<Book | undefined>
) {
    const selectedCategoryFilter = ref<string | null>(null)
    const categoryTreeStore = useCategoryTreeStore()
    const connectionTypesStore = useConnectionTypesStore()

    // Check if current connection type is COMMENTARY
    const isCommentaryType = computed(() => {
        if (!selectedConnectionTypeId.value) return false
        const connectionTypeName = connectionTypesStore.getConnectionTypeName(selectedConnectionTypeId.value)
        return connectionTypeName === 'COMMENTARY'
    })

    // Filter groups based on selected category
    const filteredGroupOptions = computed<ComboboxOption[]>(() => {
        if (selectedCategoryFilter.value) {
            const filtered = sortedLinkGroups.value
                .map((group, index) => {
                    const targetBook = group.targetBookId ? categoryTreeStore.allBooks.find(b => b.id === group.targetBookId) : null
                    const period = targetBook?.period || 'אחר'
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

    // Build unified category list: priority connection types, commentary periods, then other types
    const availableCategories = computed(() => {
        console.log('🏷️ [CommentaryFilters] Building categories:', {
            commentaryBookIdsCount: commentaryBookIds.value.length,
            commentaryBookIds: commentaryBookIds.value,
            hasBook: !!book.value
        })

        const categories: string[] = []

        // Add priority connection types first (מקור, תרגום)
        if (book.value) {
            const priorityTypes = [
                { flag: book.value.hasSourceConnection, name: 'SOURCE' },   // מקור
                { flag: book.value.hasTargumConnection, name: 'TARGUM' }    // תרגום
            ]

            priorityTypes.forEach(({ flag, name }) => {
                if (flag > 0) {
                    const label = connectionTypesStore.getHebrewLabel(name)
                    categories.push(label)
                }
            })
        }

        // Then add commentary subcategories (periods) from commentary book IDs
        if (commentaryBookIds.value.length > 0) {
            const periods = new Set<string>()

            commentaryBookIds.value.forEach(bookId => {
                const targetBook = categoryTreeStore.allBooks.find(b => b.id === bookId)
                const period = targetBook?.period || 'אחר'
                periods.add(period)
            })

            const periodOrder = ['תנ"ך', 'ספרות חז"ל', 'גאונים', 'ראשונים', 'אחרונים', 'קבלה', 'מוסר וחסידות', 'הלכה', 'אחר']
            const availablePeriods = periodOrder.filter(p => periods.has(p))
            console.log('📅 [CommentaryFilters] Commentary periods found:', availablePeriods)
            categories.push(...availablePeriods)
        }

        // Finally add other connection types at the end (קשרים, שונות)
        if (book.value) {
            const otherTypes = [
                { flag: book.value.hasReferenceConnection, name: 'REFERENCE' },  // קשרים
                { flag: book.value.hasOtherConnection, name: 'OTHER' }           // שונות
            ]

            otherTypes.forEach(({ flag, name }) => {
                if (flag > 0) {
                    const label = connectionTypesStore.getHebrewLabel(name)
                    categories.push(label)
                }
            })
        }

        console.log('✅ [CommentaryFilters] Final categories:', categories)
        return categories
    })

    return {
        selectedCategoryFilter,
        filteredGroupOptions,
        availableCategories,
        isCommentaryType
    }
}
