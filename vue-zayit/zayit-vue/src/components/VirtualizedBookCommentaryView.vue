<template>
    <div class="flex-column height-fill">
        <div class="flex-between bar commentary-header"
             style="position: relative;">
            <GenericSearch ref="searchRef"
                           :is-open="isSearchOpen"
                           top-offset="calc(100% + 8px)"
                           @close="isSearchOpen = false"
                           @search-query-change="handleSearchQueryChange"
                           @navigate-to-match="handleNavigateToMatch" />
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

                <Combobox v-model="currentGroupIndex"
                          :options="groupOptions"
                          :placeholder="currentGroupName"
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

        <div class="overflow-y flex-110 selectable commentary-content"
             :style="commentaryStyles"
             ref="commentaryContentRef"
             tabindex="0"
             @scroll.passive="handleCommentaryScroll"
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

            <div v-else
                 class="flex-column commentary-links">
                <!-- Temporarily disable virtualization: render all groups to ensure accurate measurements -->
                <div v-for="index in processedLinkGroups.length > 0 ? Array.from({ length: processedLinkGroups.length }, (_, i) => i) : []"
                     :key="index"
                     class="flex-column commentary-group"
                     :ref="el => setGroupRef(el, index)">
                    <div class="bold group-header"
                         :class="{ 'c-pointer': processedLinkGroups[index]?.targetBookId !== undefined }"
                         @click="() => { const g = processedLinkGroups[index]; if (g) handleGroupClick(g) }">
                        {{ processedLinkGroups[index]?.groupName }}
                    </div>
                    <div v-for="(link, linkIndex) in processedLinkGroups[index]?.links || []"
                         :key="linkIndex"
                         class="selectable line-1.6 justify link-item"
                         v-html="link.html"></div>
                </div>
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted, watch, nextTick, type ComponentPublicInstance } from 'vue'
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
    // Watch for theme changes
    const observer = new MutationObserver(updateDarkMode)
    observer.observe(document.documentElement, {
        attributes: true,
        attributeFilter: ['class']
    })

    // Cleanup observer on unmount
    onUnmounted(() => observer.disconnect())
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

        // Check if this is the first time loading (no filter has been explicitly set)
        const hasExplicitFilter = activeTab.bookState.hasOwnProperty('commentaryFilterConnectionTypeId')

        // If no explicit filter has been set and we have a book, use the default filter
        if (!hasExplicitFilter && props.book) {
            const defaultFilter = commentaryService.getDefaultFilter(props.book)
            if (defaultFilter !== undefined) {
                // Set the default filter in the tab state
                activeTab.bookState.commentaryFilterConnectionTypeId = defaultFilter
                return defaultFilter
            }
        }

        // Return the saved value (including undefined for "הצג הכל")
        return saved
    },
    set: (value: number | undefined) => {
        const activeTab = tabStore.activeTab
        if (activeTab?.bookState) {
            activeTab.bookState.commentaryFilterConnectionTypeId = value
        }
    }
})

// Internal state
const currentGroupIndex = ref(0)
const commentaryContentRef = ref<HTMLElement | null>(null)
const suppressNextSave = ref(false)
const suppressScrollUpdate = ref(false) // Prevent scroll updates during programmatic scrolling
const groupRefs = ref<Map<number, HTMLElement>>(new Map())
// Timeout handle for a scheduled delayed save and a token to detect stale saves
const scrollSaveTimeout = ref<ReturnType<typeof setTimeout> | null>(null)
const pendingSaveToken = ref(0)

// Search state
const searchRef = ref<InstanceType<typeof GenericSearch> | null>(null)
const isSearchOpen = ref(false)
const search = useContentSearch()

// Available filter options for the current line (only filters that have entries)
const availableFilterOptions = ref<Array<{ label: string; value: number }>>([])

// Virtualization state
const ESTIMATED_GROUP_HEIGHT = 120
const OVERSCAN = 4
const scrollTop = ref(0)
const groupHeights = ref<Record<number, number>>({})

import { cumulativeOffsetsFromHeights, findIndexForScroll } from '../utils/virtualizationHelpers'

const cumulativeOffsets = computed(() => cumulativeOffsetsFromHeights(groupHeights.value, linkGroups.value.length, ESTIMATED_GROUP_HEIGHT))

const totalHeight = computed(() => {
    const n = linkGroups.value.length
    if (n === 0) return 0
    const offsets = cumulativeOffsets.value
    const lastIndex = n - 1
    const lastOffset = offsets[lastIndex] ?? 0
    const lastHeight = groupHeights.value[lastIndex] ?? ESTIMATED_GROUP_HEIGHT
    return lastOffset + lastHeight
})

const startIndex = computed(() => Math.max(0, findIndexForScroll(cumulativeOffsets.value, scrollTop.value) - OVERSCAN))
const endIndex = computed(() => {
    const n = linkGroups.value.length
    if (n === 0) return -1
    const ch = commentaryContentRef.value?.clientHeight || 0
    let end = findIndexForScroll(cumulativeOffsets.value, scrollTop.value + ch + OVERSCAN * ESTIMATED_GROUP_HEIGHT)
    end = Math.min(n - 1, end + OVERSCAN)
    return end
})

const visibleIndexes = computed(() => {
    const arr: number[] = []
    if (endIndex.value < startIndex.value) return arr
    for (let i = startIndex.value; i <= endIndex.value; i++) arr.push(i)
    return arr
})

const topPadding = computed(() => cumulativeOffsets.value[startIndex.value] || 0)

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

// Helper: scan forward/backward up to `maxRange` lines to find a line
// that has a commentary group with the same `targetBookId`.
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

// Navigate to previous line; if possible, skip to the nearest line within
// `maxRange` that contains the same commentator (targetBookId). Otherwise
// fallback to the immediate previous line.
async function previousLine() {
    const idx = props.selectedLineIndex
    if (idx === undefined) return

    const currentTarget = linkGroups.value[currentGroupIndex.value]?.targetBookId
    const found = await findLineWithTarget(idx, -1, currentTarget, 50)
    const newIndex = found !== null ? found : Math.max(0, idx - 1)

    emit('update:selectedLineIndex', newIndex)
    emit('navigate-line', newIndex)
    // Fallback: write directly to tab state so BookLineViewer sees the change
    try {
        const activeTab = tabStore.activeTab
        if (activeTab?.bookState) {
            activeTab.bookState.selectedLineIndex = newIndex
        }
    } catch (e) {
        console.warn('[Commentary] failed to write selectedLineIndex to tabState', e)
    }
}

// Navigate to next line; similar to `previousLine` but scanning forward.
async function nextLine() {
    const idx = props.selectedLineIndex
    if (idx === undefined) return

    const currentTarget = linkGroups.value[currentGroupIndex.value]?.targetBookId
    const found = await findLineWithTarget(idx, 1, currentTarget, 50)
    const newIndex = found !== null ? found : idx + 1

    emit('update:selectedLineIndex', newIndex)
    emit('navigate-line', newIndex)
    // Fallback: write directly to tab state so BookLineViewer sees the change
    try {
        const activeTab = tabStore.activeTab
        if (activeTab?.bookState) {
            activeTab.bookState.selectedLineIndex = newIndex
        }
    } catch (e) {
        console.warn('[Commentary] failed to write selectedLineIndex to tabState', e)
    }
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
}

function handleSearchQueryChange(query: string) {
    // Build searchable items from all commentary links
    const items: Array<{ index: number; content: string }> = []
    linkGroups.value.forEach((group, groupIndex) => {
        group.links.forEach((link, linkIndex) => {
            items.push({
                index: groupIndex * 1000 + linkIndex, // Unique index
                content: link.html
            })
        })
    })

    search.searchInItems(items, query)
    searchRef.value?.setMatches(search.totalMatches.value)
}

function handleNavigateToMatch(matchIndex: number) {
    search.navigateToMatch(matchIndex)
    const match = search.currentMatch.value
    if (match) {
        // Calculate group index from combined index
        const groupIndex = Math.floor(match.itemIndex / 1000)

        // Navigate to the group
        currentGroupIndex.value = groupIndex
        scrollToGroup(groupIndex)

        // Wait for render, then fine-tune scroll position to center the mark
        setTimeout(() => {
            const currentMark = document.querySelector('.commentary-content mark.current')
            if (currentMark && commentaryContentRef.value) {
                const markRect = currentMark.getBoundingClientRect()
                const contentRect = commentaryContentRef.value.getBoundingClientRect()

                // Account for search bar height (approximately 60px)
                const searchBarOffset = 60
                const effectiveTop = contentRect.top + searchBarOffset

                // Check if mark is visible below search bar
                const isVisible = markRect.top >= effectiveTop &&
                    markRect.bottom <= contentRect.bottom

                // If not visible, adjust scroll to center it
                if (!isVisible) {
                    const offset = markRect.top - contentRect.top - (contentRect.height / 2) + (markRect.height / 2)
                    commentaryContentRef.value.scrollTop += offset
                }
            }
        }, 50)
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

            // Apply diacritics filtering
            if (diacriticsState && diacriticsState > 0) {
                html = applyDiacriticsFilter(html, diacriticsState)
            }

            // Apply search highlighting
            if (query) {
                const itemIndex = groupIndex * 1000 + linkIndex
                const currentOccurrence = currentMatch?.itemIndex === itemIndex ? currentMatch.occurrence : -1
                html = search.highlightMatches(html, query, currentOccurrence)
            }

            return {
                ...link,
                html
            }
        })
    }))
})

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

// Load commentary when props change (line navigation)
watch([() => props.bookId, () => props.selectedLineIndex], async ([bookId, lineIndex]) => {
    if (bookId !== undefined && lineIndex !== undefined) {
        await loadCommentaryLinks(bookId, lineIndex)
    }
}, { immediate: true })

// Handle connection type filter change
const handleFilterChange = async (connectionTypeId: number) => {


    // Save current filter's position (so each filter keeps its own pointer)
    try {
        // Cancel any pending delayed save to avoid it firing after we switch filters
        if (scrollSaveTimeout.value) {
            clearTimeout(scrollSaveTimeout.value)
            scrollSaveTimeout.value = null
        }
        pendingSaveToken.value++
        saveCurrentTargetBookId()
    } catch (e) {
        console.warn('[Commentary] Failed to save current targetBookId before switching filter', e)
    }

    // Apply the new filter and reload commentary for the current line
    selectedConnectionTypeId.value = connectionTypeId

    if (props.bookId !== undefined && props.selectedLineIndex !== undefined) {
        await loadCommentaryLinks(props.bookId, props.selectedLineIndex)
    }
}

// Helper to get current position for debugging
function getCurrentPosition() {
    return {
        groupIndex: currentGroupIndex.value,
        targetBookId: linkGroups.value[currentGroupIndex.value]?.targetBookId,
        scrollPosition: commentaryContentRef.value?.scrollTop || 0,
        groupName: linkGroups.value[currentGroupIndex.value]?.groupName
    }
}

async function loadCommentaryLinks(bookId: number, lineIndex: number) {
    isLoading.value = true
    try {
        // Cancel any pending delayed save since we're reloading commentary for a new line/filter
        if (scrollSaveTimeout.value) {
            clearTimeout(scrollSaveTimeout.value)
            scrollSaveTimeout.value = null
        }
        pendingSaveToken.value++
        // Suppress autosaves while we load and perform programmatic restores
        suppressNextSave.value = true

        // Clear heights when loading new groups
        groupHeights.value = {}
        groupRefs.value.clear()

        // Preserve previously-selected commentator by targetBookId so switching lines
        // keeps the same commentator if possible.
        const prevSelectedTargetBookId = linkGroups.value[currentGroupIndex.value]?.targetBookId

        linkGroups.value = await commentaryService.loadCommentaryLinks(
            bookId,
            lineIndex,
            tabStore.activeTab?.id?.toString() || '',
            { connectionTypeId: selectedConnectionTypeId.value }
        )

        // Update available filter options for this line so filters with no entries are hidden
        computeAvailableFilterOptions(bookId, lineIndex).catch(() => { })

        // Wait for DOM to render groups, then measure them all
        await nextTick()

        // Force render all groups temporarily to measure heights
        const tempHeights: Record<number, number> = {}
        groupRefs.value.forEach((el, index) => {
            const h = el.offsetHeight
            if (h > 0) {
                tempHeights[index] = h
            }
        })
        if (Object.keys(tempHeights).length > 0) {
            groupHeights.value = tempHeights
        }

        // First, try to restore to the previously-selected commentator (if any)
        if (prevSelectedTargetBookId !== undefined && prevSelectedTargetBookId !== null) {
            const matchingIndex = linkGroups.value.findIndex(g => g.targetBookId === prevSelectedTargetBookId)
            if (matchingIndex !== -1) {
                currentGroupIndex.value = matchingIndex
                setTimeout(() => scrollToGroup(matchingIndex), 100)
                return
            }
        }

        // Fallback: try to restore by saved targetBookId from tab state
        const restored = restoreByTargetBookId()
        if (restored) {
            return
        }

        // Default to first group

        currentGroupIndex.value = 0
    } catch (error) {
        console.error('❌ Failed to load commentary links:', error)
        linkGroups.value = []
    } finally {
        isLoading.value = false
        // Allow programmatic restore/scroll to settle before re-enabling saves
        setTimeout(() => {
            suppressNextSave.value = false
            // Invalidate any previously-scheduled delayed saves
            pendingSaveToken.value++
        }, 250)
    }
}

function handleGroupClick(group: CommentaryLinkGroup) {
    if (group.targetBookId !== undefined && group.targetLineIndex !== undefined) {
        // Look up the target book to determine if it has connections
        const targetBook = categoryTreeStore.allBooks.find(book => book.id === group.targetBookId)
        const targetHasConnections = targetBook ? hasConnections(targetBook) : false

        // Use the new method to create a tab directly with book state
        tabStore.openBookInNewTab(group.groupName, group.targetBookId, targetHasConnections, group.targetLineIndex)

    }
}

function handleClose() {
    // Close the bottom pane by updating tab state directly
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

// Computed property for current filter label
const currentFilterLabel = computed(() => {
    const option = availableFilterOptions.value.find(opt => opt.value === selectedConnectionTypeId.value)
    return option?.label || ''
})

// Computed property for title with filter name
const commentaryTitle = computed(() => {
    return currentFilterLabel.value ? `${currentFilterLabel.value}` : 'קשרים'
})

// Computed property for combobox options
const groupOptions = computed<ComboboxOption[]>(() => {
    return processedLinkGroups.value.map((group, index) => ({
        label: group.groupName,
        value: index
    }))
})

// Set group ref for scrolling and measure heights for virtualization
const setGroupRef = (el: Element | ComponentPublicInstance | null, index: number) => {
    if (!el) {
        groupRefs.value.delete(index)
        return
    }
    const elh = el instanceof HTMLElement ? el : (el as any).$el
    if (elh) {
        groupRefs.value.set(index, elh as HTMLElement)
        // Measure height for virtualization (synchronously - don't wait for nextTick)
        const h = (elh as HTMLElement).offsetHeight
        if ((groupHeights.value[index] || 0) !== h) {
            groupHeights.value = { ...groupHeights.value, [index]: h }
        }
    }
}

// Measure all currently visible groups heights to ensure accurate virtualization
const measureVisibleGroups = () => {
    const newHeights = { ...groupHeights.value }
    let changed = false
    groupRefs.value.forEach((el, index) => {
        const h = el.offsetHeight
        if ((newHeights[index] || 0) !== h) {
            newHeights[index] = h
            changed = true
        }
    })
    if (changed) {
        groupHeights.value = newHeights
    }
}

// Scroll to specific group
const scrollToGroup = (index: number) => {
    const groupElement = groupRefs.value.get(index)
    if (groupElement && commentaryContentRef.value) {
        suppressScrollUpdate.value = true
        groupElement.scrollIntoView({ behavior: 'auto', block: 'start' })
        setTimeout(() => {
            suppressScrollUpdate.value = false
        }, 50)
    }
}

// Handle commentary scroll to update dropdown and track virtualization
const handleCommentaryScroll = () => {
    if (!commentaryContentRef.value) return

    // Skip scroll updates during programmatic scrolling to prevent snap-back
    if (suppressScrollUpdate.value) return

    // Track scroll position for virtualization
    scrollTop.value = commentaryContentRef.value.scrollTop

    // Measure newly visible groups during scroll
    measureVisibleGroups()

    if (processedLinkGroups.value.length === 0 || groupRefs.value.size === 0) return

    const containerRect = commentaryContentRef.value.getBoundingClientRect()
    const containerTop = containerRect.top + 50

    let activeIndex = 0
    const headerPositions: Array<{ index: number; top: number }> = []
    groupRefs.value.forEach((groupElement, index) => {
        const headerRect = groupElement.getBoundingClientRect()
        headerPositions.push({ index, top: headerRect.top })
        if (headerRect.top <= containerTop) {
            activeIndex = index
        }
    })

    // Ensure activeIndex is within bounds of processedLinkGroups
    if (activeIndex >= processedLinkGroups.value.length) {
        activeIndex = Math.max(0, processedLinkGroups.value.length - 1)
    }

    if (currentGroupIndex.value !== activeIndex) {
        currentGroupIndex.value = activeIndex
    }

    // Throttle targetBookId saving to avoid excessive saves during scroll.
    // Capture current filter+line to avoid a delayed save overwriting after a quick switch.
    if (scrollSaveTimeout.value) {
        clearTimeout(scrollSaveTimeout.value)
        scrollSaveTimeout.value = null
    }

    // Bump token to invalidate any previously-scheduled saves and capture this token
    pendingSaveToken.value++
    const myToken = pendingSaveToken.value

    const _capturedConnectionTypeId = selectedConnectionTypeId.value
    const _capturedLineIndex = props.selectedLineIndex

    scrollSaveTimeout.value = setTimeout(() => {
        // If token has changed, this save is stale — ignore it
        if (myToken !== pendingSaveToken.value) {
            return
        }
        // Clear the timeout handle now that it's executing
        if (scrollSaveTimeout.value) {
            clearTimeout(scrollSaveTimeout.value)
            scrollSaveTimeout.value = null
        }

        saveCurrentTargetBookId(_capturedConnectionTypeId, _capturedLineIndex)
    }, 100)
}

// Navigation functions
const previousGroup = () => {
    if (currentGroupIndex.value > 0) {
        currentGroupIndex.value--
        scrollToGroup(currentGroupIndex.value)
    }
}

const nextGroup = () => {
    if (currentGroupIndex.value < processedLinkGroups.value.length - 1) {
        currentGroupIndex.value++
        scrollToGroup(currentGroupIndex.value)
    }
}

// Handle selection directly
function selectAllInContainer() {
    if (!commentaryContentRef.value) return

    const selection = window.getSelection()
    if (!selection) return

    const range = document.createRange()
    range.selectNodeContents(commentaryContentRef.value)
    selection.removeAllRanges()
    selection.addRange(range)

    emit('clearOtherSelections')
}





onMounted(() => {
    // Component mounted - no additional setup needed
})

// Cleanup old 'show_all' stored positions and undefined filter values
onMounted(() => {
    const activeTab = tabStore.activeTab
    if (activeTab?.bookState?.commentaryPositionsByFilter) {
        if (activeTab.bookState.commentaryPositionsByFilter['show_all']) {
            delete activeTab.bookState.commentaryPositionsByFilter['show_all']

        }
    }
    // If an explicit filter is stored as undefined, remove it to force default
    if (activeTab?.bookState && Object.prototype.hasOwnProperty.call(activeTab.bookState, 'commentaryFilterConnectionTypeId')) {
        if (activeTab.bookState.commentaryFilterConnectionTypeId === undefined) {
            delete activeTab.bookState.commentaryFilterConnectionTypeId

        }
    }
})

// Save current targetBookId for the current filter+line
function saveCurrentTargetBookId(capturedConnectionTypeId?: number | undefined, capturedLineIndex?: number | undefined) {
    const activeTab = tabStore.activeTab
    if (!activeTab?.bookState || linkGroups.value.length === 0) return
    const effectiveConnectionTypeId = capturedConnectionTypeId !== undefined ? capturedConnectionTypeId : selectedConnectionTypeId.value
    const effectiveLineIndex = capturedLineIndex !== undefined ? capturedLineIndex : props.selectedLineIndex
    const filterKey = getFilterKey(effectiveConnectionTypeId, effectiveLineIndex)

    const currentGroup = linkGroups.value[currentGroupIndex.value]

    // Initialize commentaryPositionsByFilter if it doesn't exist
    if (!activeTab.bookState.commentaryPositionsByFilter) {
        activeTab.bookState.commentaryPositionsByFilter = {}
    }

    // Save detailed position (group index + scroll position + optional targetBookId)
    if (currentGroup) {
        const scrollPosition = commentaryContentRef.value?.scrollTop || 0
        activeTab.bookState.commentaryPositionsByFilter[filterKey] = {
            groupIndex: currentGroupIndex.value,
            targetBookId: currentGroup.targetBookId,
            scrollPosition
        }

    }
}

// Restore position by finding the saved targetBookId
function restoreByTargetBookId(): boolean {
    const activeTab = tabStore.activeTab
    if (!activeTab?.bookState?.commentaryPositionsByFilter) {

        return false
    }

    const filterKey = getFilterKey(selectedConnectionTypeId.value, props.selectedLineIndex)
    const savedData = activeTab.bookState.commentaryPositionsByFilter[filterKey]



    if (!savedData?.targetBookId) {

        return false
    }

    // Find the group with the saved targetBookId
    const matchingIndex = linkGroups.value.findIndex(
        group => group.targetBookId === savedData.targetBookId
    )



    if (matchingIndex !== -1) {
        const matchedGroup = linkGroups.value[matchingIndex]
        if (matchedGroup) {


            suppressNextSave.value = true
            currentGroupIndex.value = matchingIndex

            // Wait for DOM to update before scrolling
            setTimeout(() => {
                scrollToGroup(matchingIndex)
            }, 100)
            return true
        }
    }

    // If targetBookId wasn't found, but we have a saved groupIndex, use it as a fallback
    if (savedData?.groupIndex !== undefined && Number.isInteger(savedData.groupIndex)) {
        const gi = savedData.groupIndex
        if (gi >= 0 && gi < linkGroups.value.length) {

            suppressNextSave.value = true
            currentGroupIndex.value = gi
            setTimeout(() => {
                scrollToGroup(gi)
            }, 100)
            return true
        }
    }


    return false
}

// Get filter key for position storage
function getFilterKey(connectionTypeId: number | undefined, lineIndex?: number | undefined): string {
    const linePart = lineIndex !== undefined && lineIndex !== null ? `line_${lineIndex}` : 'line_none'
    return connectionTypeId !== undefined ? `filter_${connectionTypeId}_${linePart}` : `filter_none_${linePart}`
}

// Note: Group index is now managed in loadCommentaryLinks based on targetBookId matching
// No need to reset to 0 on every linkGroups change

watch(currentGroupIndex, (newIndex) => {
    // Save targetBookId immediately when group changes, unless this change was programmatic during restore

    if (suppressNextSave.value) {
        suppressNextSave.value = false
    } else {
        saveCurrentTargetBookId()
    }
    scrollToGroup(newIndex)
})

// Re-measure visible groups when the visible set changes (virtualization)
watch(visibleIndexes, () => {
    nextTick(() => {
        measureVisibleGroups()
    })
})

watch(commentaryContentRef, (newVal, oldVal) => {
    if (oldVal) {
        oldVal.removeEventListener('scroll', handleCommentaryScroll)
    }
    if (newVal) {
        newVal.addEventListener('scroll', handleCommentaryScroll)
    }
})
</script>

<style scoped>
.commentary-header {
    justify-content: space-between;
    padding: 0 15px 5px 5px;
}

.commentary-navigation {
    gap: 3px;
}

.nav-btn {
    width: 24px;
    height: 24px;
    background: transparent;
    border: 1px solid var(--border-color);
    border-radius: 3px;
    color: var(--text-primary);
    flex-shrink: 0;
    padding: 0;
}

.nav-btn:hover:not(:disabled) {
    background: var(--hover-bg);
    border-color: var(--accent-color);
}

.nav-btn:disabled {
    opacity: 0.3;
}

.commentary-content {
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

.commentary-links {
    gap: 32px;
    padding-bottom: 32px;
    direction: rtl;
}

.commentary-group {
    gap: 8px;
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
    direction: rtl;
    padding-block-start: 0.15em;
    padding-block-end: 0.15em;
    margin: 0;
    display: block;
}

.link-item :deep(h1),
.link-item :deep(h2),
.link-item :deep(h3),
.link-item :deep(h4),
.link-item :deep(h5),
.link-item :deep(h6) {
    font-family: var(--header-font);
    color: var(--text-primary);
    margin: 0;
    text-align: right;
}

.link-item :deep(h1) {
    font-size: 2em;
    padding: 0.8em 0 0.4em 0;
    margin-bottom: 0.5em;
    border-bottom: 1px solid var(--border-color);
}

.link-item :deep(h2) {
    font-size: 1.6em;
    font-weight: 700;
    padding: 0.7em 0 0.3em 0;
    margin-bottom: 0.4em;
}

.link-item :deep(h3) {
    font-size: 1.4em;
    font-weight: 600;
    padding: 0.6em 0 0.2em 0;
    margin-bottom: 0.3em;
}
</style>
