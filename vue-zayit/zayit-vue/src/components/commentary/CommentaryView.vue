<template>
    <div class="flex-row height-fill commentary-container">
        <!-- Side panel tree view -->
        <CommentaryTreePanel :commentary-groups="commentaryGroups"
                             :selected-book-id="selectedBookId"
                             class="commentary-tree-panel"
                             @select="handleSelectGroup" />

        <!-- Main commentary content -->
        <CommentaryContent ref="commentaryContentRef"
                           :commentary-groups="commentaryGroups"
                           :is-loading-metadata="isLoadingMetadata"
                           :book-id="props.bookId"
                           :selected-line-index="props.selectedLineIndex"
                           :connection-type-id="selectedConnectionTypeId"
                           class="commentary-content"
                           @visible-book-changed="handleVisibleBookChanged" />
    </div>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import CommentaryContent from './CommentaryContent.vue'
import CommentaryTreePanel from './CommentaryTreePanel.vue'
import { useTabStore } from '@/data/stores/tabStore'
import { useConnectionTypesStore } from '@/data/stores/connectionTypesStore'
import { useCommentaryContent } from './useCommentaryContent'
import { nextTick } from 'vue'
import type { Book } from '@/data/types/Book'
import type { TocEntry } from '@/data/types/BookToc'
import type { CommentaryTreeNode } from './useCommentaryTree'

const props = withDefaults(defineProps<{
    bookId?: number
    selectedLineIndex?: number
    book?: Book
    flatTocEntries?: TocEntry[]
}>(), {
    bookId: undefined,
    selectedLineIndex: undefined,
    book: undefined,
    flatTocEntries: () => []
})

const emit = defineEmits<{
    (e: 'clearOtherSelections'): void
    (e: 'navigate-line', newIndex: number, tocEntryId?: number): void
}>()

const tabStore = useTabStore()
const connectionTypesStore = useConnectionTypesStore()
const selectedBookId = ref<number>()
const commentaryContentRef = ref<any>()
const hasInitialized = ref(false)

// Load commentary content at view level
const { commentaryGroups, isLoadingMetadata, loadCommentaryMetadata } = useCommentaryContent()

// Get selected connection type from tab state
const selectedConnectionTypeId = computed(() => {
    const activeTab = tabStore.activeTab
    return activeTab?.bookState?.commentaryFilterConnectionTypeId
})

function handleSelectGroup(node: CommentaryTreeNode) {
    if (node.type === 'book' && node.bookId !== undefined) {
        selectedBookId.value = node.bookId
        commentaryContentRef.value?.scrollToGroup(node.bookId)
    }
}

function handleVisibleBookChanged(bookId: number) {
    selectedBookId.value = bookId
}

// Load metadata when props change
watch(
    () => [props.bookId, props.selectedLineIndex, selectedConnectionTypeId.value] as const,
    async ([bookId, lineIndex, connectionTypeId]) => {
        console.log('[CommentaryView] Watch triggered:', { bookId, lineIndex, connectionTypeId, hasInitialized: hasInitialized.value })

        if (bookId !== undefined && lineIndex !== undefined) {
            await loadCommentaryMetadata(bookId, lineIndex, connectionTypeId)

            console.log('[CommentaryView] Metadata loaded, groups count:', commentaryGroups.value.length)

            // Only apply default commentary on initial load
            if (!hasInitialized.value && commentaryGroups.value.length > 0) {
                console.log('[CommentaryView] First initialization')
                hasInitialized.value = true

                const activeTab = tabStore.activeTab
                const hasPersistedScroll = activeTab?.bookState?.commentaryScrollElementIndex !== undefined

                console.log('[CommentaryView] Persisted scroll check:', {
                    commentaryScrollElementIndex: activeTab?.bookState?.commentaryScrollElementIndex,
                    commentaryScrollOffset: activeTab?.bookState?.commentaryScrollOffset,
                    hasPersistedScroll
                })

                // Only scroll to default if no meaningful persisted position
                if (!hasPersistedScroll) {
                    const defaultBookId = props.book?.defaultCommentatorBookId
                    const firstBookId = commentaryGroups.value[0]?.targetBookId
                    const targetBookId = defaultBookId || firstBookId

                    console.log('[CommentaryView] Scrolling to default:', { defaultBookId, firstBookId, targetBookId })

                    if (targetBookId) {
                        selectedBookId.value = targetBookId

                        // Retry logic to ensure ref is ready
                        const scrollToDefault = async () => {
                            for (let i = 0; i < 10; i++) {
                                await nextTick()

                                if (commentaryContentRef.value?.scrollToGroup) {
                                    console.log('[CommentaryView] Ref ready, calling scrollToGroup')
                                    await commentaryContentRef.value.scrollToGroup(targetBookId)
                                    return
                                }

                                console.log('[CommentaryView] Ref not ready, retry', i + 1)
                                await new Promise(resolve => setTimeout(resolve, 50))
                            }
                            console.log('[CommentaryView] Failed to get ref after retries')
                        }

                        scrollToDefault()
                    }
                } else {
                    console.log('[CommentaryView] Has persisted scroll, restoring')
                    // Restore persisted scroll position
                    await nextTick()
                    commentaryContentRef.value?.restoreScrollPosition(false)
                }
            } else if (commentaryGroups.value.length > 0) {
                // Subsequent loads - restore scroll position
                console.log('[CommentaryView] Subsequent load, restoring scroll')
                await nextTick()
                commentaryContentRef.value?.restoreScrollPosition(false)
            } else {
                console.log('[CommentaryView] Skipping initialization:', {
                    hasInitialized: hasInitialized.value,
                    groupsLength: commentaryGroups.value.length
                })
            }
        }
    },
    { immediate: true }
)
</script>

<style scoped>
.commentary-container {
    position: relative;
    display: flex;
    flex-direction: row;
    height: 100%;
}

.commentary-content {
    flex: 1 1 0%;
    min-height: 0;
}

.commentary-tree-panel {
    flex: 0 1 auto;
    max-width: 30%;
    min-width: 0;
    overflow: hidden;
    border-right: 1px solid var(--border-color, #ddd);
}
</style>
