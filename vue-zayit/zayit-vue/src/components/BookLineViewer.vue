<template>
    <div v-if="viewerState.totalLines.value > 0"
         class="height-fill"
         style="position: relative;">
        <GenericSearch ref="searchRef"
                       :is-open="isSearchOpen"
                       :current-match-index="search.currentMatchIndex.value"
                       :total-matches="search.totalMatches.value"
                       top-offset="4px"
                       @close="handleSearchClose"
                       @search="handleSearch"
                       @next="handleSearchNext"
                       @previous="handleSearchPrevious" />

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
import { useFocus, useEventListener } from '@vueuse/core'
import { DynamicScroller, DynamicScrollerItem } from 'vue-virtual-scroller'
import BookLine from './BookLine.vue'
import GenericSearch from './common/GenericSearch.vue'
import { BookLineViewerService } from '../services/bookLineViewerService'

import { useVirtualizedSearch } from '../composables/useVirtualizedSearch'
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
    onUnmounted(() => {
        observer.disconnect()
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

// Track if this component's scroller has focus
const scrollerElRef = computed(() => scrollerRef.value?.$el as HTMLElement | undefined)
const { focused: hasFocus } = useFocus(scrollerElRef)

// Keyboard shortcuts using useEventListener to support any keyboard layout
useEventListener('keydown', (event: KeyboardEvent) => {
    if (!hasFocus.value) return

    const hasCtrlOrMeta = event.ctrlKey || event.metaKey

    // Ctrl+F: Open search (use event.code for keyboard layout independence)
    if (hasCtrlOrMeta && event.code === 'KeyF') {
        event.preventDefault()
        isSearchOpen.value = true
    }

    // Ctrl+A: Select all (use event.code for keyboard layout independence)
    if (hasCtrlOrMeta && event.code === 'KeyA') {
        event.preventDefault()
        selectAllInContainer()
    }
})

// Handle keydown for Space key prevention (keep in template handler)
function handleKeyDown(e: KeyboardEvent) {
    // Prevent spacebar from scrolling unless in search input
    if (e.key === ' ' && e.target instanceof HTMLElement && e.target.tagName !== 'INPUT') {
        e.preventDefault()
        return
    }
}

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
// Memoize highlighting to prevent unnecessary recalculations
const virtualItems = computed(() => {
    const items = []
    const lines = viewerState.lines.value
    const diacriticsState = myTab.value?.bookState?.diacriticsState
    const currentMatch = search.currentMatch.value
    const query = search.searchQuery.value

    // Build a set of line indices that have matches for quick lookup
    const linesWithMatches = new Set<number>()
    if (query) {
        search.allMatches.value.forEach(match => {
            linesWithMatches.add(match.lineIndex)
        })
    }

    for (let i = 0; i < viewerState.totalLines.value; i++) {
        const line = lines[i]
        let processedContent = line || '\u00A0'

        // Apply diacritics filtering
        if (processedContent !== '\u00A0' && diacriticsState && diacriticsState > 0) {
            processedContent = applyDiacriticsFilter(processedContent, diacriticsState)
        }

        // Apply search highlighting only to lines that have matches
        if (processedContent !== '\u00A0' && query && linesWithMatches.has(i)) {
            const isCurrentLine = currentMatch?.lineIndex === i
            processedContent = search.highlightInContent(processedContent, isCurrentLine)
        }

        items.push({
            index: i,
            content: processedContent,
            altTocEntries: props.altTocByLineIndex?.get(i)
        })
    }

    return items
})

// Use virtualized search composable
const search = useVirtualizedSearch()

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

// Search handlers
// Flag to prevent scroll position tracking during programmatic navigation
const isNavigating = ref(false)

function handleSearchClose() {
    isSearchOpen.value = false
    search.clear()
}

async function handleSearch(query: string) {
    if (!query.trim()) {
        search.clear()
        return
    }

    // Get current scroll position to find closest match
    let currentLineIndex: number | undefined

    if (scrollerRef.value?.$el) {
        const scrollerEl = scrollerRef.value.$el
        const scrollTop = scrollerEl.scrollTop || 0
        // Estimate current line from scroll position
        currentLineIndex = Math.floor(scrollTop / minItemSize.value)

        // Clamp to valid range
        if (currentLineIndex < 0) currentLineIndex = 0
        if (currentLineIndex >= viewerState.totalLines.value) {
            currentLineIndex = viewerState.totalLines.value - 1
        }
    }

    await search.search(query, viewerState.lines.value, viewerState.totalLines.value, currentLineIndex)

    // Auto-scroll to closest match
    if (search.currentMatch.value) {
        await scrollToMatch(search.currentMatch.value.lineIndex)
    }
}

async function handleSearchNext() {
    search.nextMatch()
    if (search.currentMatch.value) {
        await scrollToMatch(search.currentMatch.value.lineIndex)
    }
}

async function handleSearchPrevious() {
    search.previousMatch()
    if (search.currentMatch.value) {
        await scrollToMatch(search.currentMatch.value.lineIndex)
    }
}

async function scrollToMatch(lineIndex: number) {
    // Set navigation flag
    isNavigating.value = true

    // Prioritize loading lines around the match
    await viewerState.prioritizeLines(lineIndex, 100)

    // Check if the line is already in view
    await nextTick()
    const scrollerEl = scrollerRef.value?.$el
    if (scrollerEl) {
        const lineEl = scrollerEl.querySelector(`[data-line-index-observer="${lineIndex}"]`)

        if (lineEl) {
            // Line is rendered, check if it's in viewport
            const lineRect = lineEl.getBoundingClientRect()
            const scrollerRect = scrollerEl.getBoundingClientRect()
            const searchBarOffset = 50

            const isInView = lineRect.top >= (scrollerRect.top + searchBarOffset) &&
                lineRect.bottom <= scrollerRect.bottom

            if (!isInView) {
                // Only scroll if not in view
                await scrollToLine(lineIndex)
                await nextTick()
            }
        } else {
            // Line not rendered, need to scroll to it
            await scrollToLine(lineIndex)
            await nextTick()
        }
    }

    // Update current mark styling via DOM (avoid full re-render)
    setTimeout(() => {
        updateCurrentMarkStyling()

        // Check if current mark is visible, scroll only if needed
        const currentMark = scrollerEl?.querySelector('mark.current')
        if (currentMark && scrollerEl) {
            const markRect = currentMark.getBoundingClientRect()
            const scrollerRect = scrollerEl.getBoundingClientRect()
            const searchBarOffset = 50

            // Check if mark is hidden behind search bar or outside viewport
            const isMarkInView = markRect.top >= (scrollerRect.top + searchBarOffset) &&
                markRect.bottom <= scrollerRect.bottom

            if (!isMarkInView) {
                // Only scroll if mark is not fully visible
                if (markRect.top < scrollerRect.top + searchBarOffset) {
                    // Scroll up a bit to clear the search bar
                    scrollerEl.scrollTop -= (scrollerRect.top + searchBarOffset - markRect.top + 10)
                } else {
                    // Use normal scroll behavior
                    currentMark.scrollIntoView({ behavior: 'smooth', block: 'nearest' })
                }
            }
        }

        // Clear navigation flag
        setTimeout(() => {
            isNavigating.value = false
        }, 300)
    }, 50)
}

// Update current mark styling without triggering full re-render
function updateCurrentMarkStyling() {
    const scrollerEl = scrollerRef.value?.$el
    if (!scrollerEl) return

    const currentMatch = search.currentMatch.value
    if (!currentMatch) return

    // Remove 'current' class from all marks
    const allMarks = scrollerEl.querySelectorAll('mark.current')
    allMarks.forEach((mark: Element) => mark.classList.remove('current'))

    // Find the line element
    const lineEl = scrollerEl.querySelector(`[data-line-index-observer="${currentMatch.lineIndex}"]`)
    if (!lineEl) return

    // Find all marks in this line and add 'current' to the right one
    const marks = lineEl.querySelectorAll('mark')
    if (marks[currentMatch.matchIndex]) {
        marks[currentMatch.matchIndex].classList.add('current')
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

// Handle copy event to copy full source content (not just visible virtual items)
function handleCopy(event: ClipboardEvent) {
    console.log('[BookLineViewer] Copy event detected')

    const selection = window.getSelection()
    if (!selection || selection.rangeCount === 0) {
        console.log('[BookLineViewer] No selection, skipping')
        return
    }

    // Check if the selection is within our scroller
    const scrollerEl = scrollerRef.value?.$el
    if (!scrollerEl) {
        console.log('[BookLineViewer] No scroller element, skipping')
        return
    }

    const range = selection.getRangeAt(0)
    if (!scrollerEl.contains(range.commonAncestorContainer)) {
        console.log('[BookLineViewer] Selection not in scroller, skipping')
        return
    }

    console.log('[BookLineViewer] Copying all source content...')

    // Get all source lines as HTML
    const lines = viewerState.lines.value
    const diacriticsState = myTab.value?.bookState?.diacriticsState

    let htmlContent = ''
    let textContent = ''

    for (let i = 0; i < viewerState.totalLines.value; i++) {
        let line = lines[i] || '\u00A0'

        // Apply diacritics filtering if needed
        if (line !== '\u00A0' && diacriticsState && diacriticsState > 0) {
            line = applyDiacriticsFilter(line, diacriticsState)
        }

        htmlContent += `<div>${line}</div>\n`

        // For plain text, strip HTML tags
        const tempDiv = document.createElement('div')
        tempDiv.innerHTML = line
        textContent += (tempDiv.textContent || tempDiv.innerText || '') + '\n'
    }

    // Set clipboard data
    event.clipboardData?.setData('text/html', htmlContent)
    event.clipboardData?.setData('text/plain', textContent)
    event.preventDefault()

    console.log('[BookLineViewer] ✅ Copied all content to clipboard:', {
        totalLines: viewerState.totalLines.value,
        htmlLength: htmlContent.length,
        textLength: textContent.length
    })
}

// Set up copy event listener using useEventListener for automatic cleanup
useEventListener(scrollerElRef, 'copy', handleCopy)

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
    // Scroll to line and apply fade-out highlight effect
    async scrollToLineWithFadeHighlight(lineIndex: number) {
        // Scroll to the line first
        await scrollToLine(lineIndex)

        // Set the highlighted line
        highlightedLineIndex.value = lineIndex

        // Clear the highlight after animation completes (3 seconds)
        setTimeout(() => {
            highlightedLineIndex.value = null
        }, 3000)
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