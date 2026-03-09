import { computed, type ComputedRef } from 'vue'
import { useCategoryTreeStore } from '@/data/stores/categoryTreeStore'
import { useConnectionTypesStore } from '@/data/stores/connectionTypesStore'
import type { CommentaryMetadata } from './useCommentaryContent'

export interface CommentaryTreeNode {
    type: "connection-type" | "book";
    name: string;
    hebrewName: string;
    connectionType?: string;
    category?: string;
    bookId?: number;
    lineIndex?: number;
    children: CommentaryTreeNode[];
    path: string[];
    metadata?: CommentaryMetadata;
}

export function useCommentaryTree(commentaryGroups: ComputedRef<CommentaryMetadata[]>) {
    const categoryTreeStore = useCategoryTreeStore()
    const connectionTypesStore = useConnectionTypesStore()

    // Helper function to sort categories
    function sortCategories(entries: [string, CommentaryMetadata[]][]): [string, CommentaryMetadata[]][] {
        return entries.sort((a, b) => {
            const [catA, groupsA] = a
            const [catB, groupsB] = b

            // Get book info to check if these are secondary categories
            const bookA = categoryTreeStore.allBooks.find(book => book.id === groupsA[0]?.targetBookId)
            const bookB = categoryTreeStore.allBooks.find(book => book.id === groupsB[0]?.targetBookId)

            // Check if these are secondary categories (for תנ"ך, משנה, תלמוד)
            const isSecondaryA = bookA?.secondaryCategory === catA
            const isSecondaryB = bookB?.secondaryCategory === catB

            // Priority order for secondary categories
            const secondaryCategoryOrder = ['תנ"ך', 'משנה', 'תוספתא', 'תלמוד']
            const secondaryIndexA = isSecondaryA ? secondaryCategoryOrder.indexOf(catA) : -1
            const secondaryIndexB = isSecondaryB ? secondaryCategoryOrder.indexOf(catB) : -1

            // Hardcoded period order (גאונים before ראשונים before אחרונים)
            const periodOrder = ['גאונים', 'ראשונים', 'אחרונים']
            const periodIndexA = periodOrder.indexOf(catA)
            const periodIndexB = periodOrder.indexOf(catB)

            // Check if A is secondary and B is period
            if (secondaryIndexA !== -1 && periodIndexB !== -1) return -1
            // Check if A is period and B is secondary
            if (periodIndexA !== -1 && secondaryIndexB !== -1) return 1

            // If both are secondary categories, sort by their order
            if (secondaryIndexA !== -1 && secondaryIndexB !== -1) {
                return secondaryIndexA - secondaryIndexB
            }

            // If both are periods, sort by period order
            if (periodIndexA !== -1 && periodIndexB !== -1) {
                return periodIndexA - periodIndexB
            }

            // Secondary categories come before other categories
            if (secondaryIndexA !== -1) return -1
            if (secondaryIndexB !== -1) return 1

            // Periods come before other categories
            if (periodIndexA !== -1) return -1
            if (periodIndexB !== -1) return 1

            // For other categories, use category tree order
            const orderA = isSecondaryA ? (bookA?.secondaryCategoryOrder ?? 999) : (bookA?.rootCategoryOrder ?? 999)
            const orderB = isSecondaryB ? (bookB?.secondaryCategoryOrder ?? 999) : (bookB?.rootCategoryOrder ?? 999)

            return orderA - orderB
        })
    }

    const commentaryTree = computed<CommentaryTreeNode[]>(() => {
        const tree: CommentaryTreeNode[] = []
        const groupedByConnectionType = new Map<string, CommentaryMetadata[]>()

        commentaryGroups.value.forEach(group => {
            if (!group.targetBookId || !group.connectionTypeId) return
            const connectionTypeName = connectionTypesStore.getConnectionTypeName(group.connectionTypeId)
            const connectionType = connectionTypeName || 'OTHER'
            if (!groupedByConnectionType.has(connectionType)) {
                groupedByConnectionType.set(connectionType, [])
            }
            groupedByConnectionType.get(connectionType)!.push(group)
        })

        // 1. Handle מקור
        const sourceGroups = groupedByConnectionType.get('SOURCE')
        if (sourceGroups?.length) {
            const sourceNode: CommentaryTreeNode = {
                type: 'connection-type' as const,
                name: 'SOURCE',
                hebrewName: 'מקור',
                children: sourceGroups.map(group => ({
                    type: 'book' as const,
                    name: group.groupName,
                    hebrewName: group.groupName,
                    bookId: group.targetBookId,
                    lineIndex: group.targetLineIndex,
                    children: [],
                    path: ['מקור', group.groupName],
                    metadata: group
                })).sort((a, b) => a.hebrewName.localeCompare(b.hebrewName, 'he')),
                path: ['מקור']
            }
            tree.push(sourceNode)
        }

        // 2. Handle קשרים (REFERENCE) - flat list
        const referenceGroups = groupedByConnectionType.get('REFERENCE')
        if (referenceGroups?.length) {
            const referenceNode: CommentaryTreeNode = {
                type: 'connection-type' as const,
                name: 'REFERENCE',
                hebrewName: 'קשרים',
                children: referenceGroups.map(group => ({
                    type: 'book' as const,
                    name: group.groupName,
                    hebrewName: group.groupName,
                    bookId: group.targetBookId,
                    lineIndex: group.targetLineIndex,
                    children: [],
                    path: ['קשרים', group.groupName],
                    metadata: group
                })).sort((a, b) => a.hebrewName.localeCompare(b.hebrewName, 'he')),
                path: ['קשרים']
            }
            tree.push(referenceNode)
        }

        // 3. Handle תרגומים
        const targumGroups = groupedByConnectionType.get('TARGUM')
        if (targumGroups?.length) {
            const targumNode: CommentaryTreeNode = {
                type: 'connection-type' as const,
                name: 'TARGUM',
                hebrewName: 'תרגומים',
                children: targumGroups.map(group => ({
                    type: 'book' as const,
                    name: group.groupName,
                    hebrewName: group.groupName,
                    bookId: group.targetBookId,
                    lineIndex: group.targetLineIndex,
                    children: [],
                    path: ['תרגומים', group.groupName],
                    metadata: group
                })).sort((a, b) => a.hebrewName.localeCompare(b.hebrewName, 'he')),
                path: ['תרגומים']
            }
            tree.push(targumNode)
        }

        // 4. Handle מפרשים (COMMENTARY) - group by categories
        const commentaryConnectionGroups = groupedByConnectionType.get('COMMENTARY')
        if (commentaryConnectionGroups?.length) {
            const groupedByCategory = new Map<string, CommentaryMetadata[]>()

            commentaryConnectionGroups.forEach(group => {
                const book = categoryTreeStore.allBooks.find(b => b.id === group.targetBookId)

                // First try to use period (ראשונים, אחרונים, גאונים)
                let categoryToUse = book?.period

                // If no period, use secondaryCategory for specific root categories
                if (!categoryToUse || categoryToUse === 'אחר') {
                    const rootCat = book?.rootCategory || 'אחר'
                    const shouldUseSecondary = rootCat === 'תנ"ך' || rootCat === 'משנה' || rootCat === 'תלמוד'

                    categoryToUse = (shouldUseSecondary && book?.secondaryCategory)
                        ? book.secondaryCategory
                        : rootCat
                }

                if (!groupedByCategory.has(categoryToUse)) {
                    groupedByCategory.set(categoryToUse, [])
                }
                groupedByCategory.get(categoryToUse)!.push(group)
            })

            // Create a node for each category, sorted by category tree order
            const sortedCategories = sortCategories(Array.from(groupedByCategory.entries()))

            sortedCategories.forEach(([category, groups]) => {
                const categoryNode: CommentaryTreeNode = {
                    type: 'connection-type' as const,
                    name: category,
                    hebrewName: `מפרשים - ${category}`,
                    children: groups.map(group => ({
                        type: 'book' as const,
                        name: group.groupName,
                        hebrewName: group.groupName,
                        category: category,
                        bookId: group.targetBookId,
                        lineIndex: group.targetLineIndex,
                        children: [],
                        path: [`מפרשים - ${category}`, group.groupName],
                        metadata: group
                    })).sort((a, b) => a.hebrewName.localeCompare(b.hebrewName, 'he')),
                    path: [`מפרשים - ${category}`]
                }
                tree.push(categoryNode)
            })
        }

        // 5. Handle שונות (OTHER) - group by categories (same as COMMENTARY)
        const otherGroups = groupedByConnectionType.get('OTHER')
        if (otherGroups?.length) {
            const groupedByCategory = new Map<string, CommentaryMetadata[]>()

            otherGroups.forEach(group => {
                const book = categoryTreeStore.allBooks.find(b => b.id === group.targetBookId)

                // First try to use period (ראשונים, אחרונים, גאונים)
                let categoryToUse = book?.period

                // If no period, use secondaryCategory for specific root categories
                if (!categoryToUse || categoryToUse === 'אחר') {
                    const rootCat = book?.rootCategory || 'אחר'
                    const shouldUseSecondary = rootCat === 'תנ"ך' || rootCat === 'משנה' || rootCat === 'תלמוד'

                    categoryToUse = (shouldUseSecondary && book?.secondaryCategory)
                        ? book.secondaryCategory
                        : rootCat
                }

                if (!groupedByCategory.has(categoryToUse)) {
                    groupedByCategory.set(categoryToUse, [])
                }
                groupedByCategory.get(categoryToUse)!.push(group)
            })

            // Create a node for each category, sorted by category tree order
            const sortedCategories = sortCategories(Array.from(groupedByCategory.entries()))

            sortedCategories.forEach(([category, groups]) => {
                const categoryNode: CommentaryTreeNode = {
                    type: 'connection-type' as const,
                    name: category,
                    hebrewName: `שונות - ${category}`,
                    children: groups.map(group => ({
                        type: 'book' as const,
                        name: group.groupName,
                        hebrewName: group.groupName,
                        category: category,
                        bookId: group.targetBookId,
                        lineIndex: group.targetLineIndex,
                        children: [],
                        path: [`שונות - ${category}`, group.groupName],
                        metadata: group
                    })).sort((a, b) => a.hebrewName.localeCompare(b.hebrewName, 'he')),
                    path: [`שונות - ${category}`]
                }
                tree.push(categoryNode)
            })
        }

        return tree
    })

    // Flatten tree to get sorted list of book nodes
    const flattenedBooks = computed<CommentaryTreeNode[]>(() => {
        const books: CommentaryTreeNode[] = []

        function traverse(node: CommentaryTreeNode) {
            if (node.type === 'book') {
                books.push(node)
            }
            node.children.forEach(traverse)
        }

        commentaryTree.value.forEach(traverse)

        return books
    })

    return {
        commentaryTree,
        flattenedBooks
    }
}
