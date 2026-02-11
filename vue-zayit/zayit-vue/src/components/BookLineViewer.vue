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
                              :is-highlighted="highlightedLineIndex === index"
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
import { DynamicScroller, DynamicScrollerItem } from 'vue-virtual-scroller'
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

    // Add global keyboard listener for Ctrl+F
    document.addEventListener('keydown', handleGlobalKeyDown)

    // Cleanup observer on unmount
    onUnmounted(() => {
        observer.disconnect()
        document.removeEventListener('keydown', handleGlobalKeyDown)
    })
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
const highlightedLineIndex = ref<number | null>(null)
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
    // Prevent spacebar from scrolling unless in search input
    if (e.key === ' ' && e.target instanceof HTMLElement && e.target.tagName !== 'INPUT') {
        e.preventDefault()
        return
    }

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

// Global keyboard handler for Ctrl+F (works even when component doesn't have focus)
const handleGlobalKeyDown = (e: KeyboardEvent) => {
    // Only handle if this is the active tab
    if (tabStore.activeTab?.id !== props.tabId) return

    // Don't interfere if user is typing in an input
    const activeElement = document.activeElement
    if (activeElement && (
        activeElement.tagName === 'INPUT' ||
        activeElement.tagName === 'TEXTAREA' ||
        (activeElement as HTMLElement).contentEditable === 'true'
    )) {
        return
    }

    if ((e.ctrlKey || e.metaKey) && e.key === 'f') {
        e.preventDefault()
        isSearchOpen.value = true
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

    // Set the search query immediately so highlights can be applied
    search.searchQuery.value = query

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

        // Scroll to the line containing the match
        scrollToLine(match.itemIndex)

        // After scrolling, find and scroll to the actual mark element
        setTimeout(() => {
            const scrollerEl = scrollerRef.value?.$el
            const currentMark = scrollerEl?.querySelector('.line-viewer mark.current')
            if (currentMark) {
                currentMark.scrollIntoView({ behavior: 'auto', block: 'center' })
            }

            // Re-enable scroll tracking after a longer delay to prevent jump-back
            setTimeout(() => {
                isNavigating.value = false
            }, 500)
        }, 200) // Wait for scrollToLine to complete (it has internal delays)
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

        // Get the actual top visible line and its offset from viewport top
        const position = getTopVisibleLinePosition()

        if (position && position.lineIndex >= 0 && position.lineIndex < viewerState.totalLines.value) {
            // Save both line index and its pixel offset for accurate restoration
            if (myTab.value?.bookState) {
                myTab.value.bookState.initialLineIndex = position.lineIndex
                myTab.value.bookState.lineOffset = position.offset
            }
        }
    }, 500) // Longer delay to avoid interference with navigation
}

function getTopVisibleLinePosition(): { lineIndex: number; offset: number } | null {
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

    // Return the topmost visible line with its offset from viewport top
    if (topMostLine) {
        const offset = topMostLine.top - scrollerRect.top
        return {
            lineIndex: topMostLine.lineIndex,
            offset: offset // Negative if line is scrolled up past viewport top, positive if below
        }
    }

    // Fallback: estimate from scroll position if no rendered lines found
    const scrollTop = scrollerEl.scrollTop || 0
    const estimatedLine = Math.floor(scrollTop / minItemSize.value)
    if (estimatedLine >= 0 && estimatedLine < viewerState.totalLines.value) {
        return {
            lineIndex: estimatedLine,
            offset: 0 // No offset information available in fallback
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
                const lineOffset = myTab.value?.bookState?.lineOffset
                await scrollToLine(initialLineIndex, lineOffset)
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
                const lineOffset = myTab.value?.bookState?.lineOffset
                await scrollToLine(initialLineIndex, lineOffset)

                // Re-enable scroll tracking after restoration
                setTimeout(() => {
                    isNavigating.value = false
                }, 200)
            }, 50)
        }
    }
})

async function scrollToLine(lineIndex: number, pixelOffset?: number) {
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
            if (scrollerRef.value && scrollerEl) {
                scrollerRef.value.scrollToItem(lineIndex)

                // If we have a pixel offset, apply it after the scroll completes
                if (pixelOffset !== undefined && pixelOffset !== 0) {
                    setTimeout(() => {
                        if (scrollerEl) {
                            // Adjust scroll position by the offset to restore exact position
                            // Negative offset means the line was scrolled up, so we scroll down more
                            scrollerEl.scrollTop = scrollerEl.scrollTop - pixelOffset
                        }
                    }, 20)
                }

                // Re-enable scrolling after the second call
                setTimeout(() => {
                    if (scrollerEl) {
                        scrollerEl.style.overflow = ''
                        scrollerEl.style.pointerEvents = ''
                    }
                }, pixelOffset !== undefined ? 30 : 10) // Slightly longer delay if applying offset
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

                    // Apply offset in fallback too
                    if (pixelOffset !== undefined && pixelOffset !== 0) {
                        setTimeout(() => {
                            if (scrollerEl) {
                                scrollerEl.scrollTop = scrollerEl.scrollTop - pixelOffset
                            }
                        }, 20)
                    }

                    // Re-enable scrolling after fallback
                    setTimeout(() => {
                        scrollerEl.style.overflow = ''
                        scrollerEl.style.pointerEvents = ''
                    }, pixelOffset !== undefined ? 30 : 10)
                }
            }, 50)
        }
    }
}

// Scroll to line and highlight it temporarily
async function scrollToLineAndHighlight(lineIndex: number) {
    await scrollToLine(lineIndex)

    // Set highlight after scroll completes
    setTimeout(() => {
        highlightedLineIndex.value = lineIndex

        // Remove highlight after 3 seconds
        setTimeout(() => {
            highlightedLineIndex.value = null
        }, 3000)
    }, 300) // Wait for scroll animation to complete
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
        const position = getTopVisibleLinePosition()

        if (position && position.lineIndex >= 0 && position.lineIndex < viewerState.totalLines.value) {
            myTab.value.bookState.initialLineIndex = position.lineIndex
            myTab.value.bookState.lineOffset = position.offset
            emit('updateScrollPosition', position.lineIndex)
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
    },
    // Expose method to scroll and highlight a line (for search results)
    async scrollToLineAndHighlight(index: number) {
        await scrollToLineAndHighlight(index)
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