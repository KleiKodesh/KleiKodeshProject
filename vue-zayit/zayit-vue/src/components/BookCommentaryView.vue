<template>
    <div class="flex-column height-fill">
        <div class="flex-between bar commentary-header"
             style="position: relative;">
            <GenericSearch ref="searchRef"
                           :is-open="isSearchOpen"
                           :current-match-index="search.currentMatchIndex.value"
                           :total-matches="search.totalMatches.value"
                           top-offset="calc(100% + 8px)"
                           @close="handleSearchClose"
                           @search="handleSearch"
                           @next="handleSearchNext"
                           @previous="handleSearchPrevious" />
            <span class="bold smaller-em commentary-title">{{ commentaryTitle }}</span>

            <div class="flex-row flex-center commentary-navigation">

                <button class="flex-center c-pointer nav-btn"
                        @click="previousLine"
                        :disabled="props.selectedLineIndex === undefined || props.selectedLineIndex <= 0"
                        title="שורה קודמת">
                    <Icon icon="fluent:chevron-right-28-regular"
                          class="small-icon" />
                </button>

                <button class="flex-center c-pointer nav-btn"
                        @click="nextLine"
                        :disabled="props.selectedLineIndex === undefined"
                        title="שורה הבאה">
                    <Icon icon="fluent:chevron-left-28-regular"
                          class="small-icon" />
                </button>

                <button class="flex-center c-pointer nav-btn"
                        @click="openSearch"
                        title="חיפוש (Ctrl+F)">
                    <Icon icon="fluent:search-28-regular"
                          class="small-icon" />
                </button>

                <CommentaryConnectionTypeFilter :book="book"
                                                :selected-connection-type-id="selectedConnectionTypeId"
                                                :available-options="availableFilterOptions"
                                                @filter-change="handleFilterChange" />

                <Combobox v-model="comboboxSelectedValue"
                          :options="groupOptions"
                          placeholder="בחר פרשן..."
                          dir="rtl" />

                <button class="flex-center c-pointer nav-btn"
                        @click="previousGroup"
                        :disabled="currentGroupIndex === 0"
                        title="קבוצה קודמת">
                    <Icon icon="fluent:chevron-up-28-regular"
                          class="small-icon" />
                </button>

                <button class="flex-center c-pointer nav-btn"
                        @click="nextGroup"
                        :disabled="currentGroupIndex === linkGroups.length - 1"
                        title="קבוצה הבאה">
                    <Icon icon="fluent:chevron-down-28-regular"
                          class="small-icon" />
                </button>
            </div>

            <button class="flex-center c-pointer commentary-close-btn"
                    @click="handleClose"
                    title="סגור פאנל">
                <Icon icon="fluent:dismiss-16-regular"
                      class="small-icon" />
            </button>
        </div>

        <div class="commentary-content-container"
             ref="commentaryContentRef"
             :style="commentaryStyles"
             tabindex="0"
             @keydown="handleKeyDown">
            <div v-if="isLoading"
                 class="flex-column flex-center height-fill text-secondary commentary-loading">
                <Icon icon="fluent:spinner-ios-20-regular"
                      class="loading-spinner" />
                <div>טוען קשרים...</div>
            </div>

            <div v-else-if="linkGroups.length === 0"
                 class="flex-column flex-center height-fill text-secondary commentary-placeholder">
                <div class="bold placeholder-text">לא נמצרו קשרים בקטגוריה זו</div>
            </div>

            <DynamicScroller v-else
                             ref="commentaryScrollerRef"
                             class="commentary-scroller"
                             :items="virtualCommentaryItems"
                             :min-item-size="commentaryMinItemSize"
                             :buffer="400"
                             key-field="id"
                             @scroll.passive="handleScrollerScroll">

                <template #default="{ item, index, active }">
                    <DynamicScrollerItem :item="item"
                                         :active="active"
                                         :size-dependencies="[
                                            item.estimatedHeight,
                                            containerWidth,
                                            item.links.map((l: any) => l.html.length).join(',')
                                        ]"
                                         :data-index="index"
                                         :data-commentary-item-observer="item.id">

                        <!-- Complete Commentary Section: Header + All Links -->
                        <section v-if="item.type === 'group-with-links'"
                                 class="commentary-section"
                                 :data-group-index="item.groupIndex">

                            <!-- Group Header -->
                            <div class="bold group-header selectable"
                                 :class="{ 'c-pointer': item.targetBookId !== undefined }"
                                 @click="handleGroupClick(item)">
                                {{ item.groupName }}
                            </div>

                            <!-- All Commentary Links for this Group -->
                            <div v-for="(link, linkIndex) in item.links"
                                 :key="`link-${linkIndex}`"
                                 class="selectable line-1.6 justify link-item"
                                 v-html="link.html">
                            </div>
                        </section>
                    </DynamicScrollerItem>
                </template>
            </DynamicScroller>
        </div>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted, watch, nextTick } from 'vue'
import { useFocus, useEventListener } from '@vueuse/core'
import { DynamicScroller, DynamicScrollerItem } from 'vue-virtual-scroller'
import Combobox, { type ComboboxOption } from './common/Combobox.vue'
import GenericSearch from './common/GenericSearch.vue'
import CommentaryConnectionTypeFilter from './CommentaryConnectionTypeFilter.vue'
import { Icon } from '@iconify/vue'

import { useContentSearch } from '../composables/useContentSearch'
import { commentaryService, type CommentaryLinkGroup } from '../services/commentaryService'
import { useTabStore } from '../stores/tabStore'
import { useSettingsStore } from '../stores/settingsStore'
import { useCategoryTreeStore } from '../stores/categoryTreeStore'
import type { Book } from '../types/Book'
import { hasConnections } from '../types/Book'

const props = withDefaults(defineProps<{
    bookId?: number
    selectedLineIndex?: number
    book?: Book
}>(), {
    bookId: undefined,
    selectedLineIndex: undefined,
    book: undefined
})

const emit = defineEmits<{
    (e: 'clearOtherSelections'): void
    (e: 'update:selectedLineIndex', newIndex: number): void
    (e: 'navigate-line', newIndex: number): void
}>()

const tabStore = useTabStore()
const settingsStore = useSettingsStore()
const categoryTreeStore = useCategoryTreeStore()

// Reactive dark mode detection
const isDarkMode = ref(false)

const updateDarkMode = () => {
    isDarkMode.value = document.documentElement.classList.contains('dark')
}

onMounted(() => {
    updateDarkMode()
    const observer = new MutationObserver(updateDarkMode)
    observer.observe(document.documentElement, {
        attributes: true,
        attributeFilter: ['class']
    })

    // Track container width for responsive height estimation
    let resizeObserver: ResizeObserver | null = null
    nextTick(() => {
        if (commentaryContentRef.value) {
            // Set initial width
            containerWidth.value = commentaryContentRef.value.clientWidth

            // Watch for resize
            resizeObserver = new ResizeObserver((entries) => {
                for (const entry of entries) {
                    const newWidth = entry.contentRect.width
                    containerWidth.value = newWidth

                    // Force multiple updates when width changes to ensure recalculation
                    nextTick(() => {
                        // Trigger resize event immediately
                        window.dispatchEvent(new Event('resize'))
                        forceScrollerUpdate()
                        // Second update after a delay to catch any late renders
                        setTimeout(() => {
                            window.dispatchEvent(new Event('resize'))
                            forceScrollerUpdate()
                        }, 200)
                    })
                }
            })
            resizeObserver.observe(commentaryContentRef.value)
        }
    })

    onUnmounted(() => {
        observer.disconnect()
        if (resizeObserver) {
            resizeObserver.disconnect()
        }
    })
})

// Computed styles that respect dark mode
const commentaryStyles = computed(() => ({
    backgroundColor: !isDarkMode.value && settingsStore.readingBackgroundColor
        ? settingsStore.readingBackgroundColor
        : 'var(--bg-primary)',
    color: !isDarkMode.value && settingsStore.readingBackgroundColor
        ? 'var(--reading-text-color)'
        : 'var(--text-primary)'
}))

// Commentary state
const linkGroups = ref<CommentaryLinkGroup[]>([])
const isLoading = ref(false)

// Get filter state from tab store, with smart default
const selectedConnectionTypeId = computed({
    get: () => {
        const activeTab = tabStore.activeTab
        if (!activeTab?.bookState) return undefined

        const saved = activeTab.bookState.commentaryFilterConnectionTypeId
        const hasExplicitFilter = activeTab.bookState.hasOwnProperty('commentaryFilterConnectionTypeId')

        if (!hasExplicitFilter && props.book) {
            const defaultFilter = commentaryService.getDefaultFilter(props.book)
            if (defaultFilter !== undefined) {
                activeTab.bookState.commentaryFilterConnectionTypeId = defaultFilter
                return defaultFilter
            }
        }

        return saved
    },
    set: (value: number | undefined) => {
        const activeTab = tabStore.activeTab
        if (activeTab?.bookState) {
            activeTab.bookState.commentaryFilterConnectionTypeId = value
        }
    }
})

// SIMPLIFIED STATE - Two separate variables for scroll tracking vs combobox editing
const currentGroupIndex = ref(0) // Tracks current scroll position (updated by intersection observer)
const comboboxSelectedValue = ref<string | number>(0) // Separate value for combobox display/editing

const commentaryScrollerRef = ref<InstanceType<typeof DynamicScroller> | null>(null)
const commentaryContentRef = ref<HTMLElement | null>(null)

// Navigation flags
const isNavigating = ref(false) // Flag to prevent scroll tracking during programmatic navigation
const lastScrollTop = ref(0) // Track last scroll position to determine direction
const scrollDirection = ref<'up' | 'down' | 'none'>('none') // Current scroll direction
const pendingNavigationTargetBookId = ref<number | undefined>(undefined) // Track target book ID for navigation
const isLoadingFromNavigation = ref(false) // Flag to indicate we're loading from next/previous line buttons

// Track container width for responsive height estimation
const containerWidth = ref(0)

// Track if this component's container has focus
const { focused: hasFocus } = useFocus(commentaryContentRef)

// Track if Ctrl+A was pressed and selection is still active (for chained Ctrl+A -> Ctrl+C shortcut)
const selectAllWasPressed = ref(false)

// Reset selectAll flag when selection changes or user interacts
useEventListener(commentaryContentRef, 'mousedown', () => {
    selectAllWasPressed.value = false
})

useEventListener(document, 'selectionchange', () => {
    // Only check if this component has focus
    if (!hasFocus.value) return
    
    // Check if selection is empty or changed
    const selection = window.getSelection()
    if (!selection || selection.rangeCount === 0 || selection.isCollapsed) {
        selectAllWasPressed.value = false
    }
})

// Keyboard shortcuts using useEventListener to support any keyboard layout
useEventListener('keydown', (event: KeyboardEvent) => {
    if (!hasFocus.value) return

    const hasCtrlOrMeta = event.ctrlKey || event.metaKey

    // Ctrl+F: Open search (use event.code for keyboard layout independence)
    if (hasCtrlOrMeta && event.code === 'KeyF') {
        event.preventDefault()
        isSearchOpen.value = true
        selectAllWasPressed.value = false
    }

    // Ctrl+A: Select all (use event.code for keyboard layout independence)
    if (hasCtrlOrMeta && event.code === 'KeyA') {
        event.preventDefault()
        selectAllWasPressed.value = true
        selectAllInContainer()
    }
    
    // Reset flag on actions that would deselect in normal circumstances
    // Arrow keys, typing, Escape, etc.
    if (!hasCtrlOrMeta && !event.shiftKey) {
        if (event.code.startsWith('Arrow') || 
            event.code === 'Escape' ||
            event.code === 'Home' ||
            event.code === 'End' ||
            event.code === 'PageUp' ||
            event.code === 'PageDown' ||
            (event.key.length === 1 && !event.ctrlKey && !event.metaKey)) {
            selectAllWasPressed.value = false
        }
    }
})

// Virtual scroller configuration with dynamic sizing
const commentaryMinItemSize = computed(() => {
    // Base calculation on actual font size and line height settings
    const baseFontSize = 16 // Base font size in pixels
    const effectiveFontSize = (baseFontSize * settingsStore.fontSize) / 100
    const effectiveLineHeight = effectiveFontSize * settingsStore.linePadding

    // Check if we have any extremely long paragraphs
    const hasExtremeParagraphs = processedLinkGroups.value.some(group =>
        group.links.some(link => {
            const textLength = link.html.replace(/<[^>]*>/g, '').length
            return textLength > 10000
        })
    )

    // Use much higher minimum for extreme content, scaled by font settings
    const baseMinSize = hasExtremeParagraphs ? 500 : 150
    const fontSizeMultiplier = settingsStore.fontSize / 105 // 105 is default
    return Math.floor(baseMinSize * fontSizeMultiplier)
})

// Virtual items for commentary scroller - GROUP ITEMS TOGETHER
const virtualCommentaryItems = computed(() => {
    const items: Array<{
        id: string
        type: 'group-with-links'
        groupIndex: number
        groupName: string
        targetBookId?: number
        targetLineIndex?: number
        links: Array<{ html: string }>
        estimatedHeight: number
    }> = []

    // Get effective width for text wrapping calculation
    // Account for padding/margins (estimate ~30px total)
    const effectiveWidth = Math.max(150, containerWidth.value - 30)

    // Calculate actual font size and line height from settings
    const baseFontSize = 16 // Base font size in pixels
    const effectiveFontSize = (baseFontSize * settingsStore.fontSize) / 100
    const effectiveLineHeight = effectiveFontSize * settingsStore.linePadding

    // Estimate characters per line based on width and actual font size
    // Hebrew characters width varies with font size
    const charWidth = effectiveFontSize * (effectiveWidth < 250 ? 0.75 : 0.65)
    const charsPerLine = Math.max(10, Math.floor(effectiveWidth / charWidth))

    processedLinkGroups.value.forEach((group, groupIndex) => {
        // Better height estimation accounting for very long paragraphs
        // Header height scales with font size
        const headerHeight = Math.ceil(effectiveFontSize * 2.5)
        let totalEstimatedHeight = headerHeight
        let maxLinkHeight = 0
        let maxLinkTextLength = 0

        group.links.forEach((link, linkIndex) => {
            // Strip HTML tags to get approximate text length
            const textLength = link.html.replace(/<[^>]*>/g, '').length

            // Calculate estimated number of lines based on viewport width
            const estimatedLines = Math.ceil(textLength / charsPerLine)

            // Use actual line height from settings
            let linkHeight = Math.max(effectiveLineHeight * 2, estimatedLines * effectiveLineHeight)

            // Add buffer based on viewport width (narrower = more unpredictable wrapping)
            // No arbitrary caps - just trust the calculation with appropriate buffer
            let bufferMultiplier = 1.0
            if (effectiveWidth < 250) {
                bufferMultiplier = 1.5  // 50% buffer for extremely narrow
            } else if (effectiveWidth < 400) {
                bufferMultiplier = 1.3  // 30% buffer for narrow
            } else if (effectiveWidth < 600) {
                bufferMultiplier = 1.15 // 15% buffer for medium
            } else {
                bufferMultiplier = 1.1  // 10% buffer for wide
            }

            linkHeight = Math.floor(linkHeight * bufferMultiplier)

            // Track max for logging
            if (linkHeight > maxLinkHeight) {
                maxLinkHeight = linkHeight
                maxLinkTextLength = textLength
            }

            totalEstimatedHeight += linkHeight + 10 // +10 for spacing
        })

        items.push({
            id: `group-${groupIndex}`,
            type: 'group-with-links',
            groupIndex: groupIndex,
            groupName: group.groupName,
            targetBookId: group.targetBookId,
            targetLineIndex: group.targetLineIndex,
            links: group.links,
            estimatedHeight: totalEstimatedHeight
        })
    })

    return items
})

// Search state
const searchRef = ref<InstanceType<typeof GenericSearch> | null>(null)
const isSearchOpen = ref(false)
const search = useContentSearch()

// Available filter options for the current line
const availableFilterOptions = ref<Array<{ label: string; value: number }>>([])

// Computed title that shows filter label, but defaults to "קשרים" for "שונות" (OTHER)
const commentaryTitle = computed(() => {
    const option = availableFilterOptions.value.find(opt => opt.value === selectedConnectionTypeId.value)
    if (!option) return 'קשרים'
    // If the label is "שונות" (OTHER), show "קשרים" instead
    if (option.label === 'שונות') return 'קשרים'
    return option.label
})

async function computeAvailableFilterOptions(bookId: number, lineIndex: number) {
    availableFilterOptions.value = []
    if (!props.book) return

    const baseOptions = commentaryService.getAvailableFilterOptions(props.book)
    if (!baseOptions || baseOptions.length === 0) return

    const tabId = tabStore.activeTab?.id?.toString() || ''
    const results: Array<{ label: string; value: number }> = []
    for (const opt of baseOptions) {
        try {
            const groups = await commentaryService.loadCommentaryLinks(bookId, lineIndex, tabId, { connectionTypeId: opt.value })
            if (groups && groups.length > 0) {
                results.push({ label: opt.label, value: opt.value })
            }
        } catch (e) {
            // ignore errors for individual filters
        }
    }

    availableFilterOptions.value = results
}

function openSearch() {
    isSearchOpen.value = true
}

async function findLineWithTarget(startIndex: number, direction: 1 | -1, targetBookId: number | undefined, maxRange = 50): Promise<number | null> {
    if (targetBookId === undefined || targetBookId === null) return null
    if (props.bookId === undefined) return null
    const tabId = tabStore.activeTab?.id?.toString() || ''

    for (let step = 1; step <= maxRange; step++) {
        const candidate = startIndex + step * direction
        if (candidate < 0) break
        try {
            const groups = await commentaryService.loadCommentaryLinks(props.bookId, candidate, tabId, { connectionTypeId: selectedConnectionTypeId.value })
            if (groups && groups.length > 0) {
                const hasTarget = groups.some(g => g.targetBookId === targetBookId)
                if (hasTarget) return candidate
            }
        } catch (e) {
            // ignore and continue
        }
    }

    return null
}

async function previousLine() {
    const idx = props.selectedLineIndex
    if (idx === undefined) return

    const currentTarget = linkGroups.value[currentGroupIndex.value]?.targetBookId

    // If we have a target commentary, try to find the previous line with it
    if (currentTarget !== undefined) {
        const found = await findLineWithTarget(idx, -1, currentTarget, 100)
        if (found !== null) {
            isLoadingFromNavigation.value = true // Set flag before navigation
            pendingNavigationTargetBookId.value = currentTarget // Set target for next load
            emit('update:selectedLineIndex', found)
            emit('navigate-line', found)
            try {
                const activeTab = tabStore.activeTab
                if (activeTab?.bookState) {
                    activeTab.bookState.selectedLineIndex = found
                }
            } catch (e) {
                console.warn('[Commentary] failed to write selectedLineIndex to tabState', e)
            }
            return
        }
    }

    // Fallback: just go to previous line
    isLoadingFromNavigation.value = true // Set flag even for fallback
    const newIndex = Math.max(0, idx - 1)
    emit('update:selectedLineIndex', newIndex)
    emit('navigate-line', newIndex)
    try {
        const activeTab = tabStore.activeTab
        if (activeTab?.bookState) {
            activeTab.bookState.selectedLineIndex = newIndex
        }
    } catch (e) {
        console.warn('[Commentary] failed to write selectedLineIndex to tabState', e)
    }
}

async function nextLine() {
    const idx = props.selectedLineIndex
    if (idx === undefined) return

    const currentTarget = linkGroups.value[currentGroupIndex.value]?.targetBookId

    // If we have a target commentary, try to find the next line with it
    if (currentTarget !== undefined) {
        const found = await findLineWithTarget(idx, 1, currentTarget, 100)
        if (found !== null) {
            isLoadingFromNavigation.value = true // Set flag before navigation
            pendingNavigationTargetBookId.value = currentTarget // Set target for next load
            emit('update:selectedLineIndex', found)
            emit('navigate-line', found)
            try {
                const activeTab = tabStore.activeTab
                if (activeTab?.bookState) {
                    activeTab.bookState.selectedLineIndex = found
                }
            } catch (e) {
                console.warn('[Commentary] failed to write selectedLineIndex to tabState', e)
            }
            return
        }
    }

    // Fallback: just go to next line
    isLoadingFromNavigation.value = true // Set flag even for fallback
    const newIndex = idx + 1
    emit('update:selectedLineIndex', newIndex)
    emit('navigate-line', newIndex)
    try {
        const activeTab = tabStore.activeTab
        if (activeTab?.bookState) {
            activeTab.bookState.selectedLineIndex = newIndex
        }
    } catch (e) {
        console.warn('[Commentary] failed to write selectedLineIndex to tabState', e)
    }
}

// Handle keydown for Space key prevention (keep in template handler)
function handleKeyDown(e: KeyboardEvent) {
    // Prevent spacebar from scrolling unless in search input
    if (e.key === ' ' && e.target instanceof HTMLElement && e.target.tagName !== 'INPUT') {
        e.preventDefault()
        return
    }
}

function handleSearch(query: string) {
    const items: Array<{ index: number; content: string }> = []
    virtualCommentaryItems.value.forEach((item, index) => {
        items.push({
            index: index,
            content: item.groupName
        })

        item.links.forEach(link => {
            items.push({
                index: index,
                content: link.html
            })
        })
    })

    // Get current scroll position to find closest match
    let currentItemIndex: number | undefined

    if (commentaryScrollerRef.value?.$el) {
        const scrollerEl = commentaryScrollerRef.value.$el
        const scrollTop = scrollerEl.scrollTop || 0
        // Estimate current item from scroll position
        currentItemIndex = Math.floor(scrollTop / commentaryMinItemSize.value)

        // Clamp to valid range
        if (currentItemIndex < 0) currentItemIndex = 0
        if (currentItemIndex >= virtualCommentaryItems.value.length) {
            currentItemIndex = virtualCommentaryItems.value.length - 1
        }
    }

    // Pass current item index to find closest match
    search.searchInItems(items, query, currentItemIndex)

    // Scroll to the selected match
    if (search.currentMatch.value) {
        scrollToCurrentMatch()
    }
}

function handleSearchNext() {
    search.nextMatch()
    scrollToCurrentMatch()
}

function handleSearchPrevious() {
    search.previousMatch()
    scrollToCurrentMatch()
}

function handleSearchClose() {
    isSearchOpen.value = false
}

function scrollToCurrentMatch() {
    const match = search.currentMatch.value
    if (match && commentaryScrollerRef.value) {
        // Set flag to prevent scroll tracking interference
        isNavigating.value = true

        const scrollerEl = commentaryScrollerRef.value.$el
        if (scrollerEl) {
            // Temporarily disable scroll events and hide overflow to prevent flickering
            scrollerEl.style.overflow = 'hidden'
            scrollerEl.style.pointerEvents = 'none'
        }

        // First call to scrollToItem
        commentaryScrollerRef.value.scrollToItem(match.itemIndex)

        // Wait a bit and call it again (the hack for vue3-virtual-scroller)
        setTimeout(() => {
            if (commentaryScrollerRef.value) {
                commentaryScrollerRef.value.scrollToItem(match.itemIndex)

                // Re-enable scrolling after the second call
                setTimeout(() => {
                    if (scrollerEl) {
                        scrollerEl.style.overflow = ''
                        scrollerEl.style.pointerEvents = ''
                    }

                    // Now find and scroll to the actual mark element
                    setTimeout(() => {
                        const currentMark = scrollerEl?.querySelector('mark.current')
                        if (currentMark) {
                            currentMark.scrollIntoView({ behavior: 'auto', block: 'center' })
                        }

                        // Re-enable scroll tracking after a longer delay to prevent jump-back
                        setTimeout(() => {
                            isNavigating.value = false
                        }, 500)
                    }, 50)
                }, 10)
            }
        }, 50)
    }
}

// Update current mark styling without triggering full re-render
function updateCurrentMarkStyling() {
    const scrollerEl = commentaryScrollerRef.value?.$el
    if (!scrollerEl) return

    const currentMatch = search.currentMatch.value
    if (!currentMatch) return

    // Remove 'current' class from all marks
    const allMarks = scrollerEl.querySelectorAll('mark.current')
    allMarks.forEach((mark: Element) => mark.classList.remove('current'))

    // Find the commentary item element
    const itemEl = scrollerEl.querySelector(`[data-commentary-item-observer]`)
    if (!itemEl) return

    // Find all marks in the visible items and add 'current' to the right one
    const allVisibleMarks = scrollerEl.querySelectorAll('mark')
    let markCounter = 0

    // Count through marks to find the one matching currentMatch
    for (const mark of allVisibleMarks) {
        if (markCounter === currentMatch.itemIndex) {
            mark.classList.add('current')
            break
        }
        markCounter++
    }
}

// Computed property for processed commentary content with diacritics filtering
const processedLinkGroups = computed(() => {
    const activeTab = tabStore.activeTab
    const diacriticsState = activeTab?.bookState?.diacriticsState
    const query = search.searchQuery.value
    const currentMatch = search.currentMatch.value

    return linkGroups.value.map((group, groupIndex) => ({
        ...group,
        links: group.links.map((link, linkIndex) => {
            let html = link.html

            if (diacriticsState && diacriticsState > 0) {
                html = applyDiacriticsFilter(html, diacriticsState)
            }

            if (query) {
                const virtualItemIndex = groupIndex
                const currentOccurrence = currentMatch?.itemIndex === virtualItemIndex ? currentMatch.occurrence : -1
                html = search.highlightMatches(html, query, currentOccurrence)
            }

            return {
                ...link,
                html
            }
        })
    }))
})

function applyDiacriticsFilter(htmlContent: string, state: number): string {
    if (!htmlContent || state === 0) return htmlContent

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

        if (state >= 1) {
            text = text.replace(/[\u0591-\u05AF]/g, '')
        }

        if (state >= 2) {
            text = text.replace(/[\u05B0-\u05BD\u05C1\u05C2\u05C4\u05C5]/g, '')
            text = text.replace(/[?!]/g, '.').replace(/—/g, '')
        }

        textNode.nodeValue = text
    })

    return tempDiv.innerHTML
}

// Load commentary when props change
watch([() => props.bookId, () => props.selectedLineIndex], async ([bookId, lineIndex]) => {
    if (bookId !== undefined && lineIndex !== undefined) {
        const targetBookId = pendingNavigationTargetBookId.value
        pendingNavigationTargetBookId.value = undefined // Clear after use
        await loadCommentaryLinks(bookId, lineIndex, targetBookId)
    }
}, { immediate: true })

// Handle connection type filter change
const handleFilterChange = async (connectionTypeId: number) => {
    selectedConnectionTypeId.value = connectionTypeId

    if (props.bookId !== undefined && props.selectedLineIndex !== undefined) {
        await loadCommentaryLinks(props.bookId, props.selectedLineIndex, undefined)
    }
}

async function loadCommentaryLinks(bookId: number, lineIndex: number, scrollToTargetBookId?: number) {
    isLoading.value = true
    const wasNavigating = isLoadingFromNavigation.value // Capture the flag

    try {
        linkGroups.value = await commentaryService.loadCommentaryLinks(
            bookId,
            lineIndex,
            tabStore.activeTab?.id?.toString() || '',
            { connectionTypeId: selectedConnectionTypeId.value }
        )

        computeAvailableFilterOptions(bookId, lineIndex).catch(() => { })

        // Restore saved position for this filter, or default to 0
        const activeTab = tabStore.activeTab
        const filterKey = selectedConnectionTypeId.value?.toString() || 'all'
        const savedPosition = activeTab?.bookState?.commentaryPositionsByFilter?.[filterKey]

        // If we have a target book ID to scroll to (from navigation), find and scroll to it
        if (scrollToTargetBookId !== undefined) {
            const targetGroupIndex = linkGroups.value.findIndex(g => g.targetBookId === scrollToTargetBookId)
            if (targetGroupIndex >= 0) {
                currentGroupIndex.value = targetGroupIndex
                comboboxSelectedValue.value = targetGroupIndex

                setTimeout(() => {
                    scrollToGroup(targetGroupIndex)
                    // Clear the navigation flag after scrolling completes
                    setTimeout(() => {
                        isLoadingFromNavigation.value = false
                    }, 300)
                }, 150)
            } else {
                // Target not found, use default
                currentGroupIndex.value = 0
                comboboxSelectedValue.value = 0
                isLoadingFromNavigation.value = false
            }
        } else if (wasNavigating) {
            // Navigation button was used but no specific target - keep current selection if possible
            const currentTarget = linkGroups.value[currentGroupIndex.value]?.targetBookId
            if (currentTarget !== undefined) {
                // Current index is still valid, keep it
                comboboxSelectedValue.value = currentGroupIndex.value
                setTimeout(() => {
                    scrollToGroup(currentGroupIndex.value)
                    setTimeout(() => {
                        isLoadingFromNavigation.value = false
                    }, 300)
                }, 150)
            } else {
                // Current index not valid, reset
                currentGroupIndex.value = 0
                comboboxSelectedValue.value = 0
                isLoadingFromNavigation.value = false
            }
        } else if (savedPosition && savedPosition.groupIndex < linkGroups.value.length) {
            // Restore saved position
            currentGroupIndex.value = savedPosition.groupIndex
            comboboxSelectedValue.value = savedPosition.groupIndex

            setTimeout(() => {
                scrollToGroup(savedPosition.groupIndex)
                if (savedPosition.scrollPosition && commentaryScrollerRef.value?.$el) {
                    commentaryScrollerRef.value.$el.scrollTop = savedPosition.scrollPosition
                }
            }, 150)
        } else {
            // Default to first group
            currentGroupIndex.value = 0
            comboboxSelectedValue.value = 0
        }

        setTimeout(() => {
            setupScrollTracking()
            // Force virtual scroller to recalculate all item sizes after content loads
            forceScrollerUpdate()
        }, 200)

    } catch (error) {
        console.error('❌ Failed to load commentary links:', error)
        linkGroups.value = []
        isLoadingFromNavigation.value = false
    } finally {
        isLoading.value = false
    }
}

// Force the virtual scroller to recalculate item sizes
function forceScrollerUpdate() {
    if (!commentaryScrollerRef.value) return

    nextTick(() => {
        // Access the internal scroller and force update
        const scroller = commentaryScrollerRef.value as any
        if (scroller && typeof scroller.forceUpdate === 'function') {
            scroller.forceUpdate()
        }

        // Trigger multiple resize events to ensure recalculation
        setTimeout(() => {
            window.dispatchEvent(new Event('resize'))

            // Force another update after a delay to catch any late-rendering content
            setTimeout(() => {
                if (scroller && typeof scroller.forceUpdate === 'function') {
                    scroller.forceUpdate()
                }
                window.dispatchEvent(new Event('resize'))
            }, 300)
        }, 100)
    })
}

// Handle scroll events to force recalculation when needed
let scrollUpdateDebounce: number | undefined
function handleScrollerScroll() {
    // Debounce to avoid excessive updates
    if (scrollUpdateDebounce) {
        clearTimeout(scrollUpdateDebounce)
    }

    // Use shorter debounce for narrow viewports where height issues are more common
    const debounceTime = containerWidth.value < 250 ? 200 : (containerWidth.value < 400 ? 300 : 500)

    scrollUpdateDebounce = window.setTimeout(() => {
        // Force update periodically during scrolling to fix any height miscalculations
        const scroller = commentaryScrollerRef.value as any
        if (scroller && typeof scroller.forceUpdate === 'function') {
            scroller.forceUpdate()
        }
    }, debounceTime)
}

function handleGroupClick(item: any) {
    if (item.targetBookId !== undefined && item.targetLineIndex !== undefined) {
        const targetBook = categoryTreeStore.allBooks.find(book => book.id === item.targetBookId)
        const targetHasConnections = targetBook ? hasConnections(targetBook) : false

        tabStore.openBookInNewTab(item.groupName, item.targetBookId, targetHasConnections, item.targetLineIndex, true)
    }
}

function handleClose() {
    const activeTab = tabStore.activeTab
    if (activeTab?.bookState) {
        activeTab.bookState.showBottomPane = false
    }
}

// Computed property for current group name
const currentGroupName = computed(() => {
    if (processedLinkGroups.value.length > 0 &&
        currentGroupIndex.value >= 0 &&
        currentGroupIndex.value < processedLinkGroups.value.length) {
        const group = processedLinkGroups.value[currentGroupIndex.value]
        return group ? group.groupName : ''
    }
    return ''
})

// Computed property for combobox options
const groupOptions = computed<ComboboxOption[]>(() => {
    return processedLinkGroups.value.map((group, index) => ({
        label: group.groupName,
        value: index
    }))
})

// Watch combobox changes and update scroll position accordingly
watch(comboboxSelectedValue, (newValue) => {
    if (typeof newValue === 'number') {
        if (newValue !== currentGroupIndex.value) {
            currentGroupIndex.value = newValue
            scrollToGroup(newValue)
        }
    } else if (typeof newValue === 'string') {
        const searchText = newValue.toLowerCase().trim()
        if (searchText) {
            const matchingGroup = processedLinkGroups.value.find(group =>
                group.groupName.toLowerCase().includes(searchText)
            )
            if (matchingGroup) {
                const matchingIndex = processedLinkGroups.value.indexOf(matchingGroup)
                if (matchingIndex !== -1 && matchingIndex !== currentGroupIndex.value) {
                    currentGroupIndex.value = matchingIndex
                    scrollToGroup(matchingIndex)
                }
            }
        }
    }
})

// Watch currentGroupIndex changes to update combobox AND persist to tab state
watch(currentGroupIndex, (newIndex) => {
    if (typeof comboboxSelectedValue.value === 'number' && comboboxSelectedValue.value !== newIndex) {
        comboboxSelectedValue.value = newIndex
    }

    // Persist to tab state for session/filter restoration
    const activeTab = tabStore.activeTab
    if (activeTab?.bookState && props.bookId !== undefined) {
        const filterKey = selectedConnectionTypeId.value?.toString() || 'all'

        if (!activeTab.bookState.commentaryPositionsByFilter) {
            activeTab.bookState.commentaryPositionsByFilter = {}
        }

        const currentGroup = linkGroups.value[newIndex]
        activeTab.bookState.commentaryPositionsByFilter[filterKey] = {
            groupIndex: newIndex,
            targetBookId: currentGroup?.targetBookId,
            scrollPosition: commentaryScrollerRef.value?.$el?.scrollTop || 0
        }
    }
})

// Scroll to specific group (virtualized)
const scrollToGroup = (index: number) => {
    if (!commentaryScrollerRef.value) return

    const groupItemId = `group-${index}`
    const itemIndex = virtualCommentaryItems.value.findIndex(item => item.id === groupItemId)

    if (itemIndex !== -1) {
        isNavigating.value = true

        const scrollerEl = commentaryScrollerRef.value.$el
        if (scrollerEl) {
            scrollerEl.style.overflow = 'hidden'
            scrollerEl.style.pointerEvents = 'none'
        }

        commentaryScrollerRef.value.scrollToItem(itemIndex)

        setTimeout(() => {
            if (commentaryScrollerRef.value) {
                commentaryScrollerRef.value.scrollToItem(itemIndex)

                setTimeout(() => {
                    if (scrollerEl) {
                        scrollerEl.style.overflow = ''
                        scrollerEl.style.pointerEvents = ''
                    }
                    setTimeout(() => {
                        isNavigating.value = false
                    }, 50)
                }, 10)
            }
        }, 50)
    }
}

// Navigation functions
const previousGroup = () => {
    if (currentGroupIndex.value > 0) {
        const newIndex = currentGroupIndex.value - 1
        currentGroupIndex.value = newIndex
        scrollToGroup(newIndex)
    }
}

const nextGroup = () => {
    if (currentGroupIndex.value < processedLinkGroups.value.length - 1) {
        const newIndex = currentGroupIndex.value + 1
        currentGroupIndex.value = newIndex
        scrollToGroup(newIndex)
    }
}

function selectAllInContainer() {
    const scrollerEl = commentaryScrollerRef.value?.$el
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
    console.log('[BookCommentaryView] Copy event detected')

    const selection = window.getSelection()
    if (!selection || selection.rangeCount === 0) {
        console.log('[BookCommentaryView] No selection, skipping')
        return
    }

    // Check if the selection is within our container
    const containerEl = commentaryContentRef.value
    if (!containerEl) {
        console.log('[BookCommentaryView] No container element, skipping')
        return
    }

    const range = selection.getRangeAt(0)
    if (!containerEl.contains(range.commonAncestorContainer)) {
        console.log('[BookCommentaryView] Selection not in container, skipping')
        return
    }

    // Only copy all content as HTML if Ctrl+A was pressed (chained shortcut pattern)
    if (!selectAllWasPressed.value) {
        console.log('[BookCommentaryView] Ctrl+A was not pressed, using default copy behavior')
        return // Let browser handle partial selection copy
    }

    // Reset the flag after use
    selectAllWasPressed.value = false

    console.log('[BookCommentaryView] Ctrl+A -> Ctrl+C detected, copying all source content...')

    // Get all source commentary content as HTML
    let htmlContent = ''
    let textContent = ''

    processedLinkGroups.value.forEach((group) => {
        // Add group header
        htmlContent += `<div style="font-weight: bold;">${group.groupName}</div>\n`
        textContent += `${group.groupName}\n`

        // Add all links in the group
        group.links.forEach((link) => {
            htmlContent += `<div>${link.html}</div>\n`

            // For plain text, strip HTML tags
            const tempDiv = document.createElement('div')
            tempDiv.innerHTML = link.html
            textContent += (tempDiv.textContent || tempDiv.innerText || '') + '\n'
        })

        htmlContent += '\n'
        textContent += '\n'
    })

    // Set clipboard data
    event.clipboardData?.setData('text/html', htmlContent)
    event.clipboardData?.setData('text/plain', textContent)
    event.preventDefault()

    console.log('[BookCommentaryView] ✅ Copied all content to clipboard:', {
        totalGroups: processedLinkGroups.value.length,
        htmlLength: htmlContent.length,
        textLength: textContent.length
    })
}

// Set up copy event listener using useEventListener for automatic cleanup
useEventListener(commentaryContentRef, 'copy', handleCopy)

// Scroll tracking with direct position checking
let scrollTrackingCleanup: (() => void) | null = null

// Register cleanup at component level (not inside async function)
onUnmounted(() => {
    if (scrollTrackingCleanup) {
        scrollTrackingCleanup()
    }
})

function setupScrollTracking() {
    if (!commentaryScrollerRef.value?.$el) return

    if (scrollTrackingCleanup) {
        scrollTrackingCleanup()
    }

    const scrollerEl = commentaryScrollerRef.value.$el
    let scrollTimeout: number | undefined

    const updateCurrentSection = () => {
        if (isNavigating.value || isLoadingFromNavigation.value) return

        const sections = scrollerEl.querySelectorAll('section.commentary-section')
        if (sections.length === 0) return

        const scrollerRect = scrollerEl.getBoundingClientRect()
        const scrollerTop = scrollerRect.top
        const scrollerBottom = scrollerRect.bottom

        let targetSection: number | undefined = undefined

        if (scrollDirection.value === 'up') {
            // Scrolling UP: Find the first section whose top is visible or just above viewport top
            let bestSection: number | undefined = undefined
            let bestDistance = Infinity

            sections.forEach((section: Element) => {
                const rect = section.getBoundingClientRect()
                const sectionIndex = parseInt(section.getAttribute('data-group-index') || '-1')
                if (sectionIndex < 0) return

                // Check if section is visible (any part in viewport)
                const isVisible = rect.bottom > scrollerTop && rect.top < scrollerBottom

                if (isVisible) {
                    // Distance from section top to viewport top (prefer sections at top)
                    const distance = Math.abs(rect.top - scrollerTop)
                    if (distance < bestDistance) {
                        bestDistance = distance
                        bestSection = sectionIndex
                    }
                }
            })

            targetSection = bestSection
        } else if (scrollDirection.value === 'down') {
            // Scrolling DOWN: Find the last section that's visible
            let bestSection: number | undefined = undefined
            let bestTop = -Infinity

            sections.forEach((section: Element) => {
                const rect = section.getBoundingClientRect()
                const sectionIndex = parseInt(section.getAttribute('data-group-index') || '-1')
                if (sectionIndex < 0) return

                // Check if section is visible (any part in viewport)
                const isVisible = rect.bottom > scrollerTop && rect.top < scrollerBottom

                if (isVisible && rect.top > bestTop) {
                    bestTop = rect.top
                    bestSection = sectionIndex
                }
            })

            targetSection = bestSection
        } else {
            // No direction: Find section closest to top of viewport
            let bestSection: number | undefined = undefined
            let bestDistance = Infinity

            sections.forEach((section: Element) => {
                const rect = section.getBoundingClientRect()
                const sectionIndex = parseInt(section.getAttribute('data-group-index') || '-1')
                if (sectionIndex < 0) return

                const isVisible = rect.bottom > scrollerTop && rect.top < scrollerBottom
                if (isVisible) {
                    const distance = Math.abs(rect.top - scrollerTop)
                    if (distance < bestDistance) {
                        bestDistance = distance
                        bestSection = sectionIndex
                    }
                }
            })

            targetSection = bestSection
        }

        if (targetSection !== undefined && targetSection >= 0 && targetSection !== currentGroupIndex.value) {
            currentGroupIndex.value = targetSection
        }
    }

    // Track scroll direction and update current section
    const handleScroll = () => {
        if (isNavigating.value || isLoadingFromNavigation.value) return

        const currentScrollTop = scrollerEl.scrollTop

        // Update scroll direction
        if (currentScrollTop > lastScrollTop.value) {
            scrollDirection.value = 'down'
        } else if (currentScrollTop < lastScrollTop.value) {
            scrollDirection.value = 'up'
        }
        lastScrollTop.value = currentScrollTop

        // Debounce the section detection (short delay for responsiveness)
        clearTimeout(scrollTimeout)
        scrollTimeout = window.setTimeout(() => {
            updateCurrentSection()

            // Also update scroll position in persistence
            const activeTab = tabStore.activeTab
            if (activeTab?.bookState && props.bookId !== undefined) {
                const filterKey = selectedConnectionTypeId.value?.toString() || 'all'
                if (activeTab.bookState.commentaryPositionsByFilter?.[filterKey]) {
                    activeTab.bookState.commentaryPositionsByFilter[filterKey].scrollPosition = currentScrollTop
                }
            }
        }, 16) // ~1 frame at 60fps for smooth updates
    }

    scrollerEl.addEventListener('scroll', handleScroll, { passive: true })

    // Initial update
    setTimeout(() => {
        updateCurrentSection()
    }, 100)

    scrollTrackingCleanup = () => {
        scrollerEl.removeEventListener('scroll', handleScroll)
        clearTimeout(scrollTimeout)
    }
}

// Watch for linkGroups changes to refresh scroll tracking
watch(linkGroups, (newGroups) => {
    if (currentGroupIndex.value >= newGroups.length) {
        currentGroupIndex.value = 0
    }

    nextTick(() => {
        setupScrollTracking()
        // Force scroller to recalculate when content changes
        forceScrollerUpdate()
    })
}, { flush: 'post' })

// Watch for significant changes in virtual items and force recalculation
watch(() => virtualCommentaryItems.value.length, () => {
    nextTick(() => {
        forceScrollerUpdate()
    })
})

// Watch for font size and line height changes and force recalculation
watch([() => settingsStore.fontSize, () => settingsStore.linePadding], () => {
    nextTick(() => {
        forceScrollerUpdate()
        // Force multiple updates to ensure proper recalculation
        setTimeout(() => {
            forceScrollerUpdate()
        }, 100)
    })
})
</script>

<style scoped>
.commentary-header {
    justify-content: space-between;
    padding: 0 15px 5px 5px;
}

.commentary-title {
    align-self: center;
}

.commentary-navigation {
    gap: 5px;
}

.nav-btn {
    min-width: 28px;
    min-height: 28px;
    width: 28px;
    height: 28px;
    background: transparent;
    border: none;
    border-radius: 3px;
    color: var(--text-primary);
    flex-shrink: 0;
    padding: 0;
}

.nav-btn:hover:not(:disabled) {
    background: var(--hover-bg);
}

.nav-btn:disabled {
    opacity: 0.3;
}

.commentary-content-container {
    flex: 1 1 0;
    min-height: 0;
    display: flex;
    flex-direction: column;
    overflow: hidden;
}

.commentary-scroller {
    flex: 1 1 0;
    min-height: 0;
    padding: 16px;
    direction: rtl;
    font-size: var(--font-size, 100%);
}

.commentary-loading {
    gap: 12px;
    direction: rtl;
}

.loading-spinner {
    font-size: 24px;
    color: var(--accent-color);
    animation: spin 1s linear infinite;
}

@keyframes spin {
    from {
        transform: rotate(0deg);
    }

    to {
        transform: rotate(360deg);
    }
}

.commentary-placeholder {
    gap: 16px;
    opacity: 0.6;
}

.placeholder-text {
    font-size: 1em;
    font-family: 'Segoe UI Variable', 'Segoe UI', system-ui, sans-serif;
    direction: rtl;
}

.group-header {
    font-size: 1.125em;
    color: var(--text-primary);
    margin: 0 0 12px 0;
    padding: 8px 0;
    direction: rtl;
    font-family: var(--header-font);
    border-bottom: 2px solid var(--border-color);
}

.group-header.c-pointer:hover {
    color: var(--accent-color);
}

.link-item {
    color: var(--text-primary);
    font-family: var(--text-font);
    line-height: var(--line-height, 1.6);
    direction: rtl;
    padding-block-start: 0.15em;
    padding-block-end: 0.15em;
    margin: 0;
    display: block;
}

.commentary-section {
    padding-bottom: 24px;
    position: relative;
    width: 100%;
    box-sizing: border-box;
}

/* Ensure DynamicScrollerItem doesn't have positioning issues */
.commentary-scroller :deep(.vue-recycle-scroller__item-wrapper) {
    position: relative;
}

.commentary-scroller :deep(.vue-recycle-scroller__item-view) {
    width: 100%;
}
</style>