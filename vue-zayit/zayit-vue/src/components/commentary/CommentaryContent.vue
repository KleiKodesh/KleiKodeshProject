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
            <div v-for="(group, index) in commentaryGroups"
                 :key="`${group.groupName}-${index}`"
                 :ref="el => setGroupRef(el, index)"
                 class="commentary-group">
                <!-- Sticky Header -->
                <div class="commentary-group-header">
                    <a
                        v-if="group.targetBookId !== undefined && group.targetLineIndex !== undefined"
                        href="#"
                        class="commentary-group-title"
                        @click.prevent="handleGroupClick(group)"
                    >
                        {{ group.groupName }}
                    </a>
                    <h3 v-else class="commentary-group-title">
                        {{ group.groupName }}
                    </h3>
                </div>

                <!-- Content -->
                <div class="commentary-group-content">
                    <div v-if="!group.isLoaded"
                         class="commentary-group-loading">
                        טוען תוכן...
                    </div>
                    <div v-else-if="group.links.length === 0"
                         class="commentary-group-empty">
                        אין תוכן זמין
                    </div>
                    <div v-else
                         class="commentary-links">
                        <div v-for="(link, linkIndex) in group.links"
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
import { ref, watch, onMounted, onUnmounted, nextTick } from 'vue'
import { useInfiniteScroll } from '@vueuse/core'
import { useCommentaryContent } from './useCommentaryContent'
import { useTabStore } from '@/data/stores/tabStore'
import { useCategoryTreeStore } from '@/data/stores/categoryTreeStore'
import { hasConnections } from '@/data/types/Book'
import type { CommentaryMetadata } from './useCommentaryContent'

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

function handleGroupClick(group: CommentaryMetadata) {
    if (group.targetBookId !== undefined && group.targetLineIndex !== undefined) {
        const targetBook = categoryTreeStore.allBooks.find(book => book.id === group.targetBookId)
        const targetHasConnections = targetBook ? hasConnections(targetBook) : false

        tabStore.openBookInNewTab(
            group.groupName,
            group.targetBookId,
            targetHasConnections,
            group.targetLineIndex,
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
                            if (!observedGroups.has(index) && props.bookId !== undefined && props.selectedLineIndex !== undefined) {
                                observedGroups.add(index)
                                loadGroupContent(index, props.bookId, props.selectedLineIndex, props.connectionTypeId)
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
    padding: 0 16px 16px 16px;
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
    margin-bottom: 24px;
}

.commentary-group-header {
    position: sticky;
    top: 0;
    background-color: var(--reading-bg-primary);
    padding: 8px 0;
    z-index: 10;
}

.commentary-group-title {
    margin: 0;
    font-size: calc(1.1rem * var(--commentary-font-size) / 100);
    font-weight: 600;
    color: var(--reading-text-primary);
    direction: rtl;
    font-family: var(--commentary-header-font);
}

a.commentary-group-title {
    color: var(--accent-color);
    text-decoration: none;
    cursor: pointer;
}

a.commentary-group-title:hover {
    text-decoration: underline;
}

.commentary-group-content {
    padding-top: 8px;
}

.commentary-group-loading,
.commentary-group-empty {
    color: var(--text-secondary);
    font-style: italic;
    padding: 8px 0;
}

.commentary-links {
    display: flex;
    flex-direction: column;
    gap: 12px;
}

.commentary-link {
    line-height: var(--commentary-line-height);
    direction: rtl;
    text-align: justify;
    font-family: var(--commentary-text-font);
    font-size: var(--commentary-font-size);
}
</style>
