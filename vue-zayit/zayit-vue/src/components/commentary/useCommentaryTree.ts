import { computed, type ComputedRef } from 'vue'
import { useCategoryTreeStore } from '@/data/stores/categoryTreeStore'
import { useConnectionTypesStore } from '@/data/stores/connectionTypesStore'
import type { CommentaryMetadata } from './useCommentaryContent'

export interface CommentaryTreeNode {
    type: 'connection-type' | 'book'
    name: string
    hebrewName: string
    connectionType?: string
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

            // Direct children are books (no period level for any connection type)
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
