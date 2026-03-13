<template>
    <div v-if="viewerState.totalLines.value > 0"
         class="height-fill"
         style="position: relative;">
        <!-- Progress bar -->
        <ProgressBar :progress="viewerState.loadingProgress.value" />

        <!-- Context Menu -->
        <ContextMenu ref="contextMenuRef"
                     :items="contextMenuItems" />

        <!-- Virtua virtualized scroll container -->
        <VList ref="virtuaRef"
               :data="virtualItems"
               :style="containerStyles"
               class="line-scroll-container"
               tabindex="0"
               @keydown="handleKeyDown"
               @contextmenu="handleContextMenu"
               @scroll="handleVirtuaScroll"
               #default="{ item, index }">
            <Line :content="item.content || '\u00A0'"
                  :line-index="index"
                  :is-selected="selectedLineIndex === index"
                  :alt-toc-entries="item.altTocEntries"
                  :show-alt-toc="myTab?.bookState?.showAltToc"
                  :class="{
                    'show-selection': myTab?.bookState?.showBottomPane
                }"
                  @line-click="handleLineClick" />
        </VList>
    </div>
</template>

<script setup lang="ts">

// IMPORTS
import { ref, watch, nextTick, onMounted, onUnmounted, computed, type Ref } from 'vue'
import { useFocus } from '@vueuse/core'
import { VList } from 'virtua/vue'
import Line from './Line.vue'
import ContextMenu from '@/components/shared/ContextMenu.vue'
import ProgressBar from '@/components/shared/ProgressBar.vue'
import { BookLineViewerService } from '@/data/services/bookLineViewerService'
import { useTabStore } from '@/data/stores/tabStore'

// Extracted utilities and composables
import { useLineViewContextMenu } from './useLineViewContextMenu'
import { useLineViewSelection } from './useLineViewSelection'
import { useLineViewCopy } from './useLineViewCopy'
import { useLineViewVirtualItems } from './useLineViewVirtualItems'
import { useLineViewScrollPositionVirtua as useLineViewScrollPosition } from './useLineViewScrollPositionVirtua'
import { useLineViewEvents } from './useLineViewEvents'
import { useLineViewSearch } from './useLineViewSearch'

// STORES
const tabStore = useTabStore()

// REFS & STATE
const myTab = computed(() => tabStore.tabs.find(t => t.id === props.tabId))
const viewerState = new BookLineViewerService()
const virtuaRef: Ref<any> = ref(null)
const contextMenuRef = ref<InstanceType<typeof ContextMenu> | null>(null)

// Get the actual DOM element from Virtua for selection/copy handlers
const virtuaDomElement = computed(() => {
    return virtuaRef.value?.$el as HTMLElement | undefined
})

// SCROLL COMPOSABLE - Handles scrolling and position persistence
const {
    containerStyles,
    saveScrollPosition,
    restoreScrollPosition,
    scrollToLine,
    detectVisibleLine,
    findItemIndex
} = useLineViewScrollPosition(virtuaRef, computed(() => props.tabId))

// PROPS & EMITS
const props = defineProps<{
    tabId?: number
    altTocByLineIndex?: Map<number, import('@/data/services/bookTocService').AltTocLineEntry[]>
    flatTocEntries?: import('@/data/types/BookToc').TocEntry[]
}>()

const emit = defineEmits<{
    placeholdersReady: []
    lineClick: [lineIndex: number]
    clearOtherSelections: []
    centerLineChanged: [lineIndex: number]
    currentTocEntryChanged: [tocEntryId: number | undefined]
}>()

// CONTEXT MENU - Use extracted composable
const { contextMenuItems } = useLineViewContextMenu(virtuaDomElement)

function handleContextMenu(event: MouseEvent) {
    contextMenuRef.value?.show(event)
}

// SELECTION MANAGEMENT - Use extracted composable
useLineViewSelection(virtuaDomElement)

// COPY HANDLER - Use extracted composable
useLineViewCopy(virtuaDomElement, viewerState, myTab)

// Global search highlighting state
const globalSearchHighlightLineIndex = ref<number | null>(null)
const globalSearchTerms = ref<string>('')
const globalSearchSnippet = ref<string>('')

// Handle keydown for Space key prevention (keep in template handler)
function handleKeyDown(e: KeyboardEvent) {
    // Prevent spacebar from scrolling unless in search input
    if (e.key === ' ' && e.target instanceof HTMLElement && e.target.tagName !== 'INPUT') {
        e.preventDefault()
        return
    }
}

// EVENT HANDLERS - Use extracted composable
const {
    selectedLineIndex,
    handleLineClick
} = useLineViewEvents(
    myTab,
    computed(() => props.flatTocEntries),
    viewerState,
    emit
)

// SEARCH - Line view search functionality
const {
    searchQuery: lineSearchQuery,
    currentMatch: lineSearchCurrentMatch,
    currentMatchIndex: lineSearchCurrentMatchIndex,
    totalMatches: lineSearchTotalMatches,
    performSearch: performLineSearch,
    nextMatch: nextLineSearchMatch,
    previousMatch: previousLineSearchMatch,
    clearSearch: clearLineSearch
} = useLineViewSearch(viewerState, virtuaRef)

const currentMatchLineIndex = computed(() => lineSearchCurrentMatch.value?.lineIndex ?? null)
const currentMatchIndexInLine = computed(() => lineSearchCurrentMatch.value?.matchIndex ?? 0)

// COMPUTED PROPERTIES - Virtual Items
// Use extracted composable for virtual items
const { virtualItems } = useLineViewVirtualItems(
    viewerState,
    myTab,
    computed(() => props.altTocByLineIndex),
    globalSearchHighlightLineIndex,
    globalSearchTerms,
    globalSearchSnippet,
    lineSearchQuery,
    currentMatchLineIndex,
    currentMatchIndexInLine
)

// SCROLL EVENT HANDLER
let scrollThrottleTimer: number | null = null
function handleVirtuaScroll(offset: number) {
    detectVisibleLine(emit)
    saveScrollPosition()

    // Throttle prioritization to avoid excessive calls
    if (scrollThrottleTimer) return

    scrollThrottleTimer = window.setTimeout(() => {
        scrollThrottleTimer = null

        // Prioritize loading lines around current scroll position
        if (virtuaRef.value) {
            const viewportSize = virtuaRef.value.viewportSize
            const scrollOffset = virtuaRef.value.scrollOffset

            // Find approximate center line using Virtua's API
            const centerOffset = scrollOffset + viewportSize / 2
            const centerLine = findItemIndex(centerOffset)

            // Prioritize lines around the center of viewport
            if (centerLine !== undefined) {
                viewerState.prioritizeLines(centerLine, 200)
            }
        }
    }, 100) // Throttle to once per 100ms
}



// WATCHERS - Book Loading and Tab Switching
// Watch for both bookId changes AND tab changes
watch(() => {
    const bookId = myTab.value?.bookState?.bookId
    const tabId = props.tabId
    return [bookId, tabId] as const
}, async ([bookId, tabId], oldValues) => {
    if (!bookId) return

    const oldBookId = oldValues?.[0]
    const oldTabId = oldValues?.[1]

    const isBookChange = bookId !== oldBookId
    const isTabSwitch = tabId !== oldTabId && bookId === oldBookId

    if (isBookChange) {
        // Book changed - load new book
        const initialLineIndex = myTab.value?.bookState?.initialLineIndex
        const isRestore = oldBookId === undefined && initialLineIndex !== undefined

        await viewerState.loadBook(bookId, isRestore, initialLineIndex)
        await nextTick()

        emit('placeholdersReady')

        // Restore scroll position or scroll to initial line
        if (initialLineIndex !== undefined) {
            await scrollToLine(initialLineIndex)
        } else {
            await restoreScrollPosition()
        }
    } else if (isTabSwitch) {
        // Tab switched - save current position before restoring
        saveScrollPosition()
        
        // Then restore scroll position
        await nextTick()
        await restoreScrollPosition()
    }
}, { immediate: true })

// TOC NAVIGATION
async function handleTocSelection(lineIndex: number) {
    await viewerState.handleTocSelection(lineIndex)
    await nextTick()
    await scrollToLine(lineIndex)
}

// GLOBAL SEARCH HIGHLIGHTING
async function scrollToLineWithFadeHighlight(
    lineIndex: number,
    searchTerms?: string,
    snippet?: string
) {
    if (searchTerms) {
        globalSearchHighlightLineIndex.value = lineIndex
        globalSearchTerms.value = searchTerms
        globalSearchSnippet.value = snippet || ''
    }

    await scrollToLine(lineIndex)

    if (searchTerms) {
        await nextTick()
        // Scroll to first highlighted word - find the line element in Virtua's rendered items
        const lineEl = document.querySelector(`[data-line-index="${lineIndex}"]`)
        if (lineEl) {
            let targetElement = lineEl.querySelector('.global-search-snippet-bg')
            if (!targetElement) {
                targetElement = lineEl.querySelector('.global-search-highlight')
            }
            if (targetElement) {
                targetElement.scrollIntoView({ block: 'nearest' })
                // Add fade animation
                await nextTick()
                const snippetBg = lineEl.querySelector('.global-search-snippet-bg')
                if (snippetBg) {
                    snippetBg.classList.add('fade-animation')
                    setTimeout(() => snippetBg.classList.remove('fade-animation'), 3000)
                }
            }
        }
    }
}

// LIFECYCLE
onMounted(() => {
    detectVisibleLine(emit)
})

onUnmounted(() => {
    saveScrollPosition()
    viewerState.cleanup()
})

// COMPONENT EXPORTS
defineExpose({
    handleTocSelection,
    async scrollToLineIndex(index?: number | null) {
        const target = index !== undefined && index !== null ? index : selectedLineIndex.value
        if (target === undefined || target === null) return
        await scrollToLine(target)
    },
    scrollToLineWithFadeHighlight,
    virtuaRef,
    viewerState,
    // Search methods
    performLineSearch,
    nextLineSearchMatch,
    previousLineSearchMatch,
    clearLineSearch,
    // Search state - expose as getters for reactivity
    get currentMatchIndex() {
        return lineSearchCurrentMatchIndex.value
    },
    get totalMatches() {
        return lineSearchTotalMatches.value
    }
})
</script>

<style scoped>
.line-scroll-container {
    height: 100%;
    padding: 8px 12px;
    outline: none;
}

/* Global search snippet background animation */
.line-scroll-container :deep(.global-search-snippet-bg.fade-animation) {
    animation: fadeSearchHighlight 3s ease-out forwards;
}

@keyframes fadeSearchHighlight {
    0% {
        background-color: rgba(245, 158, 11, 0.3);
    }

    100% {
        background-color: transparent;
    }
}

:root.dark .line-scroll-container :deep(.global-search-snippet-bg.fade-animation) {
    animation: fadeSearchHighlightDark 3s ease-out forwards;
}

@keyframes fadeSearchHighlightDark {
    0% {
        background-color: rgba(251, 191, 36, 0.3);
    }

    100% {
        background-color: transparent;
    }
}

/* Global search highlighting - foreground color */
.line-scroll-container :deep(.global-search-highlight) {
    color: var(--accent-color);
}

/* Current search match - uses global mark styling, just add weight */
.line-scroll-container :deep(mark.current) {
    font-weight: 600;
}
</style>
