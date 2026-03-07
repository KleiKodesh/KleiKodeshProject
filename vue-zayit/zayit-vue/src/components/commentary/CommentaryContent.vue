<template>
    <div ref="scrollContainer"
         class="commentary-scroll-container"
         :style="containerStyles">
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
                 class="commentary-group"
                 :style="{ containIntrinsicSize: intrinsicSize }">
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
import { useSettingsStore } from '@/data/stores/settingsStore'
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
const settingsStore = useSettingsStore()

const scrollContainer = ref<HTMLElement | null>(null)

// Get current zoom from active tab
const currentZoom = computed(() => {
    return tabStore.activeTab?.bookState?.zoom || 100
})

// Computed styles with zoom applied
const containerStyles = computed(() => {
    return {
        fontSize: `calc(var(--commentary-font-size, 100%) * ${currentZoom.value / 100})`
    }
})

// Save scroll position to tab store (two-tier: element index + offset)
function saveScrollPosition() {
    if (!scrollContainer.value) return
    const activeTab = tabStore.activeTab
    if (!activeTab?.bookState) return

    // Find which element is at the top of the viewport
    const containerRect = scrollContainer.value.getBoundingClientRect()
    const topY = containerRect.top + 50 // Account for sticky header

    const groups = scrollContainer.value.querySelectorAll('[data-book-id]')
    for (let i = 0; i < groups.length; i++) {
        const group = groups[i] as HTMLElement
        const rect = group.getBoundingClientRect()
        if (rect.top <= topY && rect.bottom > topY) {
            // Save offset FROM the element's start (how far into the element we are)
            const offset = topY - rect.top

            activeTab.bookState.commentaryScrollElementIndex = i
            activeTab.bookState.commentaryScrollOffset = offset

            console.log('[CommentaryContent] 💾 SAVED scroll position:', { index: i, offset, topY, rectTop: rect.top })
            return
        }
    }
}

// Restore scroll position from tab store with retry logic (two-tier: bookId + offset)
async function restoreScrollPosition(isFirstInit: boolean) {
    if (!scrollContainer.value) return

    // Don't restore on first initialization - let default commentary scroll happen
    if (isFirstInit) {
        console.log('[CommentaryContent] ⏭️ SKIP restore - first init')
        return
    }

    const activeTab = tabStore.activeTab
    const elementIndex = activeTab?.bookState?.commentaryScrollElementIndex
    const offset = activeTab?.bookState?.commentaryScrollOffset

    // Only restore if we have element index
    if (elementIndex === undefined) {
        console.log('[CommentaryContent] ⏭️ SKIP restore - no persisted position')
        return
    }

    console.log('[CommentaryContent] 🔄 RESTORING scroll position:', { elementIndex, offset })

    // Retry logic to wait for element to be available
    let targetElement: HTMLElement | null = null
    const maxRetries = 10

    for (let attempt = 0; attempt < maxRetries; attempt++) {
        await new Promise(resolve => requestAnimationFrame(resolve))

        const groups = scrollContainer.value.querySelectorAll('[data-book-id]')
        if (elementIndex < groups.length) {
            targetElement = groups[elementIndex] as HTMLElement
            console.log('[CommentaryContent] Found element by index:', elementIndex, 'attempt:', attempt + 1)
            break
        }

        // Wait before retry
        if (attempt < maxRetries - 1) {
            await new Promise(resolve => setTimeout(resolve, 50))
        }
    }

    if (!targetElement) {
        console.log('[CommentaryContent] ⚠️ Element not found after retries')
        return
    }

    // Temporarily force render this element
    const originalVisibility = targetElement.style.contentVisibility
    targetElement.style.contentVisibility = 'visible'

    await new Promise(resolve => requestAnimationFrame(resolve))
    await new Promise(resolve => requestAnimationFrame(resolve))

    // Scroll to element using the scroll composable
    await scrollToElement(targetElement, { block: 'nearest' })

    // If we have an offset, apply it with retry logic
    if (offset !== undefined && offset !== 0) {
        const maxOffsetRetries = 5
        for (let i = 0; i < maxOffsetRetries; i++) {
            await new Promise(resolve => requestAnimationFrame(resolve))

            const containerRect = scrollContainer.value.getBoundingClientRect()
            const elementRect = targetElement.getBoundingClientRect()
            const topY = containerRect.top + 50 // Account for sticky header

            // Calculate how far into the element we currently are
            const currentOffset = topY - elementRect.top
            const adjustment = offset - currentOffset

            console.log('[CommentaryContent] 📍 Offset adjustment attempt', i + 1, ':', {
                targetOffset: offset,
                currentOffset,
                adjustment,
                topY,
                elementTop: elementRect.top
            })

            if (Math.abs(adjustment) < 2) {
                console.log('[CommentaryContent] ✅ Offset applied successfully')
                break
            }

            scrollContainer.value.scrollTop += adjustment

            if (i < maxOffsetRetries - 1) {
                await new Promise(resolve => setTimeout(resolve, 20))
            }
        }
    }

    // Verify final position
    await new Promise(resolve => requestAnimationFrame(resolve))
    const finalRect = targetElement.getBoundingClientRect()
    const finalContainerRect = scrollContainer.value.getBoundingClientRect()
    const finalTopY = finalContainerRect.top + 50
    const finalOffset = finalTopY - finalRect.top

    console.log('[CommentaryContent] ✅ RESTORE complete:', {
        elementIndex,
        targetOffset: offset,
        actualOffset: finalOffset,
        diff: Math.abs((offset || 0) - finalOffset)
    })

    // Restore content-visibility
    targetElement.style.contentVisibility = originalVisibility
}

// Create a Map for O(1) lookups
const commentaryGroupsMap = computed(() => {
    const map = new Map<string, typeof props.commentaryGroups[0]>()
    props.commentaryGroups.forEach(g => map.set(g.groupName, g))
    return map
})

// Calculate intrinsic size based on commentary settings and zoom
const intrinsicSize = computed(() => {
    const fontSize = settingsStore.commentaryFontSize / 100 // Convert percentage to multiplier
    const lineHeight = settingsStore.commentaryLinePadding
    const zoom = currentZoom.value / 100 // Apply zoom

    // Base font size in pixels (browser default is typically 16px)
    const baseFontSize = 16

    // Header height: ~1.1rem font size + padding
    const headerHeight = (baseFontSize * 1.1 * fontSize * zoom) + 8

    // Estimate average 10 lines of content per group
    const avgLines = 10
    const lineHeightPx = baseFontSize * fontSize * lineHeight * zoom
    const contentHeight = avgLines * lineHeightPx + 16 // +16 for gaps

    const totalHeight = headerHeight + contentHeight

    return `auto ${Math.round(totalHeight)}px`
})

onMounted(() => {
    if (scrollContainer.value) {
        scrollContainer.value.addEventListener('scroll', handleScroll, { passive: true })
        detectVisibleGroup()
    }
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
    console.log('[CommentaryContent] scrollToGroup called with bookId:', bookId)

    if (!scrollContainer.value) {
        console.log('[CommentaryContent] No scroll container!')
        return
    }

    const groupElement = scrollContainer.value.querySelector(`[data-book-id="${bookId}"]`) as HTMLElement
    if (!groupElement) {
        console.log('[CommentaryContent] No group element found for bookId:', bookId)
        return
    }

    console.log('[CommentaryContent] Found group element, disabling content-visibility')

    // Temporarily disable content-visibility to force rendering
    const originalContentVisibility = groupElement.style.contentVisibility
    groupElement.style.contentVisibility = 'visible'

    // Wait for browser to render
    await new Promise(resolve => requestAnimationFrame(resolve))

    console.log('[CommentaryContent] Scrolling to top')
    // Scroll to top
    await scrollToElementTop(groupElement)

    // Verify scroll position
    await new Promise(resolve => requestAnimationFrame(resolve))
    const containerRect = scrollContainer.value.getBoundingClientRect()
    const elementRect = groupElement.getBoundingClientRect()
    const isAtTop = Math.abs(elementRect.top - containerRect.top) < 5

    console.log('[CommentaryContent] Scroll verification:', { isAtTop, elementTop: elementRect.top, containerTop: containerRect.top })

    // If not at top, try one more time
    if (!isAtTop) {
        console.log('[CommentaryContent] Not at top, retrying')
        await scrollToElementTop(groupElement)
    }

    // Restore content-visibility
    groupElement.style.contentVisibility = originalContentVisibility
    console.log('[CommentaryContent] scrollToGroup complete')
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
    saveScrollPosition()
}

defineExpose({
    scrollToGroup,
    restoreScrollPosition
})

onUnmounted(() => {
    if (scrollContainer.value) {
        scrollContainer.value.removeEventListener('scroll', handleScroll)
    }
    saveScrollPosition()
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
