<template>
    <div v-if="viewerState.totalLines.value > 0"
         class="height-fill"
         style="position: relative;">
        <GenericSearch ref="searchRef"
                       :is-open="isSearchOpen"
                       @close="isSearchOpen = false"
                       @search-query-change="handleSearchQueryChange"
                       @navigate-to-match="handleNavigateToMatch" />

        <DynamicScroller ref="scrollerRef"
                         class="scroller height-fill line-viewer"
                         :style="containerStyles"
                         :class="{ 'initial-loading': isInitialLoading }"
                         :items="virtualItems"
                         :min-item-size="minItemSize"
                         :buffer="300"
                         key-field="index"
                         tabindex="0"
                         @keydown="handleKeyDown"
                         @click="() => scrollerRef?.$el?.focus()"
                         @scroll.passive="handleScrollForPositionTracking">

            <template #default="{ item, index, active }">
                <DynamicScrollerItem :item="item"
                                     :active="active"
                                     :size-dependencies="[
                                        item.content,
                                        myTab?.bookState?.isLineDisplayInline,
                                        myTab?.bookState?.showAltToc
                                    ]"
                                     :data-index="index"
                                     :data-line-index-observer="index">
                    <BookLine :content="item.content || '\u00A0'"
                              :line-index="index"
                              :is-selected="selectedLineIndex === index"
                              :inline-mode="myTab?.bookState?.isLineDisplayInline || false"
                              :alt-toc-entries="item.altTocEntries"
                              :show-alt-toc="myTab?.bookState?.showAltToc"
                              :class="{
                                'show-selection': myTab?.bookState?.showBottomPane
                            }"
                              @line-click="handleLineClick" />
                </DynamicScrollerItem>
            </template>
        </DynamicScroller>
    </div>
</template>

<script setup lang="ts">
import { ref, watch, nextTick, onMounted, onUnmounted, computed } from 'vue'
import { DynamicScroller, DynamicScrollerItem } from 'vue3-virtual-scroller'
import BookLine from './BookLine.vue'
import GenericSearch from './common/GenericSearch.vue'
import { BookLineViewerService } from '../services/bookLineViewerService'

import { useContentSearch } from '../composables/useContentSearch'
import { useTabStore } from '../stores/tabStore'
import { useSettingsStore } from '../stores/settingsStore'

const tabStore = useTabStore()
const settingsStore = useSettingsStore()

// Reactive dark mode detection
const isDarkMode = ref(false)

const updateDarkMode = () => {
    isDarkMode.value = document.documentElement.classList.contains('dark')
}

onMounted(() => {
    updateDarkMode()
    // Watch for theme changes
    const observer = new MutationObserver(updateDarkMode)
    observer.observe(document.documentElement, {
        attributes: true,
        attributeFilter: ['class']
    })

    // Cleanup observer on unmount
    onUnmounted(() => observer.disconnect())
})

// Computed styles that respect dark mode and reading background
const containerStyles = computed(() => ({
    backgroundColor: !isDarkMode.value && settingsStore.readingBackgroundColor
        ? settingsStore.readingBackgroundColor
        : 'var(--bg-primary)',
    color: !isDarkMode.value && settingsStore.readingBackgroundColor
        ? 'var(--reading-text-color)'
        : 'var(--text-primary)'
}))

const props = defineProps<{
    tabId?: number
    altTocByLineIndex?: Map<number, import('../services/bookTocService').AltTocLineEntry[]>
}>()

const emit = defineEmits<{
    updateScrollPosition: [lineIndex: number]
    placeholdersReady: []
    lineClick: [lineIndex: number]
    clearOtherSelections: []
}>()

const myTab = computed(() => tabStore.tabs.find(t => t.id === props.tabId))
const selectedLineIndex = ref<number | null>(null)
const isInitialLoading = ref(false)

const viewerState = new BookLineViewerService()
const scrollerRef = ref<InstanceType<typeof DynamicScroller> | null>(null)
const searchRef = ref<InstanceType<typeof GenericSearch> | null>(null)

// Minimum item size for the scroller - account for variable heights
// Minimum item size for the scroller - be more conservative to prevent scroll issues
const minItemSize = computed(() => {
    const isInline = myTab.value?.bookState?.isLineDisplayInline || false

    // Use more conservative estimates to prevent virtual scroller height miscalculation
    if (isInline) {
        return 24 // Smaller for inline mode
    } else {
        return 40 // Conservative base size for block mode
    }
})

// Create virtual items array for the scroller
const virtualItems = computed(() => {
    const items = []
    const lines = viewerState.lines.value
    const diacriticsState = myTab.value?.bookState?.diacriticsState
    const query = search.searchQuery.value
    const currentMatch = search.currentMatch.value

    for (let i = 0; i < viewerState.totalLines.value; i++) {
        const line = lines[i]
        let processedContent = line || '\u00A0'

        // Apply diacritics filtering
        if (processedContent !== '\u00A0' && diacriticsState && diacriticsState > 0) {
            processedContent = applyDiacriticsFilter(processedContent, diacriticsState)
        }

        // Apply search highlighting
        if (processedContent !== '\u00A0' && query) {
            const currentOccurrence = currentMatch?.itemIndex === i ? currentMatch.occurrence : -1
            processedContent = search.highlightMatches(processedContent, query, currentOccurrence)
        }

        items.push({
            index: i,
            content: processedContent,
            altTocEntries: props.altTocByLineIndex?.get(i)
        })
    }

    return items
})

// Use content search composable
const search = useContentSearch()

// Sync search state with tab store
const isSearchOpen = computed({
    get: () => myTab.value?.bookState?.isSearchOpen || false,
    set: (value) => {
        if (myTab.value?.bookState) {
            myTab.value.bookState.isSearchOpen = value
        }
    }
})

// Watch for selectedLineIndex changes to restore selection
watch(() => myTab.value?.bookState?.selectedLineIndex, (newIndex) => {
    if (newIndex !== undefined) {
        selectedLineIndex.value = newIndex
    }
}, { immediate: true })

function handleLineClick(lineIndex: number) {
    selectedLineIndex.value = lineIndex
    // Save selected line to tab state
    if (myTab.value?.bookState) {
        myTab.value.bookState.selectedLineIndex = lineIndex
    }
    emit('lineClick', lineIndex)
}

function handleKeyDown(e: KeyboardEvent) {
    if ((e.ctrlKey || e.metaKey) && e.key === 'f') {
        e.preventDefault()
        isSearchOpen.value = true
        return
    }

    if ((e.ctrlKey || e.metaKey) && e.key === 'a') {
        e.preventDefault()
        e.stopPropagation()
        selectAllInContainer()
        return
    }

    // Handle Ctrl+Home and Ctrl+End for virtualized content
    if (e.ctrlKey || e.metaKey) {
        if (e.key === 'Home') {
            e.preventDefault()
            scrollToLine(0)
        } else if (e.key === 'End') {
            e.preventDefault()
            const lastLine = viewerState.totalLines.value - 1
            if (lastLine >= 0) {
                scrollToLine(lastLine)
            }
        }
    }
}

// Current search query for reactive updates
const currentSearchQuery = ref('')

function handleSearchQueryChange(query: string) {
    currentSearchQuery.value = query
    performSearch()
}

async function performSearch() {
    const query = currentSearchQuery.value
    if (!query.trim()) {
        search.searchInItems([], query)
        searchRef.value?.setMatches(0)
        return
    }

    // Start progressive search with immediate feedback
    searchRef.value?.setMatches(0) // Reset count

    try {
        const allMatches = await viewerState.searchProgressively(
            query,
            (progressiveMatches: any) => {
                // Update search results as they come in
                search.updateMatches(progressiveMatches)
                searchRef.value?.setMatches(search.totalMatches.value)
            }
        )

        // Final update with complete results
        search.updateMatches(allMatches)
        searchRef.value?.setMatches(search.totalMatches.value)

    } catch (error) {
        console.error('Search failed:', error)
        search.searchInItems([], query)
        searchRef.value?.setMatches(0)
    }
}

// Re-search when new content loads
watch(() => Object.keys(viewerState.lines.value).length, () => {
    if (currentSearchQuery.value.trim()) {
        performSearch()
    }
})

// Flag to prevent scroll position tracking during programmatic navigation
const isNavigating = ref(false)

function handleNavigateToMatch(matchIndex: number) {
    search.navigateToMatch(matchIndex)
    const match = search.currentMatch.value
    if (match) {
        // Set flag to prevent scroll tracking interference
        isNavigating.value = true

        // Use the same reliable scroll mechanism as TOC navigation
        // This will scroll the line to center and handle virtualization properly
        scrollToLine(match.itemIndex)

        // After the line is in view, wait a bit for the highlight to render
        // then fine-tune the scroll to center the highlighted text
        setTimeout(() => {
            const currentMark = document.querySelector('.line-viewer mark.current')
            if (currentMark) {
                // Fine-tune scroll to center the highlighted text within the line
                currentMark.scrollIntoView({ behavior: 'auto', block: 'center' })
            }

            // Re-enable scroll tracking after navigation completes
            setTimeout(() => {
                isNavigating.value = false
            }, 100)
        }, 150) // Shorter timeout since scrollToLine handles the main positioning
    }
}

let scrollUpdateTimeout: any = null

function handleScrollForPositionTracking() {
    // Only track scroll position, don't interfere with navigation
    if (!scrollerRef.value || viewerState.isInitialLoad) return

    if (scrollUpdateTimeout !== null) {
        clearTimeout(scrollUpdateTimeout)
        scrollUpdateTimeout = null
    }

    scrollUpdateTimeout = setTimeout(() => {
        // Skip if currently navigating to avoid interference
        if (isNavigating.value) return

        // Get the actual top visible line ID from DOM, not estimated from scroll position
        const topVisibleLineIndex = getTopVisibleLineIndex()

        if (topVisibleLineIndex !== null && topVisibleLineIndex >= 0 && topVisibleLineIndex < viewerState.totalLines.value) {
            // Only save to tab state, don't emit events that might cause interference
            if (myTab.value?.bookState) {
                myTab.value.bookState.initialLineIndex = topVisibleLineIndex
            }
        }
    }, 500) // Longer delay to avoid interference with navigation
}

function getTopVisibleLineIndex(): number | null {
    if (!scrollerRef.value?.$el) return null

    const scrollerEl = scrollerRef.value.$el
    const scrollerRect = scrollerEl.getBoundingClientRect()

    // Find all line elements currently rendered
    const lineElements = scrollerEl.querySelectorAll('[data-line-index-observer]')

    let topMostLine: { element: Element; lineIndex: number; top: number } | null = null

    for (const lineEl of lineElements) {
        const lineRect = lineEl.getBoundingClientRect()

        // Check if this line is visible in the viewport
        if (lineRect.bottom > scrollerRect.top && lineRect.top < scrollerRect.bottom) {
            const lineIndex = parseInt(lineEl.getAttribute('data-line-index-observer') || '-1')
            if (lineIndex >= 0) {
                // Find the line that's closest to the top of the viewport
                if (!topMostLine || lineRect.top < topMostLine.top) {
                    topMostLine = {
                        element: lineEl,
                        lineIndex: lineIndex,
                        top: lineRect.top
                    }
                }
            }
        }
    }

    // Return the line that's actually at the top, not just any visible line
    if (topMostLine) {
        // Only return if the line is actually at or very close to the top
        const distanceFromTop = topMostLine.top - scrollerRect.top
        if (distanceFromTop >= -10 && distanceFromTop <= 50) { // Allow small tolerance
            return topMostLine.lineIndex
        }
    }

    return null
}

function handleScrollDebounced() {
    // Temporarily disabled to debug jump-back issue
    // TODO: Re-enable with proper navigation protection
    return

    if (!scrollerRef.value || viewerState.isInitialLoad || isNavigating.value) return

    if (scrollUpdateTimeout !== null) {
        clearTimeout(scrollUpdateTimeout)
        scrollUpdateTimeout = null
    }
    scrollUpdateTimeout = window.setTimeout(() => {
        // Skip if still navigating
        if (isNavigating.value) return

        // For virtual scroller, we need to estimate the top visible line
        // based on scroll position and item size
        const scrollTop = scrollerRef.value?.$el?.scrollTop || 0
        const estimatedTopLine = Math.floor(scrollTop / minItemSize.value)

        if (estimatedTopLine >= 0 && estimatedTopLine < viewerState.totalLines.value) {
            // Save scroll position to tab state
            if (myTab.value?.bookState) {
                myTab.value.bookState.initialLineIndex = estimatedTopLine
            }
            emit('updateScrollPosition', estimatedTopLine)
        }
    }, 300)
}

function handleScrollUpdate(topLineIndex: number) {
    if (scrollUpdateTimeout !== null) {
        clearTimeout(scrollUpdateTimeout)
        scrollUpdateTimeout = null
    }
    scrollUpdateTimeout = window.setTimeout(() => {
        // Save scroll position to tab state
        if (myTab.value?.bookState) {
            myTab.value.bookState.initialLineIndex = topLineIndex
        }
        emit('updateScrollPosition', topLineIndex)
    }, 300)
}

// Load book when bookId changes
watch(() => myTab.value?.bookState?.bookId, async (bookId, oldBookId) => {
    if (bookId && bookId !== oldBookId) {
        const initialLineIndex = myTab.value?.bookState?.initialLineIndex
        const isRestore = oldBookId === undefined && initialLineIndex !== undefined

        // Set loading state if we need to scroll to a specific position
        if (initialLineIndex !== undefined) {
            isInitialLoading.value = true
        }

        await viewerState.loadBook(bookId, isRestore, initialLineIndex)
        await nextTick()

        emit('placeholdersReady')

        // Scroll to initial position if provided (session resume)
        if (initialLineIndex !== undefined) {
            // Set navigation flag to prevent interference
            isNavigating.value = true

            // Wait a bit more for the virtual scroller to initialize
            setTimeout(async () => {
                await scrollToLine(initialLineIndex)
                // Clear loading state after scroll completes
                isInitialLoading.value = false

                // Re-enable scroll tracking after restoration
                setTimeout(() => {
                    isNavigating.value = false
                }, 200)
            }, 100)
        } else {
            isInitialLoading.value = false
        }
    }
}, { immediate: true })

// Restore scroll when tab becomes active
watch(() => myTab.value?.isActive, async (isActive, wasActive) => {
    const bookId = myTab.value?.bookState?.bookId
    const initialLineIndex = myTab.value?.bookState?.initialLineIndex
    if (isActive && !wasActive && bookId && viewerState.totalLines.value > 0) {
        await nextTick()
        if (initialLineIndex !== undefined) {
            // Set navigation flag to prevent interference
            isNavigating.value = true

            // Wait for virtual scroller to be ready
            setTimeout(async () => {
                await scrollToLine(initialLineIndex)

                // Re-enable scroll tracking after restoration
                setTimeout(() => {
                    isNavigating.value = false
                }, 200)
            }, 50)
        }
    }
})

async function scrollToLine(lineIndex: number) {
    if (!scrollerRef.value) return

    // Prioritize loading lines around the target for smooth scrolling
    await viewerState.prioritizeLines(lineIndex, 100)

    // Wait for DOM update
    await nextTick()

    // Hide scrolling during the double-call hack to prevent flickering
    const scrollerEl = scrollerRef.value.$el
    if (scrollerEl) {
        // Temporarily disable scroll events and hide overflow to prevent flickering
        scrollerEl.style.overflow = 'hidden'
        scrollerEl.style.pointerEvents = 'none'
    }

    try {
        // First call
        scrollerRef.value.scrollToItem(lineIndex)

        // Wait a bit and call it again (the hack!) - but hidden
        setTimeout(() => {
            if (scrollerRef.value) {
                scrollerRef.value.scrollToItem(lineIndex)

                // Re-enable scrolling after the second call
                setTimeout(() => {
                    if (scrollerEl) {
                        scrollerEl.style.overflow = ''
                        scrollerEl.style.pointerEvents = ''
                    }
                }, 10) // Very short delay to ensure scroll completes
            }
        }, 50)

    } catch (error) {
        console.warn('Failed to scroll to line:', lineIndex, error)
        // Fallback: try to scroll manually twice (also hidden)
        const scrollTop = lineIndex * minItemSize.value
        if (scrollerEl) {
            scrollerEl.scrollTop = scrollTop
            setTimeout(() => {
                if (scrollerEl) {
                    scrollerEl.scrollTop = scrollTop
                    // Re-enable scrolling after fallback
                    setTimeout(() => {
                        scrollerEl.style.overflow = ''
                        scrollerEl.style.pointerEvents = ''
                    }, 10)
                }
            }, 50)
        }
    }
}

async function handleTocSelection(lineIndex: number) {
    // Set flag to prevent scroll tracking interference
    isNavigating.value = true

    await viewerState.handleTocSelection(lineIndex)

    // Scroll immediately using the improved scroller with verification
    await nextTick()
    await scrollToLine(lineIndex)

    // Re-enable scroll tracking after TOC navigation completes
    setTimeout(() => {
        isNavigating.value = false
    }, 200)
}

// Handle selection directly
function selectAllInContainer() {
    const scrollerEl = scrollerRef.value?.$el
    if (!scrollerEl) return

    const selection = window.getSelection()
    if (!selection) return

    const range = document.createRange()
    range.selectNodeContents(scrollerEl)
    selection.removeAllRanges()
    selection.addRange(range)

    emit('clearOtherSelections')
}

// Helper function to apply diacritics filtering to HTML content
function applyDiacriticsFilter(htmlContent: string, state: number): string {
    if (!htmlContent || state === 0) return htmlContent

    // Create a temporary div to parse HTML
    const tempDiv = document.createElement('div')
    tempDiv.innerHTML = htmlContent

    const walker = document.createTreeWalker(
        tempDiv,
        NodeFilter.SHOW_TEXT,
        null
    )

    const textNodes: Text[] = []
    let node: Node | null
    while ((node = walker.nextNode())) {
        textNodes.push(node as Text)
    }

    textNodes.forEach(textNode => {
        if (!textNode) return

        let text = textNode.nodeValue || ''

        // State 1: Remove cantillations only (U+0591-U+05AF)
        if (state >= 1) {
            text = text.replace(/[\u0591-\u05AF]/g, '')
        }

        // State 2: Remove nikkud as well (U+05B0-U+05BD, U+05C1, U+05C2, U+05C4, U+05C5)
        if (state >= 2) {
            text = text.replace(/[\u05B0-\u05BD\u05C1\u05C2\u05C4\u05C5]/g, '')
            // Replace ? and ! with . and remove em dash (—)
            text = text.replace(/[?!]/g, '.').replace(/—/g, '')
        }

        textNode.nodeValue = text
    })

    return tempDiv.innerHTML
}

onUnmounted(() => {
    viewerState.cleanup()

    // Save current scroll position when unmounting for session restoration
    if (myTab.value?.bookState) {
        const topVisibleLineIndex = getTopVisibleLineIndex()

        if (topVisibleLineIndex !== null && topVisibleLineIndex >= 0 && topVisibleLineIndex < viewerState.totalLines.value) {
            myTab.value.bookState.initialLineIndex = topVisibleLineIndex
            emit('updateScrollPosition', topVisibleLineIndex)
        }
    }
})

defineExpose({
    handleTocSelection,
    // Expose a method so parent can request an explicit scroll to a line.
    async scrollToLineIndex(index?: number | null) {
        const target = index !== undefined && index !== null ? index : selectedLineIndex.value
        if (target === undefined || target === null) return
        await scrollToLine(target)
    }
})
</script>

<style scoped>
.line-viewer {
    padding: 25px 15px;
    font-size: var(--font-size, 100%);
}

.line-viewer.initial-loading {
    visibility: hidden;
}

.scroller {
    height: 100%;
}
</style>