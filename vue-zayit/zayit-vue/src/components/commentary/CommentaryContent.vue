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
                 :data-book-id="bookNode.bookId"
                 class="commentary-group">
                <!-- Sticky Header with full path -->
                <CommentaryHeader :path="bookNode.path"
                                  :book-id="bookNode.bookId"
                                  :line-index="bookNode.lineIndex"
                                  @click="handleGroupClick(bookNode)" />

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
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { scrollToElement, scrollToElementTop } from '@/components/shared/useScrollToElement'
import { useCommentaryTree } from './useCommentaryTree'
import { useTabStore } from '@/data/stores/tabStore'
import { useCategoryTreeStore } from '@/data/stores/categoryTreeStore'
import { hasConnections } from '@/data/types/Book'
import CommentaryHeader from './CommentaryHeader.vue'
import type { CommentaryTreeNode } from './useCommentaryTree'
import type { CommentaryMetadata } from './useCommentaryContent'

const props = defineProps<{
    commentaryGroups: CommentaryMetadata[]
    isLoadingMetadata: boolean
    bookId?: number
    selectedLineIndex?: number
    connectionTypeId?: number
}>()

const emit = defineEmits<{
    (e: 'visible-book-changed', bookId: number): void
}>()

const { flattenedBooks } = useCommentaryTree(computed(() => props.commentaryGroups))

const tabStore = useTabStore()
const categoryTreeStore = useCategoryTreeStore()

const scrollContainer = ref<HTMLElement | null>(null)

// Create a Map for O(1) lookups
const commentaryGroupsMap = computed(() => {
    const map = new Map<string, typeof props.commentaryGroups[0]>()
    props.commentaryGroups.forEach(g => map.set(g.groupName, g))
    return map
})

defineExpose({
    scrollToGroup
})

function getGroupMetadata(node: CommentaryTreeNode) {
    return commentaryGroupsMap.value.get(node.metadata?.groupName || '')
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

async function scrollToGroup(bookId: number) {
    if (!scrollContainer.value) return

    const groupElement = scrollContainer.value.querySelector(`[data-book-id="${bookId}"]`) as HTMLElement
    if (!groupElement) return

    // Temporarily disable content-visibility to force rendering
    const originalContentVisibility = groupElement.style.contentVisibility
    groupElement.style.contentVisibility = 'visible'

    // Wait for browser to render
    await new Promise(resolve => requestAnimationFrame(resolve))

    // Scroll to top
    await scrollToElementTop(groupElement)

    // Verify scroll position
    await new Promise(resolve => requestAnimationFrame(resolve))
    const containerRect = scrollContainer.value.getBoundingClientRect()
    const elementRect = groupElement.getBoundingClientRect()
    const isAtTop = Math.abs(elementRect.top - containerRect.top) < 5

    // If not at top, try one more time
    if (!isAtTop) {
        await scrollToElementTop(groupElement)
    }

    // Restore content-visibility
    groupElement.style.contentVisibility = originalContentVisibility
}

// Track which commentary group is at the top
function detectVisibleGroup() {
    if (!scrollContainer.value) return

    const containerRect = scrollContainer.value.getBoundingClientRect()
    const topY = containerRect.top + 50

    const groups = scrollContainer.value.querySelectorAll('[data-book-id]')

    for (const group of groups) {
        const rect = group.getBoundingClientRect()
        if (rect.top <= topY && rect.bottom > topY) {
            const bookId = parseInt(group.getAttribute('data-book-id') || '0')
            if (bookId) {
                emit('visible-book-changed', bookId)
            }
            break
        }
    }
}

function handleScroll() {
    detectVisibleGroup()
}

onMounted(() => {
    if (scrollContainer.value) {
        scrollContainer.value.addEventListener('scroll', handleScroll, { passive: true })
        detectVisibleGroup()
    }
})

onUnmounted(() => {
    if (scrollContainer.value) {
        scrollContainer.value.removeEventListener('scroll', handleScroll)
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
    content-visibility: auto;
    contain-intrinsic-size: auto 500px;
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
