<template>
    <div ref="scrollContainer"
         class="commentary-scroll-container"
         tabindex="0"
         :style="containerStyles">
        <div v-if="isLoadingMetadata" class="commentary-loading flex-center">
            טוען רשימת מפרשים...
        </div>

        <div v-else-if="commentaryGroups.length === 0" class="commentary-empty flex-center">
            אין מפרשים לשורה זו
        </div>

        <div v-else
             class="commentary-groups">
            <div v-for="(bookNode, index) in flattenedBooks"
                 :key="`${bookNode.path.join('-')}-${index}`"
                 :data-book-id="bookNode.bookId"
                 class="commentary-group"
                 :style="{ containIntrinsicSize: intrinsicSize }">
                <!-- Sticky Toolbar with navigation -->
                <CommentaryHeader :path="bookNode.path"
                                  :book-id="bookNode.bookId"
                                  :line-index="bookNode.lineIndex"
                                  :has-previous="index > 0"
                                  :has-next="index < flattenedBooks.length - 1"
                                  :available-books="flattenedBooks"
                                  :is-dragging-selection="isDraggingSelection"
                                  @click="handleGroupClick(bookNode)"
                                  @navigate-previous="navigateToPrevious(index)"
                                  @navigate-next="navigateToNext(index)"
                                  @navigate-previous-line="emit('navigate-previous-line', bookNode.bookId)"
                                  @navigate-next-line="emit('navigate-next-line', bookNode.bookId)"
                                  @select-commentary="(bookId) => emit('select-commentary', bookId)"
                                  @focus-content="focusContent" />

                <div class="commentary-group-content">
                    <div v-if="!getGroupMetadata(bookNode)?.isLoaded" class="commentary-group-loading">
                        טוען תוכן...
                    </div>
                    <div v-else-if="!getGroupMetadata(bookNode)?.links || getGroupMetadata(bookNode)!.links.length === 0" class="commentary-group-empty">
                        אין תוכן זמין
                    </div>
                    <div v-else class="commentary-links">
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
import { useCommentaryTree } from './useCommentaryTree'
import { useCommentaryScroll } from './useCommentaryScroll'
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
    (e: 'navigate-previous-line', bookId?: number): void
    (e: 'navigate-next-line', bookId?: number): void
    (e: 'select-commentary', bookId: number): void
}>()

const scrollContainer = ref<HTMLElement | null>(null)
const isDraggingSelection = ref(false)
const { flattenedBooks } = useCommentaryTree(computed(() => props.commentaryGroups))

const {
    containerStyles,
    intrinsicSize,
    saveScrollPosition,
    restoreScrollPosition,
    scrollToGroup,
    detectVisibleGroup,
    handleGroupClick
} = useCommentaryScroll(scrollContainer)

const commentaryGroupsMap = computed(() => {
    const map = new Map<string, typeof props.commentaryGroups[0]>()
    props.commentaryGroups.forEach(g => map.set(g.groupName, g))
    return map
})

function getGroupMetadata(node: CommentaryTreeNode) {
    return commentaryGroupsMap.value.get(node.metadata?.groupName || '')
}

function handleScroll() {
    detectVisibleGroup(emit)
    saveScrollPosition()
}

async function navigateToPrevious(currentIndex: number) {
    if (currentIndex > 0) {
        const previousBook = flattenedBooks.value[currentIndex - 1]
        if (previousBook?.bookId) {
            await scrollToGroup(previousBook.bookId)
            focusContent()
        }
    }
}

async function navigateToNext(currentIndex: number) {
    if (currentIndex < flattenedBooks.value.length - 1) {
        const nextBook = flattenedBooks.value[currentIndex + 1]
        if (nextBook?.bookId) {
            await scrollToGroup(nextBook.bookId)
            focusContent()
        }
    }
}

function focusContent() {
    if (scrollContainer.value) {
        scrollContainer.value.focus()
    }
}

function handleSelectStart() {
    isDraggingSelection.value = true
}

function handleMouseUp() {
    isDraggingSelection.value = false
}

onMounted(() => {
    if (scrollContainer.value) {
        scrollContainer.value.addEventListener('scroll', handleScroll, { passive: true })
        scrollContainer.value.addEventListener('selectstart', handleSelectStart)
        detectVisibleGroup(emit)
    }
    document.addEventListener('mouseup', handleMouseUp)
})

onUnmounted(() => {
    if (scrollContainer.value) {
        scrollContainer.value.removeEventListener('scroll', handleScroll)
        scrollContainer.value.removeEventListener('selectstart', handleSelectStart)
    }
    document.removeEventListener('mouseup', handleMouseUp)
    saveScrollPosition()
})

defineExpose({
    scrollToGroup,
    restoreScrollPosition,
    focusContent
})
</script>

<style scoped>
.commentary-scroll-container {
    height: 100%;
    overflow-y: auto;
    overflow-x: hidden;
    padding: 0 12px 12px;
    background-color: var(--reading-bg-primary);
    color: var(--reading-text-primary);
    outline: none;
}

.commentary-loading,
.commentary-empty {
    height: 100%;
    color: var(--text-secondary);
}

.commentary-group {
    margin-bottom: 12px;
    content-visibility: auto;
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
