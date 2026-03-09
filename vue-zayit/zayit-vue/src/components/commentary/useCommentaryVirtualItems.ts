/**
 * Commentary Virtual Items Composable
 * Handles commentary items array creation with transformations
 * Follows same pattern as LineView for consistency
 */

import { computed, type Ref, type ComputedRef } from 'vue'
import type { CommentaryMetadata } from './useCommentaryContent'
import type { CommentaryTreeNode } from './useCommentaryTree'
import { transformText } from '@/components/shared/useTextTransformations'

export interface CommentaryVirtualLink {
    text: string
    html: string
    transformedHtml: string
}

export interface CommentaryVirtualGroup {
    bookNode: CommentaryTreeNode
    metadata: CommentaryMetadata | undefined
    transformedLinks: CommentaryVirtualLink[]
}

export function useCommentaryVirtualItems(
    flattenedBooks: ComputedRef<CommentaryTreeNode[]>,
    commentaryGroupsMap: ComputedRef<Map<string, CommentaryMetadata>>,
    diacriticsState: ComputedRef<number | undefined>,
    searchQuery: ComputedRef<string>,
    currentMatchBookId: ComputedRef<number | null>,
    currentMatchLinkIndex: ComputedRef<number | null>,
    currentMatchIndexInLink: ComputedRef<number>
) {
    const virtualGroups = computed<CommentaryVirtualGroup[]>(() => {
        // Access all reactive dependencies at the top level for proper tracking
        const matchBookId = currentMatchBookId.value
        const matchLinkIndex = currentMatchLinkIndex.value
        const matchIndexInLink = currentMatchIndexInLink.value
        const query = searchQuery.value
        const diacritics = diacriticsState.value

        const groups: CommentaryVirtualGroup[] = []

        flattenedBooks.value.forEach((bookNode, groupIndex) => {
            const metadata = commentaryGroupsMap.value.get(bookNode.metadata?.groupName || '')

            if (!metadata) {
                groups.push({
                    bookNode,
                    metadata: undefined,
                    transformedLinks: []
                })
                return
            }

            // Transform all links for this group
            const transformedLinks: CommentaryVirtualLink[] = metadata.links.map((link, linkIndex) => {
                const isCurrentSearchMatch =
                    matchBookId === bookNode.bookId &&
                    matchLinkIndex === linkIndex

                const transformedHtml = transformText(link.html, {
                    diacriticsState: diacritics,
                    searchQuery: query,
                    isCurrentSearchMatch,
                    currentMatchIndex: isCurrentSearchMatch ? matchIndexInLink : undefined
                })

                return {
                    text: link.text,
                    html: link.html,
                    transformedHtml
                }
            })

            groups.push({
                bookNode,
                metadata,
                transformedLinks
            })
        })

        return groups
    })

    return {
        virtualGroups
    }
}
