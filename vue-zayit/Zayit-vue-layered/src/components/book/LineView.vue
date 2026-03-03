<template>
    <div v-if="viewerState.totalLines.value > 0"
         class="height-fill"
         style="position: relative;">
        <GenericSearch ref="searchRef"
                       :is-open="isSearchOpen"
                       :current-match-index="currentMatchIndex"
                       :total-matches="totalMatches"
                       top-offset="4px"
                       @close="handleSearchClose"
                       @search="handleSearch"
                       @next="handleSearchNext"
                       @previous="handleSearchPrevious" />

        <!-- Context Menu -->
        <ContextMenu ref="contextMenuRef"
                     :items="contextMenuItems" />

        <!-- Virtualized scroller -->
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
                         @contextmenu="handleContextMenu">

            <template #default="{ item, index, active }">
                <DynamicScrollerItem :item="item"
                                     :active="active"
                                     :size-dependencies="[
                                        item.content,
                                        myTab?.bookState?.showAltToc
                                    ]"
                                     :data-index="index">
                    <Line :content="item.content || '\u00A0'"
                          :line-index="index"
                          :is-selected="selectedLineIndex === index"
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

// IMPORTS
import { ref, watch, nextTick, onMounted, onUnmounted, computed } from 'vue'
import { useFocus, useEventListener } from '@vueuse/core'
import { DynamicScroller, DynamicScrollerItem } from 'vue-virtual-scroller'
import Line from './Line.vue'
import GenericSearch from '@/components/shared/GenericSearch.vue'
import ContextMenu from '@/components/shared/ContextMenu.vue'
import { BookLineViewerService } from '@/data/services/bookLineViewerService'

import { useVirtualizedSearch } from '@/components/shared/useVirtualizedSearch'
import { useVirtualScrollerPosition } from '@/components/shared/useVirtualScrollerPosition'
import { useVirtualScrollerKeyboard } from '@/components/shared/useVirtualScrollerKeyboard'
import { scrollToElement } from '@/components/shared/useScrollToElement'
import { useTabStore } from '@/data/stores/tabStore'
import { useSettingsStore } from '@/data/stores/settingsStore'

// Extracted utilities and composables
import { applyDiacriticsFilter } from '@/utils/hebrewTextProcessing'
import { highlightGlobalSearchWithSnippet } from '@/utils/searchHighlighting'
import { useLineViewContextMenu } from './useLineViewContextMenu'
import { useLineViewSelection } from './useLineViewSelection'
import { useLineViewCenterObserver } from './useLineViewCenterObserver'
import { useLineViewCopy } from './useLineViewCopy'
import { useLineViewVirtualItems } from './useLineViewVirtualItems'
import { useLineViewScroll } from './useLineViewScroll'
import { useLineViewEvents } from './useLineViewEvents'

// STORES
const tabStore = useTabStore()
const settingsStore = useSettingsStore()

// COMPUTED STYLES
// Computed styles that respect dark mode and reading background
const containerStyles = computed(() => {
    const zoom = myTab.value?.bookState?.zoom || 100
    return {
        backgroundColor: 'var(--reading-bg-primary)',
        color: 'var(--reading-text-primary)',
        fontSize: `calc(var(--font-size, 100%) * ${zoom / 100})`
    }
})

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

// REFS & STATE
const myTab = computed(() => tabStore.tabs.find(t => t.id === props.tabId))
const isInitialLoading = ref(false)

const viewerState = new BookLineViewerService()
const scrollerRef = ref<InstanceType<typeof DynamicScroller> | null>(null)
const contextMenuRef = ref<InstanceType<typeof ContextMenu> | null>(null)

// SCROLL COMPOSABLE - Handles scrolling and global search highlighting
const {
    globalSearchHighlightLineIndex,
    globalSearchTerms,
    globalSearchSnippet,
    scrollToLine,
    scrollToLineWithFadeHighlight
} = useLineViewScroll(scrollerRef)

// Track if this component's scroller has focus
const scrollerElRef = computed(() => scrollerRef.value?.$el as HTMLElement | undefined)
const { focused: hasFocus } = useFocus(scrollerElRef)

// CONTEXT MENU - Use extracted composable
const { contextMenuItems } = useLineViewContextMenu()

function handleContextMenu(event: MouseEvent) {
    contextMenuRef.value?.show(event)
}

// SELECTION MANAGEMENT - Use extracted composable
const { selectAllWasPressed } = useLineViewSelection(scrollerElRef)

// COPY HANDLER - Use extracted composable
const { selectAllInContainer } = useLineViewCopy(scrollerElRef, viewerState, myTab, emit)

// EVENT LISTENERS - Keyboard Shortcuts
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

// Handle keydown for Space key prevention (keep in template handler)
function handleKeyDown(e: KeyboardEvent) {
    // Prevent spacebar from scrolling unless in search input
    if (e.key === ' ' && e.target instanceof HTMLElement && e.target.tagName !== 'INPUT') {
        e.preventDefault()
        return
    }
}

// COMPUTED PROPERTIES - Virtual Scroller
// Minimum item size for the scroller - be more conservative to prevent scroll issues
const minItemSize = computed(() => {
    return 40 // Conservative base size for block mode
})

// SEARCH COMPOSABLE SETUP
// Use virtualized search composable (must be before virtualItems)
const searchUI = useVirtualizedSearch({
    scrollerRef,
    itemSelector: '[data-index]',
    itemIndexAttribute: 'data-index',
    minItemSize,
    totalItems: computed(() => viewerState.totalLines.value),
    searchBarOffset: 50,
    onScrollToItem: async (lineIndex: number) => {
        // Prioritize loading lines around the target
        await viewerState.prioritizeLines(lineIndex, 100)
        await nextTick()
    }
})

const { searchQuery, matches, currentMatchIndex, totalMatches, currentMatch, highlightMatches, clear, isSearchOpen: searchOpenState, isNavigating: searchNavigating, handleSearchNext, handleSearchPrevious, openSearch, handleSearchClose: closeSearch } = searchUI

// EVENT HANDLERS - Use extracted composable
const {
    selectedLineIndex,
    isSearchOpen,
    handleLineClick,
    handleSearchClose,
    handleSearch
} = useLineViewEvents(
    myTab,
    computed(() => props.flatTocEntries),
    searchOpenState,
    viewerState,
    emit
)

// COMPUTED PROPERTIES - Virtual Items
// Use extracted composable for virtual items
const { virtualItems } = useLineViewVirtualItems(
    viewerState,
    myTab,
    computed(() => props.altTocByLineIndex),
    searchQuery,
    matches,
    currentMatch,
    highlightMatches,
    globalSearchHighlightLineIndex,
    globalSearchTerms,
    globalSearchSnippet
)

// POSITION COMPOSABLE SETUP
// Self-sufficient position manager with localStorage persistence
// Position ID format: "book-lines-{tabId}-{bookId}"
const positionId = computed(() => {
    const tabId = props.tabId ?? 'none'
    const bookId = myTab.value?.bookState?.bookId ?? 'none'
    return `book-lines-${tabId}-${bookId}`
})

useVirtualScrollerPosition(scrollerRef, positionId, {
    onRestore: async (itemIndex) => {
        await viewerState.prioritizeLines(itemIndex)
    }
})

// Keyboard navigation for virtual scroller
useVirtualScrollerKeyboard(
    scrollerRef,
    computed(() => viewerState.totalLines.value),
    hasFocus
)

// CENTER LINE OBSERVER - Use extracted composable
const { setupCenterLineObserver } = useLineViewCenterObserver(
    scrollerElRef,
    myTab,
    computed(() => props.flatTocEntries),
    emit,
    emit
)

// WATCHERS - Book Loading
// Load book when bookId changes (position restore is automatic via positionId)
watch(() => myTab.value?.bookState?.bookId, async (bookId, oldBookId) => {
    if (bookId && bookId !== oldBookId) {
        const initialLineIndex = myTab.value?.bookState?.initialLineIndex
        const isRestore = oldBookId === undefined && initialLineIndex !== undefined

        await viewerState.loadBook(bookId, isRestore, initialLineIndex)
        await nextTick()

        emit('placeholdersReady')

        // Position is automatically restored by composable when positionId changes

        // Set up center line observer after book loads
        await nextTick()
        setupCenterLineObserver()
    }
}, { immediate: true })


// TOC NAVIGATION
async function handleTocSelection(lineIndex: number) {
    // Prioritize loading lines around the target before scrolling
    await viewerState.handleTocSelection(lineIndex)

    await nextTick()
    await scrollToLine(lineIndex)

    // Position will be automatically saved by composable after scroll settles (300ms debounce)
}

// LIFECYCLE - Cleanup
onUnmounted(() => {
    viewerState.cleanup()
    // Position is automatically saved by composable to localStorage
})

// COMPONENT EXPORTS
defineExpose({
    handleTocSelection,
    // Expose a method so parent can request an explicit scroll to a line.
    async scrollToLineIndex(index?: number | null) {
        const target = index !== undefined && index !== null ? index : selectedLineIndex.value
        if (target === undefined || target === null) return
        await scrollToLine(target)
    },
    // Scroll to line and highlight search terms (permanent highlighting)
    scrollToLineWithFadeHighlight
})
</script>

<style scoped>
.line-viewer {
    padding: 8px 12px;
    font-size: var(--font-size, 100%);
}

.line-viewer.initial-loading {
    visibility: hidden;
}

.scroller {
    height: 100%;
    scroll-padding-top: 70px;
    /* Account for search bar */
}

/* Global search snippet background animation */
.line-viewer :deep(.global-search-snippet-bg.fade-animation) {
    animation: fadeSearchHighlight 3s ease-out forwards;
}

@keyframes fadeSearchHighlight {
    0% {
        background-color: rgba(245, 158, 11, 0.3);
        /* Orange/amber - same as in-book search */
    }

    100% {
        background-color: transparent;
    }
}

:root.dark .line-viewer :deep(.global-search-snippet-bg.fade-animation) {
    animation: fadeSearchHighlightDark 3s ease-out forwards;
}

@keyframes fadeSearchHighlightDark {
    0% {
        background-color: rgba(251, 191, 36, 0.3);
        /* Lighter amber for dark mode - same as in-book search */
    }

    100% {
        background-color: transparent;
    }
}

/* Global search highlighting - foreground color */
.line-viewer :deep(.global-search-highlight) {
    color: var(--accent-color);
}
</style>
