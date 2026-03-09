import { computed, type ComputedRef } from 'vue'
import { useCategoryTreeStore } from '@/data/stores/categoryTreeStore'
import { useConnectionTypesStore } from '@/data/stores/connectionTypesStore'
import type { CommentaryMetadata } from './useCommentaryContent'

export interface CommentaryTreeNode {
    type: 'connection-type' | 'period' | 'book'
    name: string
    hebrewName: string
    connectionType?: string
    period?: string
    bookId?: number
    lineIndex?: number
    children: CommentaryTreeNode[]
    path: string[]
    metadata?: CommentaryMetadata
}

export function useCommentaryTree(commentaryGroups: ComputedRef<CommentaryMetadata[]>) {
    const categoryTreeStore = useCategoryTreeStore()
    const connectionTypesStore = useConnectionTypesStore()

    const connectionTypeOrder = [
        { name: 'SOURCE', hebrewName: 'מקור' },
        { name: 'TARGUM', hebrewName: 'תרגומים' },
        { name: 'COMMENTARY', hebrewName: 'מפרשים' },
        { name: 'REFERENCE', hebrewName: 'קשרים' },
        { name: 'OTHER', hebrewName: 'שונות' }
    ]

    const commentaryTree = computed<CommentaryTreeNode[]>(() => {
        const tree: CommentaryTreeNode[] = []

        // Group commentaries by connection type
        const groupedByConnectionType = new Map<string, CommentaryMetadata[]>()

        commentaryGroups.value.forEach(group => {
            if (!group.targetBookId || !group.connectionTypeId) return

            // Get the connection type name from the connectionTypeId
            const connectionTypeName = connectionTypesStore.getConnectionTypeName(group.connectionTypeId)
            const connectionType = connectionTypeName || 'OTHER'

            if (!groupedByConnectionType.has(connectionType)) {
                groupedByConnectionType.set(connectionType, [])
            }
            groupedByConnectionType.get(connectionType)!.push(group)
        })

        // Build tree following connection type order
        connectionTypeOrder.forEach(({ name: connectionType, hebrewName }) => {
            const groups = groupedByConnectionType.get(connectionType)
            if (!groups || groups.length === 0) return

            const connectionTypeNode: CommentaryTreeNode = {
                type: 'connection-type',
                name: connectionType,
                hebrewName,
                connectionType,
                children: [],
                path: [hebrewName]
            }

            // For COMMENTARY type, create combined nodes (e.g., "מפרשים - ראשונים")
            if (connectionType === 'COMMENTARY') {
                const groupedByPeriod = new Map<string, CommentaryMetadata[]>()

                groups.forEach(group => {
                    const book = categoryTreeStore.allBooks.find(b => b.id === group.targetBookId)
                    const period = book?.period || 'אחר'

                    if (!groupedByPeriod.has(period)) {
                        groupedByPeriod.set(period, [])
                    }
                    groupedByPeriod.get(period)!.push(group)
                })

                const periodOrder = ['תנ"ך', 'ספרות חז"ל', 'גאונים', 'ראשונים', 'אחרונים', 'קבלה', 'מוסר וחסידות', 'הלכה', 'אחר']

                periodOrder.forEach(period => {
                    const periodGroups = groupedByPeriod.get(period)
                    if (!periodGroups || periodGroups.length === 0) return

                    // Use simplified name: "חז"ל" instead of "ספרות חז"ל", and just the period name for others
                    const displayName = period === 'ספרות חז"ל' ? 'חז"ל' : period

                    const periodNode: CommentaryTreeNode = {
                        type: 'period',
                        name: displayName,
                        hebrewName: displayName,
                        connectionType,
                        period,
                        children: [],
                        path: [displayName]
                    }

                    periodGroups.forEach(group => {
                        const bookNode: CommentaryTreeNode = {
                            type: 'book',
                            name: group.groupName,
                            hebrewName: group.groupName,
                            connectionType,
                            period,
                            bookId: group.targetBookId,
                            lineIndex: group.targetLineIndex,
                            children: [],
                            path: [displayName, group.groupName],
                            metadata: group
                        }
                        periodNode.children.push(bookNode)
                    })

                    // Sort books by publication date (oldest first), then alphabetically
                    periodNode.children.sort((a, b) => {
                        const bookA = a.bookId ? categoryTreeStore.allBooks.find(book => book.id === a.bookId) : null
                        const bookB = b.bookId ? categoryTreeStore.allBooks.find(book => book.id === b.bookId) : null

                        const dateA = bookA?.pubDate
                        const dateB = bookB?.pubDate

                        // Parse as numbers, ignore non-numeric dates
                        const numA = dateA ? parseInt(dateA) : NaN
                        const numB = dateB ? parseInt(dateB) : NaN

                        // Both have valid numeric dates - sort by date
                        if (!isNaN(numA) && !isNaN(numB)) {
                            return numA - numB
                        }

                        // Only one has a valid numeric date - prioritize the one with a date
                        if (!isNaN(numA) && isNaN(numB)) return -1
                        if (isNaN(numA) && !isNaN(numB)) return 1

                        // Neither has a valid numeric date - sort alphabetically
                        return a.hebrewName.localeCompare(b.hebrewName, 'he')
                    })

                    tree.push(periodNode)
                })
            } else {
                // For other connection types, direct children are books
                groups.forEach(group => {
                    const bookNode: CommentaryTreeNode = {
                        type: 'book',
                        name: group.groupName,
                        hebrewName: group.groupName,
                        connectionType,
                        bookId: group.targetBookId,
                        lineIndex: group.targetLineIndex,
                        children: [],
                        path: [hebrewName, group.groupName],
                        metadata: group
                    }
                    connectionTypeNode.children.push(bookNode)
                })

                // Sort books alphabetically
                connectionTypeNode.children.sort((a, b) =>
                    a.hebrewName.localeCompare(b.hebrewName, 'he')
                )

                tree.push(connectionTypeNode)
            }
        })

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

        // If there's only one connection type, remove it from paths
        const uniqueConnectionTypes = new Set(books.map(b => b.connectionType))
        if (uniqueConnectionTypes.size === 1) {
            books.forEach(book => {
                // Remove the first element (connection type) from the path
                book.path = book.path.slice(1)
            })
        }

        return books
    })

    return {
        commentaryTree,
        flattenedBooks
    }
}
