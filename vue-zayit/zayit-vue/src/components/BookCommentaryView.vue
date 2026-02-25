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
                        :disabled="!canNavigateToPreviousLine || isNavigatingToLine"
                        @click.stop="handleNavigateToPreviousLine"
                        :title="isNavigatingToLine ? 'מחפש...' : 'שורה קודמת'">
                    <Icon icon="fluent:chevron-right-28-regular"
                          class="small-icon" />
                </button>

                <button class="flex-center c-pointer nav-btn"
                        :disabled="!canNavigateToNextLine || isNavigatingToLine"
                        @click.stop="handleNavigateToNextLine"
                        :title="isNavigatingToLine ? 'מחפש...' : 'שורה הבאה'">
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

                <!-- Category Filter -->
                <CommentaryCategoryFilter :available-categories="availableCategories"
                                          :selected-category-id="selectedCategoryId"
                                          @category-change="handleCategoryChange" />

                <!-- Commentary Selector Combobox -->
                <Combobox v-model="comboboxSelectedValue"
                          :options="filteredGroupOptions"
                          placeholder="בחר פרשן..."
                          dir="rtl" />

                <!-- Previous/Next Group Buttons -->
                <button class="flex-center c-pointer nav-btn"
                        :disabled="!canNavigateToPreviousGroup"
                        @click.stop="handleNavigateToPreviousGroup"
                        title="דלג אחורה">
                    <Icon icon="fluent:chevron-up-28-regular"
                          class="small-icon" />
                </button>

                <button class="flex-center c-pointer nav-btn"
                        :disabled="!canNavigateToNextGroup"
                        @click.stop="handleNavigateToNextGroup"
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
                             key-field="id">

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
import CommentaryCategoryFilter from './CommentaryCategoryFilter.vue'
import LoadingSpinner from './common/LoadingSpinner.vue'
import { Icon } from '@iconify/vue'

import { useVirtualizedSearch } from '../composables/useVirtualizedSearch'
import { useVirtualScrollerKeyboard } from '../composables/useVirtualScrollerKeyboard'
import { useVirtualScrollerPosition } from '../composables/useVirtualScrollerPosition'
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
const currentGroupIndex = ref(0)
const comboboxSelectedValue = ref<string | number>(0)
const pendingDefaultGroupIndex = ref<number | null>(null) // Store default group to apply when scroller ready
const isNavigatingToLine = ref(false) // Loading state for line navigation
const skipScrollRestore = ref(false) // Flag to skip scroll position restore during line navigation

// Scroll tracking flags to prevent loops
const isUpdatingFromScroll = ref(false) // Flag: scroll observer is updating combobox
const scrollObserverEnabled = ref(true) // Master switch to disable scroll observer during programmatic scrolls
const isLineNavigationInProgress = ref(false) // MASTER FLAG: Disables ALL watchers during line navigation

// Selection State
const selectAllWasPressed = ref(false)

// Filter Options
const availableFilterOptions = ref<Array<{ label: string; value: number }>>([])

// ============================================
// COMPUTED PROPERTIES
// ============================================
// Can navigate to previous/next line
const canNavigateToPreviousLine = computed(() => {
    return props.selectedLineIndex !== undefined && props.selectedLineIndex > 0
})

const canNavigateToNextLine = computed(() => {
    return props.selectedLineIndex !== undefined && props.bookId !== undefined
    // Note: We don't check max line here as we don't have total lines in commentary view
})

// Can navigate to previous/next group
const canNavigateToPreviousGroup = computed(() => {
    return linkGroups.value.length > 0 && currentGroupIndex.value > 0
})

const canNavigateToNextGroup = computed(() => {
    return linkGroups.value.length > 0 && currentGroupIndex.value < linkGroups.value.length - 1
})

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

// Category filter state from tab store (now using string ID for label-based filtering)
const selectedCategoryId = computed({
    get: () => {
        const activeTab = tabStore.activeTab
        return activeTab?.bookState?.commentaryFilterCategoryId
    },
    set: (value: number | undefined) => {
        const activeTab = tabStore.activeTab
        if (activeTab?.bookState) {
            activeTab.bookState.commentaryFilterCategoryId = value
        }
    }
})

// Dynamic styles based on dark mode and settings
const commentaryStyles = computed(() => {
    const zoom = tabStore.activeTab?.bookState?.zoom || 100
    return {
        backgroundColor: !isDarkMode.value && settingsStore.readingBackgroundColor
            ? settingsStore.readingBackgroundColor
            : 'var(--bg-primary)',
        color: !isDarkMode.value && settingsStore.readingBackgroundColor
            ? 'var(--reading-text-color)'
            : 'var(--text-primary)',
        fontFamily: settingsStore.textFont,
        fontSize: `calc(${settingsStore.fontSize}% * ${zoom / 100})`,
        lineHeight: settingsStore.linePadding.toString()
    }
})

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

// Focus tracking (needed by keyboard composable)
const { focused: hasFocus } = useFocus(commentaryContentRef)

// Keyboard navigation for virtual scroller
useVirtualScrollerKeyboard(
    commentaryScrollerRef,
    computed(() => virtualCommentaryItems.value.length),
    hasFocus
)

// Position ID for scroll persistence (tab + book + filter)
const scrollPositionId = computed(() => {
    const tabId = tabStore.activeTab?.id?.toString() || ''
    const bookId = props.bookId || 0
    const filterId = selectedConnectionTypeId.value ?? 'all'
    const id = `commentary-${tabId}-${bookId}-${filterId}`
    return id
})

// Scroll position persistence (for tab switches, filter changes, sessions)
useVirtualScrollerPosition(
    commentaryScrollerRef,
    scrollPositionId,
    { skipRestore: skipScrollRestore }
)

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
        // Apply category label filter
        if (selectedCategoryId.value !== undefined && group.targetBookId) {
            const label = bookCategoryLabelMap.value.get(group.targetBookId)
            if (label !== selectedCategoryId.value) {
                return // Skip this group
            }
        }

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

// Combobox removed - will be reimplemented
// Combobox options from link groups (use original names without highlighting)
const groupOptions = computed<ComboboxOption[]>(() => {
    return linkGroups.value.map((group, index) => ({
        label: group.groupName,
        value: index
    }))
})

// Get available categories from commentary books based on their category hierarchy
const availableCategories = ref<Array<{ id: string; name: string; categoryIds: number[] }>>([])
const bookCategoryLabelMap = ref<Map<number, string>>(new Map())

// Resolve group label by walking up category tree (mimics Zayit's resolveGroupLabel)
function resolveGroupLabel(book: Book | undefined, categoryTree: typeof categoryTreeStore.categoryTree): string {
    if (!book) return ''
    
    // Build category map for quick lookup
    const categoryMap = new Map<number, Category>()
    function buildMap(cats: typeof categoryTree) {
        cats.forEach(cat => {
            categoryMap.set(cat.id, cat)
            if (cat.children) buildMap(cat.children)
        })
    }
    buildMap(categoryTree)
    
    let currentId: number | undefined = book.categoryId
    while (currentId) {
        const category = categoryMap.get(currentId)
        if (!category) break
        
        const title = category.title
        
        // Prefer high-level "commentaries on ..." buckets
        if (title.includes('על התנ״ך') || 
            title.includes('על התלמוד') || 
            title.includes('על המשנה') || 
            title.includes('על המשניות') ||
            title.includes('על הש״ס') ||
            title.includes('על השס')) {
            return title
        }
        
        // Broad families
        if (title === 'חסידות' || title.includes('חסידות')) {
            return title
        }
        if (title.includes('מילונים')) {
            return title
        }
        if (title === 'ראשונים') {
            return title
        }
        if (title === 'מחברי זמננו') {
            return title
        }
        if (title === 'ביאור חברותא' || title === 'הערות על ביאור חברותא') {
            return 'חברותא'
        }
        
        // Generic "מפרשים" bucket
        if (title === 'מפרשים') {
            const parent = category.parentId ? categoryMap.get(category.parentId) : undefined
            if (parent && parent.title) {
                return `מפרשים על ${parent.title}`
            }
            return title
        }
        
        currentId = category.parentId
    }
    
    // Fallback to base category
    const baseCategory = categoryMap.get(book.categoryId)
    return baseCategory?.title || ''
}

// Load categories when link groups change
watch(linkGroups, () => {
    console.log('🏷️ Loading category groups for commentary books...')
    
    // Get unique book IDs from link groups
    const bookIds = Array.from(new Set(
        linkGroups.value
            .map(group => group.targetBookId)
            .filter((id): id is number => id !== undefined)
    ))

    console.log('📚 Found book IDs:', bookIds)

    if (bookIds.length === 0) {
        availableCategories.value = []
        bookCategoryLabelMap.value = new Map()
        console.log('⚠️ No book IDs found, clearing categories')
        return
    }

    try {
        // Build map of bookId -> resolved group label
        const labelMap = new Map<number, string>()
        const labelToCategoryIds = new Map<string, Set<number>>()
        
        bookIds.forEach(bookId => {
            const book = categoryTreeStore.allBooks.find(b => b.id === bookId)
            if (book) {
                const label = resolveGroupLabel(book, categoryTreeStore.categoryTree)
                if (label) {
                    labelMap.set(bookId, label)
                    
                    // Track which category IDs belong to this label
                    if (!labelToCategoryIds.has(label)) {
                        labelToCategoryIds.set(label, new Set())
                    }
                    labelToCategoryIds.get(label)!.add(book.categoryId)
                }
            }
        })
        
        bookCategoryLabelMap.value = labelMap
        
        // Create unique filter options from resolved labels
        const categories: Array<{ id: string; name: string; categoryIds: number[] }> = []
        labelToCategoryIds.forEach((categoryIds, label) => {
            categories.push({
                id: label, // Use label as ID since it's unique
                name: label,
                categoryIds: Array.from(categoryIds)
            })
        })
        
        availableCategories.value = categories
        
        console.log('✅ Category filter ready with', categories.length, 'groups:', categories.map(c => c.name))
    } catch (error) {
        console.error('❌ Failed to load categories:', error)
        availableCategories.value = []
        bookCategoryLabelMap.value = new Map()
    }
}, { immediate: true })

// Filter group options based on selected category label
const filteredGroupOptions = computed<ComboboxOption[]>(() => {
    if (selectedCategoryId.value === undefined) {
        return groupOptions.value
    }

    // Filter groups by resolved category label
    return groupOptions.value.filter(option => {
        const groupIndex = option.value as number
        const group = linkGroups.value[groupIndex]
        if (!group?.targetBookId) return true // Keep groups without book ID
        
        const label = bookCategoryLabelMap.value.get(group.targetBookId)
        return label === selectedCategoryId.value
    })
})

// ============================================
// SCROLL OBSERVER - Detect center element and update combobox
// ============================================
/**
 * Find the commentary group at the center of the viewport
 * Returns the group index or null if not found
 * Works with ANY element (header or link) since all have data-group-index
 */
function findCenterCommentaryGroup(): number | null {
    if (!commentaryScrollerRef.value) return null

    const scrollerEl = commentaryScrollerRef.value.$el as HTMLElement | undefined
    if (!scrollerEl) return null

    // Get scroller bounds
    const scrollerRect = scrollerEl.getBoundingClientRect()
    const centerY = scrollerRect.top + scrollerRect.height / 2

    // Find all items with group index (both headers and links)
    const items = scrollerEl.querySelectorAll('[data-group-index]')
    if (items.length === 0) return null

    // Find the item closest to center
    let closestItem: Element | null = null
    let closestDistance = Infinity

    items.forEach((item: Element) => {
        const rect = item.getBoundingClientRect()
        const itemCenterY = rect.top + rect.height / 2
        const distance = Math.abs(itemCenterY - centerY)

        if (distance < closestDistance) {
            closestDistance = distance
            closestItem = item
        }
    })

    if (!closestItem) return null

    // Extract group index from ANY element (header or link)
    const groupIndexAttr = (closestItem as Element).getAttribute('data-group-index')
    if (groupIndexAttr !== null) {
        return parseInt(groupIndexAttr, 10)
    }

    return null
}

/**
 * Handle scroll events - update combobox and tab store based on center element
 */
function handleScrollUpdate() {
    // Don't update if scroll observer is disabled OR line navigation in progress
    if (!scrollObserverEnabled.value || isLineNavigationInProgress.value) {
        return
    }

    const centerGroupIndex = findCenterCommentaryGroup()

    if (centerGroupIndex !== null && centerGroupIndex !== currentGroupIndex.value) {
        const centerGroup = linkGroups.value[centerGroupIndex]
        const activeTab = tabStore.activeTab

        if (centerGroup && activeTab?.bookState) {
            isUpdatingFromScroll.value = true

            // Update combobox
            currentGroupIndex.value = centerGroupIndex
            comboboxSelectedValue.value = centerGroupIndex

            // Update tab state (for line navigation only)
            activeTab.bookState.currentCommentaryBookId = centerGroup.targetBookId
            activeTab.bookState.currentCommentaryGroupName = centerGroup.groupName

            // Note: Scroll position is automatically saved by useVirtualScrollerPosition composable

            // Reset flag after update completes
            nextTick(() => {
                setTimeout(() => {
                    isUpdatingFromScroll.value = false
                }, 50)
            })
        }
    }
}

// Debounced scroll handler to avoid excessive updates
let scrollTimeout: ReturnType<typeof setTimeout> | null = null
function onScrollerScroll() {
    if (scrollTimeout) {
        clearTimeout(scrollTimeout)
    }
    scrollTimeout = setTimeout(() => {
        handleScrollUpdate()
    }, 150) // Debounce to wait for scroll to settle
}

/**
 * Setup scroll listener for commentary tracking
 */
function setupScrollObserver() {
    const scrollerEl = commentaryScrollerRef.value?.$el as HTMLElement | undefined
    if (!scrollerEl) {
        return null
    }

    scrollerEl.addEventListener('scroll', onScrollerScroll, { passive: true })

    // Return cleanup function
    return () => {
        scrollerEl.removeEventListener('scroll', onScrollerScroll)
        if (scrollTimeout) {
            clearTimeout(scrollTimeout)
        }
    }
}

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

        // Clean up scroll observer (handled by watcher)
        if (scrollObserverCleanup) {
            scrollObserverCleanup()
        }
    })
})

// Cleanup handled by composables
// ============================================
// WATCHERS
// ============================================
// Watch for scroller becoming available and set up observer
let scrollObserverCleanup: (() => void) | null = null
watch(commentaryScrollerRef, (newScroller) => {
    // Cleanup old observer
    if (scrollObserverCleanup) {
        scrollObserverCleanup()
        scrollObserverCleanup = null
    }

    // Setup new observer when scroller becomes available
    if (newScroller) {
        nextTick(() => {
            scrollObserverCleanup = setupScrollObserver()

            // Apply pending default group index if waiting
            if (pendingDefaultGroupIndex.value !== null) {
                const defaultGroupIndex = pendingDefaultGroupIndex.value
                pendingDefaultGroupIndex.value = null

                // Update current group index
                currentGroupIndex.value = defaultGroupIndex

                // Update combobox value (for display only, don't trigger watcher scroll)
                comboboxSelectedValue.value = defaultGroupIndex

                // Update tab state immediately
                const activeTab = tabStore.activeTab
                if (activeTab?.bookState) {
                    const targetGroup = linkGroups.value[defaultGroupIndex]
                    if (targetGroup) {
                        activeTab.bookState.currentCommentaryBookId = targetGroup.targetBookId
                        activeTab.bookState.currentCommentaryGroupName = targetGroup.groupName
                    }
                }

                // Directly scroll to the group (bypasses watcher)
                scrollToGroup(defaultGroupIndex)
            }
        })
    }
}, { immediate: true })

// Load commentary when props or TOC mode changes
watch([() => props.bookId, () => props.selectedLineIndex, () => tabStore.activeTab?.bookState?.selectedTocEntryId],
    async ([bookId, lineIndex], [oldBookId, oldLineIndex]) => {
        if (bookId !== undefined && lineIndex !== undefined) {
            const isLineNavigation = oldBookId === bookId && oldLineIndex !== undefined && oldLineIndex !== lineIndex
            await loadCommentaryLinks(bookId, lineIndex, isLineNavigation)
        }
    },
    { immediate: true }
)

// Handle combobox selection changes - scroll to selected group
watch(comboboxSelectedValue, (newValue) => {
    // IGNORE ALL UPDATES during line navigation
    if (isLineNavigationInProgress.value) {
        return
    }

    // Ignore updates triggered by scroll observer
    if (isUpdatingFromScroll.value) {
        return
    }

    if (typeof newValue === 'number') {
        if (newValue !== currentGroupIndex.value) {
            currentGroupIndex.value = newValue
            scrollToGroup(newValue)
        }
    } else if (typeof newValue === 'string') {
        // Handle text search in combobox
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

// Watchers for forceScrollerUpdate removed - will be reimplemented if needed

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
 * Position is automatically saved/restored by composable when positionId changes
 */
async function handleFilterChange(connectionTypeId: number) {
    console.log('🔄 Filter change:', selectedConnectionTypeId.value, '→', connectionTypeId)

    // Switch to new filter (positionId will change, triggering auto-restore)
    selectedConnectionTypeId.value = connectionTypeId

    if (props.bookId !== undefined && props.selectedLineIndex !== undefined) {
        await loadCommentaryLinks(props.bookId, props.selectedLineIndex)

        // Give the virtual scroller time to render before auto-restore kicks in
        await nextTick()
        await nextTick()
        console.log('🔄 Filter change complete, scroller should restore position')
    }
}

/**
 * CATEGORY FILTER CHANGE HANDLER
 * Filters commentary groups by resolved category label
 */
function handleCategoryChange(categoryId: number | string | undefined) {
    console.log('🔄 Category filter change:', selectedCategoryId.value, '→', categoryId)
    selectedCategoryId.value = categoryId

    // Reset combobox selection if current selection is filtered out
    if (categoryId !== undefined) {
        const currentIndex = comboboxSelectedValue.value as number
        const isCurrentVisible = filteredGroupOptions.value.some(opt => opt.value === currentIndex)
        
        if (!isCurrentVisible && filteredGroupOptions.value.length > 0) {
            // Select first visible option
            comboboxSelectedValue.value = filteredGroupOptions.value[0].value
        }
    }
}

/**
 * LOAD COMMENTARY LINKS
 */
async function loadCommentaryLinks(bookId: number, lineIndex: number, isLineNavigation = false) {
    // SET MASTER FLAG if this is line navigation - blocks ALL watchers
    if (isLineNavigation) {
        isLineNavigationInProgress.value = true
        skipScrollRestore.value = true // Skip scroll position restore during line navigation
    }

    isLoading.value = true

    // Reset current group index when loading new commentary
    currentGroupIndex.value = -1

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
                    links: data.links,
                    targetBookId: data.targetBookId,
                    targetLineIndex: data.targetLineIndex
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

        // Handle commentary scrolling based on context
        await nextTick()

        if (isLineNavigation) {
            // Line navigation - scroll to current commentary
            await handleLineNavigationCommentary()

            // CLEAR MASTER FLAG after everything is complete
            setTimeout(() => {
                isLineNavigationInProgress.value = false
                skipScrollRestore.value = false
            }, 1000) // Wait 1 second for all scrolling to settle
        } else {
            // First load - scroll to default commentary if no saved position
            await handleFirstLoadDefaultCommentary()
        }

    } catch (error) {
        console.error('❌ Failed to load commentary links:', error)
        linkGroups.value = []

        // Clear flag on error too
        if (isLineNavigation) {
            isLineNavigationInProgress.value = false
            skipScrollRestore.value = false
        }
    } finally {
        isLoading.value = false
    }
}

/**
 * Scroll to a specific commentary book ID
 * Used for both first load (default) and line navigation (current)
 */
async function scrollToCommentaryBookId(targetBookId: number, targetGroupName?: string) {
    // Find the group with this book ID and optionally matching group name
    let groupIndex = -1

    if (targetGroupName) {
        // Try to find exact match by both book ID and group name
        groupIndex = linkGroups.value.findIndex(
            group => group.targetBookId === targetBookId && group.groupName === targetGroupName
        )
    }

    if (groupIndex === -1) {
        // Fall back to just book ID
        groupIndex = linkGroups.value.findIndex(
            group => group.targetBookId === targetBookId
        )
    }

    if (groupIndex === -1) {
        groupIndex = 0
    }

    // Wait for DOM to update
    await nextTick()

    // If scroller not available, store for later
    if (!commentaryScrollerRef.value) {
        pendingDefaultGroupIndex.value = groupIndex
        return
    }

    // Set combobox - watcher handles scroll
    comboboxSelectedValue.value = groupIndex

    // Note: We don't update tab state here - the scroll observer will do it after scroll completes
}

/**
 * Handle first load - scroll to default commentary if no saved position exists
 */
async function handleFirstLoadDefaultCommentary() {
    // Check if we have a saved scroll position (from useVirtualScrollerPosition)
    const positionKey = `vscroller-pos-${scrollPositionId.value}`
    const hasSavedPosition = localStorage.getItem(positionKey) !== null

    if (hasSavedPosition) {
        // Let useVirtualScrollerPosition handle restoration
        return
    }

    // First load - check if we have a current commentary, otherwise use book's default
    const activeTab = tabStore.activeTab
    const currentCommentaryBookId = activeTab?.bookState?.currentCommentaryBookId
    const defaultCommentaryBookId = props.book?.defaultCommentatorBookId

    const targetBookId = currentCommentaryBookId || defaultCommentaryBookId

    if (!targetBookId) {
        return
    }

    await scrollToCommentaryBookId(targetBookId)
}

/**
 * Handle line navigation - scroll to current commentary
 */
async function handleLineNavigationCommentary() {
    const activeTab = tabStore.activeTab
    const currentCommentaryBookId = activeTab?.bookState?.currentCommentaryBookId
    const currentCommentaryGroupName = activeTab?.bookState?.currentCommentaryGroupName
    const defaultCommentaryBookId = props.book?.defaultCommentatorBookId

    // Use current if available, otherwise fall back to default
    const targetBookId = currentCommentaryBookId || defaultCommentaryBookId

    if (!targetBookId) {
        if (linkGroups.value.length > 0) {
            comboboxSelectedValue.value = 0
        }
        return
    }

    await scrollToCommentaryBookId(targetBookId, currentCommentaryGroupName)
}

// ============================================
// NAVIGATION FUNCTIONS
// ============================================
/**
 * Find the next line where current commentary exists
 * Scans forward from startLine until commentary is found or max lines reached
 */
async function findNextLineWithCommentary(startLine: number, maxScanLines = 50): Promise<number | null> {
    if (!props.bookId) return null

    const activeTab = tabStore.activeTab
    const currentCommentaryBookId = activeTab?.bookState?.currentCommentaryBookId
    if (!currentCommentaryBookId) return null

    const tabId = activeTab?.id?.toString() || ''
    const connectionTypeId = selectedConnectionTypeId.value

    // Scan forward up to maxScanLines
    for (let offset = 1; offset <= maxScanLines; offset++) {
        const testLine = startLine + offset

        try {
            // Load commentary for this line
            const testGroups = await bookCommentaryService.loadCommentaryLinks(
                props.bookId,
                testLine,
                tabId,
                { connectionTypeId }
            )

            // Check if current commentary exists in this line
            const hasCommentary = testGroups.some(group => group.targetBookId === currentCommentaryBookId)
            if (hasCommentary) {
                return testLine
            }
        } catch (error) {
            // Line doesn't exist or error loading - stop scanning
            break
        }
    }

    return null
}

/**
 * Find the previous line where current commentary exists
 * Scans backward from startLine until commentary is found or line 0 reached
 */
async function findPreviousLineWithCommentary(startLine: number, maxScanLines = 50): Promise<number | null> {
    if (!props.bookId) return null

    const activeTab = tabStore.activeTab
    const currentCommentaryBookId = activeTab?.bookState?.currentCommentaryBookId
    if (!currentCommentaryBookId) return null

    const tabId = activeTab?.id?.toString() || ''
    const connectionTypeId = selectedConnectionTypeId.value

    // Scan backward up to maxScanLines or until line 0
    for (let offset = 1; offset <= maxScanLines; offset++) {
        const testLine = startLine - offset
        if (testLine < 0) break

        try {
            // Load commentary for this line
            const testGroups = await bookCommentaryService.loadCommentaryLinks(
                props.bookId,
                testLine,
                tabId,
                { connectionTypeId }
            )

            // Check if current commentary exists in this line
            const hasCommentary = testGroups.some(group => group.targetBookId === currentCommentaryBookId)
            if (hasCommentary) {
                return testLine
            }
        } catch (error) {
            // Error loading - continue scanning
            continue
        }
    }

    return null
}

/**
 * Navigate to next line with current commentary
 */
async function handleNavigateToNextLine() {
    if (!canNavigateToNextLine.value || props.selectedLineIndex === undefined || isNavigatingToLine.value) return

    isNavigatingToLine.value = true

    try {
        const nextLine = await findNextLineWithCommentary(props.selectedLineIndex)

        if (nextLine !== null) {
            emit('navigate-line', nextLine)
        }
    } finally {
        isNavigatingToLine.value = false
    }
}

/**
 * Navigate to previous line with current commentary
 */
async function handleNavigateToPreviousLine() {
    if (!canNavigateToPreviousLine.value || props.selectedLineIndex === undefined || isNavigatingToLine.value) return

    isNavigatingToLine.value = true

    try {
        const previousLine = await findPreviousLineWithCommentary(props.selectedLineIndex)

        if (previousLine !== null) {
            emit('navigate-line', previousLine)
        }
    } finally {
        isNavigatingToLine.value = false
    }
}

/**
 * Navigate to previous group (up)
 */
function handleNavigateToPreviousGroup() {
    if (!canNavigateToPreviousGroup.value) return

    const newIndex = currentGroupIndex.value - 1
    comboboxSelectedValue.value = newIndex
}

/**
 * Navigate to next group (down)
 */
function handleNavigateToNextGroup() {
    if (!canNavigateToNextGroup.value) return

    const newIndex = currentGroupIndex.value + 1
    comboboxSelectedValue.value = newIndex
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

// ============================================
// SCROLL FUNCTIONS
// ============================================
// ============================================
// Scroll functions removed - will be reimplemented
/**
 * Scroll to a specific commentary group by index
 * Disables scroll observer during programmatic scroll to prevent interference
 */
async function scrollToGroup(groupIndex: number) {
    if (!commentaryScrollerRef.value) return
    if (groupIndex < 0 || groupIndex >= linkGroups.value.length) return

    // DISABLE scroll observer during programmatic scroll
    scrollObserverEnabled.value = false

    await nextTick()

    // Find the virtual item index for this group header
    const groupHeaderId = `group-header-${groupIndex}`
    const itemIndex = virtualCommentaryItems.value.findIndex(item => item.id === groupHeaderId)

    if (itemIndex === -1) {
        scrollObserverEnabled.value = true
        return
    }

    // Update current group index immediately
    currentGroupIndex.value = groupIndex

    // CRITICAL: Force virtual scroller to update and recalculate sizes
    await nextTick()
    if (commentaryScrollerRef.value && typeof commentaryScrollerRef.value.$forceUpdate === 'function') {
        commentaryScrollerRef.value.$forceUpdate()
        await nextTick()
    }

    // Use virtual scroller to scroll to item (gets it into rendered range)
    commentaryScrollerRef.value.scrollToItem(itemIndex)

    const scrollerEl = commentaryScrollerRef.value.$el as HTMLElement

    // Update tab state immediately (don't wait for scroll observer)
    const targetGroup = linkGroups.value[groupIndex]
    const activeTab = tabStore.activeTab
    if (targetGroup && activeTab?.bookState) {
        activeTab.bookState.currentCommentaryBookId = targetGroup.targetBookId
        activeTab.bookState.currentCommentaryGroupName = targetGroup.groupName
    }

    // Wait for virtual scroller to render, then use scrollIntoView for precise positioning
    await nextTick()

    if (!scrollerEl) return

    // Poll for the element to appear in DOM (virtual scroller renders lazily)
    const maxAttempts = 20
    let attempts = 0
    let targetElement: HTMLElement | null = null

    while (attempts < maxAttempts && !targetElement) {
        await new Promise(resolve => requestAnimationFrame(resolve))
        targetElement = scrollerEl.querySelector(`[data-group-index="${groupIndex}"]`) as HTMLElement
        attempts++
    }

    if (!targetElement) {
        scrollObserverEnabled.value = true
        return
    }

    targetElement.scrollIntoView({ block: 'start', behavior: 'auto' })

    // Re-enable scroll observer after scroll settles
    await nextTick()
    setTimeout(() => {
        scrollObserverEnabled.value = true
    }, 500)
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
