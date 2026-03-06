<template>
    <div ref="scrollContainer"
         class="commentary-scroll-container">
        <div v-if="isLoadingMetadata"
             class="commentary-loading">
            טוען רשימת מפרשים...
        </div>

        <div v-else-if="commentaryGroups.length === 0"
             class="commentary-empty">
            אין מפרשים לשורה זו
        </div>

        <div v-else
             class="commentary-groups">
            <div v-for="(bookNode, index) in flattenedBooks"
                 :key="`${bookNode.path.join('-')}-${index}`"
                 :ref="el => setGroupRef(el, index)"
                 class="commentary-group">
                <!-- Sticky Header with full path -->
                <CommentaryHeader
                    :path="bookNode.path"
                    :book-id="bookNode.bookId"
                    :line-index="bookNode.lineIndex"
                    @click="handleGroupClick(bookNode)"
                />

                <!-- Content -->
                <div class="commentary-group-content">
                    <div v-if="!getGroupMetadata(bookNode)?.isLoaded"
                         class="commentary-group-loading">
                        טוען תוכן...
                    </div>
                    <div v-else-if="!getGroupMetadata(bookNode)?.links || getGroupMetadata(bookNode)!.links.length === 0"
                         class="commentary-group-empty">
                        אין תוכן זמין
                    </div>
                    <div v-else
                         class="commentary-links">
                        <div v-for="(link, linkIndex) in getGroupMetadata(bookNode)!.links"
                             :key="linkIndex"
                             class="commentary-link selectable"
                             v-html="link.html" />
                    </div>
                </div>
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { ref, watch, onMounted, onUnmounted, nextTick, computed } from 'vue'
import { useInfiniteScroll } from '@vueuse/core'
import { useCommentaryContent } from './useCommentaryContent'
import { useCommentaryTree } from './useCommentaryTree'
import { useTabStore } from '@/data/stores/tabStore'
import { useCategoryTreeStore } from '@/data/stores/categoryTreeStore'
import { hasConnections } from '@/data/types/Book'
import CommentaryHeader from './CommentaryHeader.vue'
import type { CommentaryTreeNode } from './useCommentaryTree'

const props = defineProps<{
    bookId?: number
    selectedLineIndex?: number
    connectionTypeId?: number
}>()

const {
    commentaryGroups,
    isLoadingMetadata,
    loadCommentaryMetadata,
    loadGroupContent
} = useCommentaryContent()

const { flattenedBooks } = useCommentaryTree(computed(() => commentaryGroups.value))

const tabStore = useTabStore()
const categoryTreeStore = useCategoryTreeStore()

const scrollContainer = ref<HTMLElement | null>(null)
const groupRefs = new Map<number, HTMLElement>()
const observedGroups = new Set<number>()

function setGroupRef(el: any, index: number) {
    if (el) {
        groupRefs.set(index, el as HTMLElement)
    }
}

function getGroupMetadata(node: CommentaryTreeNode) {
    return commentaryGroups.value.find(g => g.groupName === node.metadata?.groupName)
}

function handleGroupClick(node: CommentaryTreeNode) {
    if (node.bookId !== undefined && node.lineIndex !== undefined) {
        const targetBook = categoryTreeStore.allBooks.find(book => book.id === node.bookId)
        const targetHasConnections = targetBook ? hasConnections(targetBook) : false

        tabStore.openBookInNewTab(
            node.hebrewName,
            node.bookId,
            targetHasConnections,
            node.lineIndex,
            true
        )
    }
}

// Intersection Observer to detect when groups come into view
let intersectionObserver: IntersectionObserver | null = null

function setupIntersectionObserver() {
    if (intersectionObserver) {
        intersectionObserver.disconnect()
    }

    intersectionObserver = new IntersectionObserver(
        (entries) => {
            entries.forEach((entry) => {
                if (entry.isIntersecting) {
                    // Find which group this element belongs to
                    for (const [index, element] of groupRefs.entries()) {
                        if (element === entry.target) {
                            const bookNode = flattenedBooks.value[index]
                            if (bookNode?.metadata && !observedGroups.has(index) && props.bookId !== undefined && props.selectedLineIndex !== undefined) {
                                observedGroups.add(index)
                                // Find group index by matching groupName
                                const groupIndex = commentaryGroups.value.findIndex(
                                    g => g.groupName === bookNode.metadata!.groupName
                                )
                                console.log('Loading group:', bookNode.metadata.groupName, 'at index:', groupIndex)
                                if (groupIndex >= 0) {
                                    loadGroupContent(groupIndex, props.bookId, props.selectedLineIndex, props.connectionTypeId)
                                        .then(() => {
                                            console.log('Loaded group:', bookNode.metadata!.groupName, 'isLoaded:', commentaryGroups.value[groupIndex].isLoaded)
                                        })
                                }
                            }
                            break
                        }
                    }
                }
            })
        },
        {
            root: scrollContainer.value,
            rootMargin: '200px',
            threshold: 0
        }
    )

    // Observe all group elements
    groupRefs.forEach((element) => {
        intersectionObserver!.observe(element)
    })
}

// Watch for changes to bookId/lineIndex and reload metadata
watch(
    () => [props.bookId, props.selectedLineIndex, props.connectionTypeId] as const,
    async ([bookId, lineIndex, connectionTypeId]) => {
        if (bookId !== undefined && lineIndex !== undefined) {
            // Clear previous state
            groupRefs.clear()
            observedGroups.clear()

            // Load new metadata
            await loadCommentaryMetadata(bookId, lineIndex, connectionTypeId)

            // Setup observer after DOM updates
            await nextTick()
            setupIntersectionObserver()
        }
    },
    { immediate: true }
)

// Infinite scroll for loading more groups (if needed in future)
useInfiniteScroll(
    scrollContainer,
    () => {
        // Future: Load more groups if pagination is needed
    },
    { distance: 100 }
)

onMounted(() => {
    setupIntersectionObserver()
})

onUnmounted(() => {
    if (intersectionObserver) {
        intersectionObserver.disconnect()
    }
})
</script>

<style scoped>
.commentary-scroll-container {
    height: 100%;
    overflow-y: auto;
    overflow-x: hidden;
    padding: 0 12px 12px 12px;
    background-color: var(--reading-bg-primary);
    color: var(--reading-text-primary);
}

.commentary-loading,
.commentary-empty {
    display: flex;
    align-items: center;
    justify-content: center;
    height: 100%;
    color: var(--text-secondary);
}

.commentary-group {
    margin-bottom: 12px;
}

.commentary-group-content {
    padding-top: 4px;
}

.commentary-group-loading,
.commentary-group-empty {
    color: var(--text-secondary);
    font-style: italic;
    padding: 4px 0;
}

.commentary-links {
    display: flex;
    flex-direction: column;
    gap: 8px;
}

.commentary-link {
    line-height: var(--commentary-line-height);
    direction: rtl;
    text-align: justify;
    font-family: var(--commentary-text-font);
    font-size: var(--commentary-font-size);
}
</style>
