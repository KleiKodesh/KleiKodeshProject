<template>
    <div class="flex-column height-fill">
        <!-- ============================================ -->
        <!-- HEADER BAR -->
        <!-- ============================================ -->
        <div class="flex-between bar commentary-header"
             style="position: relative;">
            <!-- Search Overlay -->
            <GenericSearch ref="searchRef"
                           :is-open="isSearchOpen"
                           :current-match-index="currentMatchIndex"
                           :total-matches="totalMatches"
                           top-offset="calc(100% + 8px)"
                           @close="handleSearchClose"
                           @search="handleSearch"
                           @next="handleSearchNext"
                           @previous="handleSearchPrevious" />

            <!-- Title -->
            <span class="bold smaller-em commentary-title">{{ commentaryTitle }}</span>

            <!-- Navigation Controls -->
            <div class="flex-row flex-center commentary-navigation">
                <!-- Previous/Next Line Buttons -->
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

                <!-- Search Button -->
                <button class="flex-center c-pointer nav-btn"
                        @click="openSearch"
                        title="חיפוש (Ctrl+F)">
                    <Icon icon="fluent:search-28-regular"
                          class="small-icon" />
                </button>

                <!-- Filter Dropdown -->
                <CommentaryConnectionTypeFilter :book="book"
                                                :selected-connection-type-id="selectedConnectionTypeId"
                                                :available-options="availableFilterOptions"
                                                @filter-change="handleFilterChange" />

                <!-- Commentary Selector Combobox -->
                <Combobox v-model="comboboxSelectedValue"
                          :options="groupOptions"
                          placeholder="בחר פרשן..."
                          dir="rtl" />

                <!-- Previous/Next Group Buttons -->
                <button class="flex-center c-pointer nav-btn"
                        @click="previousGroup"
                        :disabled="currentGroupIndex === 0"
                        title="דלג אחורה">
                    <Icon icon="fluent:chevron-up-28-regular"
                          class="small-icon" />
                </button>

                <button class="flex-center c-pointer nav-btn"
                        @click="nextGroup"
                        :disabled="currentGroupIndex === linkGroups.length - 1"
                        title="דלג קדימה">
                    <Icon icon="fluent:chevron-down-28-regular"
                          class="small-icon" />
                </button>
            </div>

            <!-- Close Button -->
            <button class="flex-center c-pointer commentary-close-btn"
                    @click="handleClose"
                    title="סגור פאנל">
                <Icon icon="fluent:dismiss-16-regular"
                      class="small-icon" />
            </button>
        </div>

        <!-- ============================================ -->
        <!-- CONTENT AREA -->
        <!-- ============================================ -->
        <div class="commentary-content-container"
             ref="commentaryContentRef"
             :style="commentaryStyles"
             tabindex="0"
             @keydown="handleKeyDown">

            <!-- Loading State -->
            <div v-if="isLoading"
                 class="flex-column flex-center height-fill text-secondary commentary-loading">
                <LoadingSpinner text="טוען קשרים..." />
            </div>

            <!-- Empty State -->
            <div v-else-if="linkGroups.length === 0"
                 class="flex-column flex-center height-fill text-secondary commentary-placeholder">
                <div class="bold placeholder-text">לא נמצרו קשרים בקטגוריה זו</div>
            </div>

            <!-- Virtual Scroller with Commentary Content -->
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
                                         :size-dependencies="[item.html?.length || 0, containerWidth]"
                                         :data-index="index"
                                         :data-commentary-item-observer="item.id">

                        <!-- Group Header -->
                        <div v-if="item.type === 'group-header'"
                             class="bold group-header selectable"
                             :class="{ 'c-pointer': item.targetBookId !== undefined }"
                             :data-group-index="item.groupIndex"
                             @click="handleGroupClick(item)"
                             v-html="item.groupName">
                        </div>

                        <!-- Individual Commentary Link -->
                        <div v-else-if="item.type === 'link'"
                             class="selectable line-1.6 justify link-item"
                             :data-group-index="item.groupIndex"
                             v-html="item.html">
                        </div>
                    </DynamicScrollerItem>
                </template>
            </DynamicScroller>
        </div>
    </div>
</template>

<script setup lang="ts">
// ============================================
// IMPORTS
// ============================================
import { ref, computed, onMounted, onUnmounted, watch, nextTick } from 'vue'
import { useFocus, useEventListener } from '@vueuse/core'
import { DynamicScroller, DynamicScrollerItem } from 'vue-virtual-scroller'
import Combobox, { type ComboboxOption } from './common/Combobox.vue'
import GenericSearch from './common/GenericSearch.vue'
import CommentaryConnectionTypeFilter from './CommentaryConnectionTypeFilter.vue'
import LoadingSpinner from './common/LoadingSpinner.vue'
import { Icon } from '@iconify/vue'

import { useVirtualizedSearch } from '../composables/useVirtualizedSearch'
import { bookCommentaryService, type CommentaryLinkGroup } from '../services/bookCommentaryService'
import { dbService } from '../services/dbService'
import { useTabStore } from '../stores/tabStore'
import { useSettingsStore } from '../stores/settingsStore'
import { useCategoryTreeStore } from '../stores/categoryTreeStore'
import type { Book } from '../types/Book'
import { hasConnections } from '../types/Book'

// ============================================
// PROPS & EMITS
// ============================================
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

// ============================================
// STORES
// ============================================
const tabStore = useTabStore()
const settingsStore = useSettingsStore()
const categoryTreeStore = useCategoryTreeStore()

// ============================================
// REFS & STATE
// ============================================
// DOM References
const commentaryScrollerRef = ref<InstanceType<typeof DynamicScroller> | null>(null)
const commentaryContentRef = ref<HTMLElement | null>(null)
const searchRef = ref<InstanceType<typeof GenericSearch> | null>(null)

// Core State
const linkGroups = ref<CommentaryLinkGroup[]>([])
const isLoading = ref(false)
const isDarkMode = ref(false)
const containerWidth = ref(0)

// Navigation State
const isNavigating = ref(false) // Prevents scroll tracking during programmatic scrolls
const isLoadingFromNavigation = ref(false) // Flag for next/previous line navigation
const pendingNavigationTargetBookId = ref<number | undefined>(undefined)
const lastScrollTop = ref(0)
const scrollDirection = ref<'up' | 'down' | 'none'>('none')

// Combobox & Scroll Tracking State
// NOTE: Two separate systems:
// 1. currentGroupIndex - Updated by scroll tracking (based on CENTER of viewport)
// 2. Persistence system - Saves/restores based on TOP of viewport
const currentGroupIndex = ref(0)
const comboboxSelectedValue = ref<string | number>(0)

// Selection State
const selectAllWasPressed = ref(false)

// Filter Options
const availableFilterOptions = ref<Array<{ label: string; value: number }>>([])

// ============================================
// COMPUTED PROPERTIES
// ============================================
// Filter state from tab store with smart default
const selectedConnectionTypeId = computed({
    get: () => {
        const activeTab = tabStore.activeTab
        if (!activeTab?.bookState) return undefined

        const saved = activeTab.bookState.commentaryFilterConnectionTypeId
        const hasExplicitFilter = activeTab.bookState.hasOwnProperty('commentaryFilterConnectionTypeId')

        if (!hasExplicitFilter && props.book) {
            const defaultFilter = bookCommentaryService.getDefaultFilter(props.book)
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

// Dynamic styles based on dark mode and settings
const commentaryStyles = computed(() => ({
    backgroundColor: !isDarkMode.value && settingsStore.readingBackgroundColor
        ? settingsStore.readingBackgroundColor
        : 'var(--bg-primary)',
    color: !isDarkMode.value && settingsStore.readingBackgroundColor
        ? 'var(--reading-text-color)'
        : 'var(--text-primary)',
    fontFamily: settingsStore.textFont,
    fontSize: `${settingsStore.fontSize}%`,
    lineHeight: settingsStore.linePadding.toString()
}))

// Title that shows filter label (defaults to "קשרים" for "שונות")
const commentaryTitle = computed(() => {
    const option = availableFilterOptions.value.find(opt => opt.value === selectedConnectionTypeId.value)
    if (!option) return 'קשרים'
    if (option.label === 'שונות') return 'קשרים'
    return option.label
})

// Virtual scroller minimum item size
const commentaryMinItemSize = computed(() => {
    const baseFontSize = 16
    const effectiveFontSize = (baseFontSize * settingsStore.fontSize) / 100
    const effectiveLineHeight = effectiveFontSize * settingsStore.linePadding
    return Math.floor(effectiveLineHeight * 3)
})

// Search State - initialized after commentaryMinItemSize
const searchUI = useVirtualizedSearch({
    scrollerRef: commentaryScrollerRef,
    itemSelector: '[data-index]',
    itemIndexAttribute: 'data-index',
    minItemSize: commentaryMinItemSize,
    totalItems: computed(() => virtualCommentaryItems.value.length),
    searchBarOffset: 50,
    onScrollToItem: scrollToItem
})

const { searchQuery, matches, currentMatchIndex, totalMatches, currentMatch, highlightMatches, isSearchOpen, isNavigating: searchNavigating, handleSearch: handleSearchBase, handleSearchNext, handleSearchPrevious, openSearch, handleSearchClose } = searchUI

// Flatten link groups into individual virtual items
const virtualCommentaryItems = computed(() => {
    const items: Array<{
        id: string
        type: 'group-header' | 'link'
        groupIndex: number
        groupName?: string
        targetBookId?: number
        targetLineIndex?: number
        html?: string
    }> = []

    processedLinkGroups.value.forEach((group, groupIndex) => {
        // Add group header
        items.push({
            id: `group-header-${groupIndex}`,
            type: 'group-header',
            groupIndex: groupIndex,
            groupName: group.groupName,
            targetBookId: group.targetBookId,
            targetLineIndex: group.targetLineIndex
        })

        // Add each link
        group.links.forEach((link, linkIndex) => {
            items.push({
                id: `group-${groupIndex}-link-${linkIndex}`,
                type: 'link',
                groupIndex: groupIndex,
                html: link.html
            })
        })
    })

    return items
})

// Process link groups with diacritics filtering and search highlighting
const processedLinkGroups = computed(() => {
    const activeTab = tabStore.activeTab
    const diacriticsState = activeTab?.bookState?.diacriticsState
    const query = searchQuery.value

    // Build a map of virtual item indices for proper highlighting
    let virtualItemIndex = 0
    const itemIndexMap = new Map<string, number>()

    linkGroups.value.forEach((group, groupIndex) => {
        // Map group header
        itemIndexMap.set(`group-header-${groupIndex}`, virtualItemIndex++)

        // Map each link
        group.links.forEach((link, linkIndex) => {
            itemIndexMap.set(`group-${groupIndex}-link-${linkIndex}`, virtualItemIndex++)
        })
    })

    return linkGroups.value.map((group, groupIndex) => {
        // Apply highlighting to group name if searching
        let processedGroupName = group.groupName
        if (query) {
            const headerItemIndex = itemIndexMap.get(`group-header-${groupIndex}`)
            if (headerItemIndex !== undefined) {
                processedGroupName = highlightMatches(group.groupName, headerItemIndex)
            }
        }

        return {
            ...group,
            groupName: processedGroupName,
            links: group.links.map((link, linkIndex) => {
                let html = link.html

                // Apply diacritics filter
                if (diacriticsState && diacriticsState > 0) {
                    html = applyDiacriticsFilter(html, diacriticsState)
                }

                // Apply search highlighting
                if (query) {
                    const linkItemIndex = itemIndexMap.get(`group-${groupIndex}-link-${linkIndex}`)
                    if (linkItemIndex !== undefined) {
                        html = highlightMatches(html, linkItemIndex)
                    }
                }

                return { ...link, html }
            })
        }
    })
})

// Combobox options from link groups (use original names without highlighting)
const groupOptions = computed<ComboboxOption[]>(() => {
    return linkGroups.value.map((group, index) => ({
        label: group.groupName,
        value: index
    }))
})

// Focus tracking
const { focused: hasFocus } = useFocus(commentaryContentRef)

// ============================================
// LIFECYCLE HOOKS
// ============================================
onMounted(() => {
    // Dark mode detection
    updateDarkMode()
    const darkModeObserver = new MutationObserver(updateDarkMode)
    darkModeObserver.observe(document.documentElement, {
        attributes: true,
        attributeFilter: ['class']
    })

    // Container width tracking for responsive height estimation
    let resizeObserver: ResizeObserver | null = null
    nextTick(() => {
        if (commentaryContentRef.value) {
            containerWidth.value = commentaryContentRef.value.clientWidth

            resizeObserver = new ResizeObserver((entries) => {
                for (const entry of entries) {
                    containerWidth.value = entry.contentRect.width
                    nextTick(() => {
                        window.dispatchEvent(new Event('resize'))
                        forceScrollerUpdate()
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
        darkModeObserver.disconnect()
        if (resizeObserver) {
            resizeObserver.disconnect()
        }
    })
})

// Scroll tracking cleanup
let scrollTrackingCleanup: (() => void) | null = null
onUnmounted(() => {
    if (scrollTrackingCleanup) {
        scrollTrackingCleanup()
    }
})

// ============================================
// WATCHERS
// ============================================
// Load commentary when props or TOC mode changes
watch([() => props.bookId, () => props.selectedLineIndex, () => tabStore.activeTab?.bookState?.selectedTocEntryId],
    async ([bookId, lineIndex]) => {
        if (bookId !== undefined && lineIndex !== undefined) {
            const targetBookId = pendingNavigationTargetBookId.value
            pendingNavigationTargetBookId.value = undefined
            await loadCommentaryLinks(bookId, lineIndex, targetBookId)
        }
    },
    { immediate: true }
)

// Update combobox when currentGroupIndex changes
watch(currentGroupIndex, (newIndex) => {
    if (typeof comboboxSelectedValue.value === 'number' && comboboxSelectedValue.value !== newIndex) {
        comboboxSelectedValue.value = newIndex
    }

    // Persist position to tab state
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

// Handle combobox selection changes
watch(comboboxSelectedValue, (newValue) => {
    if (typeof newValue === 'number') {
        if (newValue !== currentGroupIndex.value) {
            currentGroupIndex.value = newValue
            scrollToGroup(newValue)
        }
    } else if (typeof newValue === 'string') {
        const searchText = newValue.toLowerCase().trim()
        if (searchText) {
            const matchingGroup = linkGroups.value.find(group =>
                group.groupName.toLowerCase().includes(searchText)
            )
            if (matchingGroup) {
                const matchingIndex = linkGroups.value.indexOf(matchingGroup)
                if (matchingIndex !== -1 && matchingIndex !== currentGroupIndex.value) {
                    currentGroupIndex.value = matchingIndex
                    scrollToGroup(matchingIndex)
                }
            }
        }
    }
})

// Refresh scroll tracking when link groups change
watch(linkGroups, (newGroups) => {
    if (currentGroupIndex.value >= newGroups.length) {
        currentGroupIndex.value = 0
    }

    nextTick(() => {
        setupScrollTracking()
        forceScrollerUpdate()
    })
}, { flush: 'post' })

// Force recalculation when virtual items change
watch(() => virtualCommentaryItems.value.length, () => {
    nextTick(() => {
        forceScrollerUpdate()
    })
})

// Force recalculation when font settings change
watch([() => settingsStore.fontSize, () => settingsStore.linePadding], () => {
    nextTick(() => {
        forceScrollerUpdate()
        setTimeout(() => {
            forceScrollerUpdate()
        }, 100)
    })
})

// ============================================
// EVENT LISTENERS
// ============================================
// Keyboard shortcuts (layout-independent using event.code)
useEventListener('keydown', (event: KeyboardEvent) => {
    if (!hasFocus.value) return

    const hasCtrlOrMeta = event.ctrlKey || event.metaKey

    if (hasCtrlOrMeta && event.code === 'KeyF') {
        event.preventDefault()
        isSearchOpen.value = true
        selectAllWasPressed.value = false
    }

    if (hasCtrlOrMeta && event.code === 'KeyA') {
        event.preventDefault()
        selectAllWasPressed.value = true
        selectAllInContainer()
    }

    if (hasCtrlOrMeta && event.code === 'Home') {
        event.preventDefault()
        scrollToFirstLine()
        selectAllWasPressed.value = false
    }

    if (hasCtrlOrMeta && event.code === 'End') {
        event.preventDefault()
        scrollToLastLine()
        selectAllWasPressed.value = false
    }

    // Reset selectAll flag on navigation/typing
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

// Reset selectAll flag on mouse interaction
useEventListener(commentaryContentRef, 'mousedown', () => {
    selectAllWasPressed.value = false
})

// Reset selectAll flag when selection changes
useEventListener(document, 'selectionchange', () => {
    if (!hasFocus.value) return

    const selection = window.getSelection()
    if (!selection || selection.rangeCount === 0 || selection.isCollapsed) {
        selectAllWasPressed.value = false
    }
})

// Copy event handler for full content copying
useEventListener(commentaryContentRef, 'copy', handleCopy)

// ============================================
// CORE FUNCTIONS
// ============================================
/**
 * Update dark mode state based on document class
 */
function updateDarkMode() {
    isDarkMode.value = document.documentElement.classList.contains('dark')
}

/**
 * FILTER CHANGE HANDLER
 * Saves current position before switching filters (PERSISTENCE SYSTEM)
 * Saves the TOP visible item, not the centered one
 */
async function handleFilterChange(connectionTypeId: number) {
    const activeTab = tabStore.activeTab
    if (activeTab?.bookState && props.bookId !== undefined) {
        const currentFilterKey = selectedConnectionTypeId.value?.toString() || 'all'
        if (!activeTab.bookState.commentaryPositionsByFilter) {
            activeTab.bookState.commentaryPositionsByFilter = {}
        }

        // Get the specific item at the TOP of viewport (not center)
        const topItem = getTopVisibleItem()
        if (topItem) {
            const topGroup = linkGroups.value[topItem.groupIndex]

            activeTab.bookState.commentaryPositionsByFilter[currentFilterKey] = {
                groupIndex: topItem.groupIndex,
                targetBookId: topGroup?.targetBookId,
                scrollPosition: commentaryScrollerRef.value?.$el?.scrollTop || 0,
                topVisibleItemId: topItem.itemId
            }
        }
    }

    selectedConnectionTypeId.value = connectionTypeId

    if (props.bookId !== undefined && props.selectedLineIndex !== undefined) {
        await loadCommentaryLinks(props.bookId, props.selectedLineIndex, undefined)
    }
}

/**
 * LOAD COMMENTARY LINKS
 * Main function to load and display commentary for a given line
 */
async function loadCommentaryLinks(bookId: number, lineIndex: number, scrollToTargetBookId?: number) {
    isLoading.value = true
    const wasNavigating = isLoadingFromNavigation.value

    try {
        const activeTab = tabStore.activeTab
        const tocEntryId = activeTab?.bookState?.selectedTocEntryId

        // Check if loading for TOC section (multiple lines) or single line
        if (tocEntryId !== undefined) {
            // Get all line IDs for this TOC entry
            const lineIdResults = await dbService.getLineIdsByTocEntry(bookId, tocEntryId)
            const lineIds = lineIdResults.map(r => r.lineId)

            if (lineIds.length > 0) {
                // Load merged links for all lines
                const connectionTypeId = selectedConnectionTypeId.value
                const linksPromises = lineIds.map(lineId =>
                    dbService.getLinks(lineId, tabStore.activeTab?.id?.toString() || '', bookId, connectionTypeId)
                )
                const allLinksArrays = await Promise.all(linksPromises)
                const allLinks = allLinksArrays.flat()

                // Group links by title
                const grouped = new Map<string, {
                    links: Array<{ text: string; html: string }>,
                    targetBookId?: number,
                    targetLineIndex?: number
                }>()

                allLinks.forEach(link => {
                    const groupName = link.title || 'אחר'
                    if (!grouped.has(groupName)) {
                        grouped.set(groupName, {
                            links: [],
                            targetBookId: link.targetBookId,
                            targetLineIndex: link.lineIndex
                        })
                    }
                    grouped.get(groupName)!.links.push({
                        text: link.content || '',
                        html: link.content || ''
                    })
                })

                linkGroups.value = Array.from(grouped.entries()).map(([groupName, data]) => ({
                    groupName,
                    targetBookId: data.targetBookId,
                    targetLineIndex: data.targetLineIndex,
                    links: data.links
                }))
            } else {
                linkGroups.value = []
            }
        } else {
            // Regular single-line commentary loading
            linkGroups.value = await bookCommentaryService.loadCommentaryLinks(
                bookId,
                lineIndex,
                tabStore.activeTab?.id?.toString() || '',
                { connectionTypeId: selectedConnectionTypeId.value }
            )
        }

        // Compute available filter options
        if (tocEntryId !== undefined) {
            if (props.book) {
                availableFilterOptions.value = bookCommentaryService.getAvailableFilterOptions(props.book)
            }
        } else {
            computeAvailableFilterOptions(bookId, lineIndex).catch(() => { })
        }

        // RESTORATION LOGIC (PERSISTENCE SYSTEM)
        const filterKey = selectedConnectionTypeId.value?.toString() || 'all'
        const savedPosition = activeTab?.bookState?.commentaryPositionsByFilter?.[filterKey]

        // Priority 1: Navigation with target book ID
        if (scrollToTargetBookId !== undefined) {
            const targetGroupIndex = linkGroups.value.findIndex(g => g.targetBookId === scrollToTargetBookId)
            if (targetGroupIndex >= 0) {
                currentGroupIndex.value = targetGroupIndex
                comboboxSelectedValue.value = targetGroupIndex
                setTimeout(() => {
                    scrollToGroup(targetGroupIndex)
                    setTimeout(() => {
                        isLoadingFromNavigation.value = false
                    }, 300)
                }, 150)
            } else {
                isLoadingFromNavigation.value = false
            }
        }
        // Priority 2: Navigation button without specific target
        else if (wasNavigating) {
            const currentTarget = linkGroups.value[currentGroupIndex.value]?.targetBookId
            if (currentTarget !== undefined) {
                // Keep current index, just scroll to it
                comboboxSelectedValue.value = currentGroupIndex.value
                setTimeout(() => {
                    scrollToGroup(currentGroupIndex.value)
                    setTimeout(() => {
                        isLoadingFromNavigation.value = false
                    }, 300)
                }, 150)
            } else {
                isLoadingFromNavigation.value = false
            }
        }
        // Priority 3: Restore saved position from filter/tab/session
        else if (savedPosition) {
            // Find commentary by targetBookId (more reliable across filters)
            let restoredIndex = -1

            if (savedPosition.targetBookId !== undefined) {
                restoredIndex = linkGroups.value.findIndex(g => g.targetBookId === savedPosition.targetBookId)
            }

            // Fallback to groupIndex if targetBookId not found
            if (restoredIndex === -1 && savedPosition.groupIndex < linkGroups.value.length) {
                restoredIndex = savedPosition.groupIndex
            }

            if (restoredIndex >= 0) {
                // Set combobox immediately so it shows the correct commentary
                currentGroupIndex.value = restoredIndex
                comboboxSelectedValue.value = restoredIndex

                // Scroll to specific item that was at TOP of viewport
                setTimeout(() => {
                    if (savedPosition.topVisibleItemId) {
                        const itemIndex = virtualCommentaryItems.value.findIndex(
                            item => item.id === savedPosition.topVisibleItemId
                        )

                        if (itemIndex !== -1) {
                            scrollToSpecificItem(itemIndex)
                        } else {
                            scrollToGroup(restoredIndex)
                        }
                    } else {
                        scrollToGroup(restoredIndex)
                    }
                }, 150)
            }
        }
        // Priority 4: Default commentator or first group
        else {
            let defaultGroupIndex = 0
            if (props.book?.defaultCommentatorBookId) {
                const defaultIndex = linkGroups.value.findIndex(
                    g => g.targetBookId === props.book!.defaultCommentatorBookId
                )
                if (defaultIndex >= 0) {
                    defaultGroupIndex = defaultIndex
                }
            }

            // Set the combobox to the default group
            currentGroupIndex.value = defaultGroupIndex
            comboboxSelectedValue.value = defaultGroupIndex

            if (defaultGroupIndex > 0) {
                setTimeout(() => {
                    scrollToGroup(defaultGroupIndex)
                }, 150)
            }
        }

        setTimeout(() => {
            setupScrollTracking()
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

// ============================================
// NAVIGATION FUNCTIONS
// ============================================
/**
 * Navigate to previous line with same commentary if possible
 */
async function previousLine() {
    const idx = props.selectedLineIndex
    if (idx === undefined) return

    const currentTarget = linkGroups.value[currentGroupIndex.value]?.targetBookId

    if (currentTarget !== undefined) {
        const found = await findLineWithTarget(idx, -1, currentTarget, 100)
        if (found !== null) {
            isLoadingFromNavigation.value = true
            pendingNavigationTargetBookId.value = currentTarget
            emit('update:selectedLineIndex', found)
            emit('navigate-line', found)
            updateTabLineIndex(found)
            return
        }
    }

    // Fallback: just go to previous line
    isLoadingFromNavigation.value = true
    const newIndex = Math.max(0, idx - 1)
    emit('update:selectedLineIndex', newIndex)
    emit('navigate-line', newIndex)
    updateTabLineIndex(newIndex)
}

/**
 * Navigate to next line with same commentary if possible
 */
async function nextLine() {
    const idx = props.selectedLineIndex
    if (idx === undefined) return

    const currentTarget = linkGroups.value[currentGroupIndex.value]?.targetBookId

    if (currentTarget !== undefined) {
        const found = await findLineWithTarget(idx, 1, currentTarget, 100)
        if (found !== null) {
            isLoadingFromNavigation.value = true
            pendingNavigationTargetBookId.value = currentTarget
            emit('update:selectedLineIndex', found)
            emit('navigate-line', found)
            updateTabLineIndex(found)
            return
        }
    }

    // Fallback: just go to next line
    isLoadingFromNavigation.value = true
    const newIndex = idx + 1
    emit('update:selectedLineIndex', newIndex)
    emit('navigate-line', newIndex)
    updateTabLineIndex(newIndex)
}

/**
 * Navigate to previous commentary group
 */
function previousGroup() {
    if (currentGroupIndex.value > 0) {
        const newIndex = currentGroupIndex.value - 1
        currentGroupIndex.value = newIndex
        scrollToGroup(newIndex)
    }
}

/**
 * Navigate to next commentary group
 */
function nextGroup() {
    if (currentGroupIndex.value < processedLinkGroups.value.length - 1) {
        const newIndex = currentGroupIndex.value + 1
        currentGroupIndex.value = newIndex
        scrollToGroup(newIndex)
    }
}

/**
 * Helper: Update tab line index
 */
function updateTabLineIndex(lineIndex: number) {
    try {
        const activeTab = tabStore.activeTab
        if (activeTab?.bookState) {
            activeTab.bookState.selectedLineIndex = lineIndex
        }
    } catch (e) {
        console.warn('[Commentary] Failed to update selectedLineIndex', e)
    }
}

/**
 * Helper: Find line with target commentary
 */
async function findLineWithTarget(
    startIndex: number,
    direction: 1 | -1,
    targetBookId: number | undefined,
    maxRange = 50
): Promise<number | null> {
    if (targetBookId === undefined || targetBookId === null) return null
    if (props.bookId === undefined) return null

    const tabId = tabStore.activeTab?.id?.toString() || ''

    for (let step = 1; step <= maxRange; step++) {
        const candidate = startIndex + step * direction
        if (candidate < 0) break

        try {
            const groups = await bookCommentaryService.loadCommentaryLinks(
                props.bookId,
                candidate,
                tabId,
                { connectionTypeId: selectedConnectionTypeId.value }
            )
            if (groups && groups.length > 0) {
                const hasTarget = groups.some(g => g.targetBookId === targetBookId)
                if (hasTarget) return candidate
            }
        } catch (e) {
            // Continue searching
        }
    }

    return null
}

// ============================================
// SCROLL FUNCTIONS
// ============================================
/**
 * Scroll to a specific commentary group header
 */
function scrollToGroup(index: number) {
    if (!commentaryScrollerRef.value) return

    const groupHeaderId = `group-header-${index}`
    const itemIndex = virtualCommentaryItems.value.findIndex(item => item.id === groupHeaderId)

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
                    }, 300) // Longer delay for scroll tracking to settle
                }, 10)
            }
        }, 50)
    }
}

/**
 * Scroll to a specific virtual item by index
 */
function scrollToSpecificItem(itemIndex: number) {
    if (!commentaryScrollerRef.value) return
    if (itemIndex < 0 || itemIndex >= virtualCommentaryItems.value.length) return

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
                }, 300) // Longer delay for scroll tracking to settle
            }, 10)
        }
    }, 50)
}

/**
 * Scroll to first line (Ctrl+Home)
 */
function scrollToFirstLine() {
    if (virtualCommentaryItems.value.length === 0) return
    currentGroupIndex.value = 0
    scrollToGroup(0)
}

/**
 * Scroll to last line (Ctrl+End)
 */
function scrollToLastLine() {
    if (virtualCommentaryItems.value.length === 0) return
    const lastIndex = virtualCommentaryItems.value.length - 1
    currentGroupIndex.value = lastIndex

    if (!commentaryScrollerRef.value) return

    const groupItemId = `group-${lastIndex}`
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
                        scrollerEl.scrollTop = scrollerEl.scrollHeight
                        scrollerEl.style.overflow = ''
                        scrollerEl.style.pointerEvents = ''
                    }
                    setTimeout(() => {
                        isNavigating.value = false
                    }, 300) // Longer delay for scroll tracking to settle
                }, 10)
            }
        }, 50)
    }
}

/**
 * Force virtual scroller to recalculate item sizes
 */
function forceScrollerUpdate() {
    if (!commentaryScrollerRef.value) return

    nextTick(() => {
        const scroller = commentaryScrollerRef.value as any
        if (scroller && typeof scroller.forceUpdate === 'function') {
            scroller.forceUpdate()
        }

        setTimeout(() => {
            window.dispatchEvent(new Event('resize'))

            setTimeout(() => {
                if (scroller && typeof scroller.forceUpdate === 'function') {
                    scroller.forceUpdate()
                }
                window.dispatchEvent(new Event('resize'))
            }, 300)
        }, 100)
    })
}

/**
 * Handle scroll events (debounced updates)
 */
let scrollUpdateDebounce: number | undefined
function handleScrollerScroll() {
    if (scrollUpdateDebounce) {
        clearTimeout(scrollUpdateDebounce)
    }

    const debounceTime = containerWidth.value < 250 ? 200 : (containerWidth.value < 400 ? 300 : 500)

    scrollUpdateDebounce = window.setTimeout(() => {
        const scroller = commentaryScrollerRef.value as any
        if (scroller && typeof scroller.forceUpdate === 'function') {
            scroller.forceUpdate()
        }
    }, debounceTime)
}

// ============================================
// SCROLL TRACKING SYSTEM
// Tracks which commentary is at CENTER of viewport
// Updates combobox based on centered content
// ============================================
/**
 * Get the top visible item (for PERSISTENCE system)
 * Returns the specific item at the TOP of viewport
 */
function getTopVisibleItem(): { groupIndex: number; itemId: string } | null {
    if (!commentaryScrollerRef.value?.$el) return null

    const scrollerEl = commentaryScrollerRef.value.$el
    const items = scrollerEl.querySelectorAll('[data-commentary-item-observer]')
    if (items.length === 0) return null

    const scrollerRect = scrollerEl.getBoundingClientRect()
    const scrollerTop = scrollerRect.top

    let topItem: { groupIndex: number; itemId: string } | null = null
    let minDistance = Infinity

    items.forEach((item: Element) => {
        const rect = item.getBoundingClientRect()
        const itemId = item.getAttribute('data-commentary-item-observer')
        const groupIndexAttr = item.getAttribute('data-group-index')

        if (!itemId || !groupIndexAttr) return

        const groupIndex = parseInt(groupIndexAttr)
        if (groupIndex < 0) return

        if (rect.bottom > scrollerTop) {
            const distance = Math.abs(rect.top - scrollerTop)
            if (distance < minDistance) {
                minDistance = distance
                topItem = { groupIndex, itemId }
            }
        }
    })

    return topItem
}

/**
 * Get the group index at top (fallback helper)
 */
function getTopVisibleGroupIndex(): number {
    const topItem = getTopVisibleItem()
    return topItem?.groupIndex ?? currentGroupIndex.value
}

/**
 * Setup scroll tracking system
 * Updates currentGroupIndex based on CENTER of viewport
 */
function setupScrollTracking() {
    if (!commentaryScrollerRef.value?.$el) return

    if (scrollTrackingCleanup) {
        scrollTrackingCleanup()
    }

    const scrollerEl = commentaryScrollerRef.value.$el
    let scrollTimeout: number | undefined

    const updateCurrentSection = () => {
        if (isNavigating.value || isLoadingFromNavigation.value) return

        const items = scrollerEl.querySelectorAll('[data-group-index]')
        if (items.length === 0) return

        const scrollerRect = scrollerEl.getBoundingClientRect()
        const scrollerTop = scrollerRect.top
        const scrollerBottom = scrollerRect.bottom
        const scrollerCenter = scrollerTop + (scrollerBottom - scrollerTop) / 2

        // Find group closest to CENTER of viewport
        let targetSection: number | undefined = undefined
        let bestDistance = Infinity

        items.forEach((item: Element) => {
            const rect = item.getBoundingClientRect()
            const sectionIndex = parseInt(item.getAttribute('data-group-index') || '-1')
            if (sectionIndex < 0) return

            const isVisible = rect.bottom > scrollerTop && rect.top < scrollerBottom

            if (isVisible) {
                const itemCenter = rect.top + rect.height / 2
                const distance = Math.abs(itemCenter - scrollerCenter)

                if (distance < bestDistance) {
                    bestDistance = distance
                    targetSection = sectionIndex
                }
            }
        })

        if (targetSection !== undefined && targetSection >= 0 && targetSection !== currentGroupIndex.value) {
            currentGroupIndex.value = targetSection
        }
    }

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

        // Debounced section detection
        clearTimeout(scrollTimeout)
        scrollTimeout = window.setTimeout(() => {
            updateCurrentSection()

            // Update scroll position in persistence
            const activeTab = tabStore.activeTab
            if (activeTab?.bookState && props.bookId !== undefined) {
                const filterKey = selectedConnectionTypeId.value?.toString() || 'all'
                if (activeTab.bookState.commentaryPositionsByFilter?.[filterKey]) {
                    activeTab.bookState.commentaryPositionsByFilter[filterKey].scrollPosition = currentScrollTop
                }
            }
        }, 16) // ~1 frame at 60fps
    }

    scrollerEl.addEventListener('scroll', handleScroll, { passive: true })

    // Only run initial update if we haven't already set a position
    // (e.g., during restoration, navigation, or default commentary loading)
    // This prevents overriding the manually set combobox value
    const hasInitialPosition = currentGroupIndex.value !== 0 || comboboxSelectedValue.value !== 0
    if (!hasInitialPosition) {
        setTimeout(() => {
            updateCurrentSection()
        }, 100)
    }

    scrollTrackingCleanup = () => {
        scrollerEl.removeEventListener('scroll', handleScroll)
        clearTimeout(scrollTimeout)
    }
}

// ============================================
// SEARCH FUNCTIONS
// ============================================
/**
 * Handle search query - wrapper for unified search UI
 */
function handleSearch(query: string) {
    handleSearchBase(
        virtualCommentaryItems.value,
        query,
        (item) => item.type === 'group-header' ? item.groupName : item.html
    )
}

/**
 * Scroll to a specific item by index (callback for search composable)
 * CommentaryView doesn't need to prioritize loading, so this is a no-op
 */
async function scrollToItem(itemIndex: number) {
    // No-op: CommentaryView doesn't need to prioritize loading
    // The composable handles all scrolling logic
    await nextTick()
}

// ============================================
// UTILITY FUNCTIONS
// ============================================
/**
 * Compute available filter options for current line
 */
async function computeAvailableFilterOptions(bookId: number, lineIndex: number) {
    availableFilterOptions.value = []
    if (!props.book) return

    const baseOptions = bookCommentaryService.getAvailableFilterOptions(props.book)
    if (!baseOptions || baseOptions.length === 0) return

    const tabId = tabStore.activeTab?.id?.toString() || ''
    const results: Array<{ label: string; value: number }> = []

    for (const opt of baseOptions) {
        try {
            const groups = await bookCommentaryService.loadCommentaryLinks(
                bookId,
                lineIndex,
                tabId,
                { connectionTypeId: opt.value }
            )
            if (groups && groups.length > 0) {
                results.push({ label: opt.label, value: opt.value })
            }
        } catch (e) {
            // Ignore errors for individual filters
        }
    }

    availableFilterOptions.value = results
}

/**
 * Apply diacritics filter to HTML content
 */
function applyDiacriticsFilter(htmlContent: string, state: number): string {
    if (!htmlContent || state === 0) return htmlContent

    const tempDiv = document.createElement('div')
    tempDiv.innerHTML = htmlContent

    const walker = document.createTreeWalker(tempDiv, NodeFilter.SHOW_TEXT, null)

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

/**
 * Handle group header click (navigate to target book)
 */
function handleGroupClick(item: any) {
    if (item.targetBookId !== undefined && item.targetLineIndex !== undefined) {
        const targetBook = categoryTreeStore.allBooks.find(book => book.id === item.targetBookId)
        const targetHasConnections = targetBook ? hasConnections(targetBook) : false

        tabStore.openBookInNewTab(
            item.groupName,
            item.targetBookId,
            targetHasConnections,
            item.targetLineIndex,
            true
        )
    }
}

/**
 * Handle close button
 */
function handleClose() {
    const activeTab = tabStore.activeTab
    if (activeTab?.bookState) {
        activeTab.bookState.showBottomPane = false
    }
}

/**
 * Handle keydown (prevent spacebar scroll)
 */
function handleKeyDown(e: KeyboardEvent) {
    if (e.key === ' ' && e.target instanceof HTMLElement && e.target.tagName !== 'INPUT') {
        e.preventDefault()
    }
}

/**
 * Select all content in container
 */
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

/**
 * Handle copy event (copy full source content)
 */
function handleCopy(event: ClipboardEvent) {
    const selection = window.getSelection()
    if (!selection || selection.rangeCount === 0) return

    const containerEl = commentaryContentRef.value
    if (!containerEl) return

    const range = selection.getRangeAt(0)
    if (!containerEl.contains(range.commonAncestorContainer)) return

    // Check if full selection
    const isFullSelection = range.startContainer === containerEl ||
        containerEl.contains(range.startContainer) &&
        containerEl.contains(range.endContainer) &&
        range.toString().length > containerEl.textContent!.length * 0.95

    if (!isFullSelection) return // Let browser handle partial selection

    // Get all source commentary content
    let htmlContent = ''
    let textContent = ''

    processedLinkGroups.value.forEach((group) => {
        htmlContent += `<div style="font-weight: bold;">${group.groupName}</div>\n`
        textContent += `${group.groupName}\n`

        group.links.forEach((link) => {
            htmlContent += `<div>${link.html}</div>\n`

            const tempDiv = document.createElement('div')
            tempDiv.innerHTML = link.html
            textContent += (tempDiv.textContent || tempDiv.innerText || '') + '\n'
        })

        htmlContent += '\n'
        textContent += '\n'
    })

    // Wrap in full HTML document
    htmlContent = `<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<style>
body { direction: rtl; font-weight: normal; }
</style>
</head>
<body>
${htmlContent}
</body>
</html>`

    event.clipboardData?.setData('text/html', htmlContent)
    event.clipboardData?.setData('text/plain', textContent)
    event.preventDefault()
}

</script>

<style scoped>
/* ============================================ */
/* HEADER */
/* ============================================ */
.commentary-header {
    justify-content: space-between;
    padding: 0 6px 6px 6px;
    min-height: 34px;
}

.commentary-title {
    align-self: center;
    padding: 0 4px 3px 4px;
}

.commentary-navigation {
    gap: 4px;
}

.nav-btn {
    min-width: 32px;
    min-height: 32px;
    width: 32px;
    height: 32px;
    background: transparent;
    border: none;
    border-radius: 3px;
    color: var(--text-primary);
    flex-shrink: 0;
    padding: 0;
    touch-action: manipulation;
    transition: transform 0.1s ease, background-color 0.1s ease;
}

.nav-btn:hover:not(:disabled) {
    background: var(--hover-bg);
}

.nav-btn:active:not(:disabled) {
    transform: scale(0.95);
    background: var(--active-bg, var(--hover-bg));
}

.nav-btn:disabled {
    opacity: 0.3;
    cursor: not-allowed;
}

.commentary-close-btn {
    min-width: 32px;
    min-height: 32px;
    width: 32px;
    height: 32px;
    background: transparent;
    border: none;
    border-radius: 3px;
    color: var(--text-primary);
    touch-action: manipulation;
    transition: transform 0.1s ease, background-color 0.1s ease;
}

.commentary-close-btn:hover {
    background: var(--hover-bg);
}

.commentary-close-btn:active {
    transform: scale(0.95);
    background: var(--active-bg, var(--hover-bg));
}

/* Touch device specific styles */
@media (hover: none) and (pointer: coarse) {
    .commentary-navigation {
        gap: 6px;
    }

    .commentary-header {
        padding: 2px 8px 4px 8px;
    }

    .nav-btn,
    .commentary-close-btn {
        min-width: 36px;
        min-height: 36px;
        width: 36px;
        height: 36px;
    }
}

/* Icon sizing for touch-friendly buttons */
.nav-btn .small-icon,
.commentary-close-btn .small-icon {
    width: 16px !important;
    height: 16px !important;
}

@media (hover: none) and (pointer: coarse) {

    .nav-btn .small-icon,
    .commentary-close-btn .small-icon {
        width: 18px !important;
        height: 18px !important;
    }
}

/* ============================================ */
/* CONTENT AREA */
/* ============================================ */
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
    scroll-padding-top: 70px;
    /* Account for search bar */
}

/* ============================================ */
/* LOADING & EMPTY STATES */
/* ============================================ */
.commentary-loading {
    gap: 12px;
    direction: rtl;
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

/* ============================================ */
/* COMMENTARY ITEMS */
/* ============================================ */
.group-header {
    font-size: 1.125em;
    color: var(--text-primary);
    margin: 0 0 12px 0;
    padding: 8px 0;
    border-bottom: 1px solid var(--border-color);
}

.group-header.c-pointer:hover {
    color: var(--accent-color);
}

.link-item {
    margin-bottom: 8px;
    padding: 4px 0;
}

/* Apply header font to any headers in commentary content */
.link-item :deep(h1),
.link-item :deep(h2),
.link-item :deep(h3),
.link-item :deep(h4),
.link-item :deep(h5),
.link-item :deep(h6) {
    font-family: var(--header-font) !important;
}

/* Also apply header font to group headers (commentary names) */
.group-header {
    font-family: var(--header-font) !important;
}

/* ============================================ */
/* SEARCH HIGHLIGHTING */
/* ============================================ */
:deep(mark) {
    background-color: rgba(250, 204, 21, 0.5);
    color: inherit;
    padding: 0 2px;
    border-radius: 2px;
}

:deep(mark.current) {
    background-color: rgba(245, 158, 11, 0.8) !important;
    font-weight: bold;
    color: black;
}

:root.dark :deep(mark) {
    background-color: rgba(250, 204, 21, 0.3);
}

:root.dark :deep(mark.current) {
    background-color: rgba(251, 191, 36, 0.8) !important;
    font-weight: bold;
    color: black;
}
</style>
