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
    diacriticsState: ComputedRef<number | undefined>
) {
    const virtualGroups = computed<CommentaryVirtualGroup[]>(() => {
        const groups: CommentaryVirtualGroup[] = []

        flattenedBooks.value.forEach(bookNode => {
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
            const transformedLinks: CommentaryVirtualLink[] = metadata.links.map(link => {
                const transformedHtml = transformText(link.html, {
                    diacriticsState: diacriticsState.value
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
