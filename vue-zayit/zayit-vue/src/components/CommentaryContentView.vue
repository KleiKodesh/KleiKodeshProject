<template>
    <div ref="contentRef"
         class="commentary-content"
         :style="commentaryStyles"
         tabindex="0"
         @keydown="handleKeyDown">

        <!-- Search Overlay -->
        <GenericSearch ref="searchRef"
                       :is-open="isSearchOpen"
                       :current-match-index="currentMatchIndex"
                       :total-matches="totalMatches"
                       top-offset="8px"
                       @close="handleSearchClose"
                       @search="handleSearch"
                       @next="handleSearchNext"
                       @previous="handleSearchPrevious" />

        <!-- Loading State -->
        <div v-if="isLoading"
             class="flex-column flex-center height-fill text-secondary commentary-loading">
            <LoadingSpinner text="טוען קשרים..." />
        </div>

        <!-- Empty State -->
        <div v-else-if="displayGroups.length === 0"
             class="flex-column flex-center height-fill text-secondary commentary-placeholder">
            <div class="bold placeholder-text">
                {{ processedLinkGroups.length === 0 ? 'לא נמצאו קשרים' : 'בחר פרשן מהרשימה' }}
            </div>
        </div>

        <!-- Commentary Content -->
        <div v-else
             class="commentary-container">
            <div v-for="(group, groupIndex) in displayGroups"
                 :key="groupIndex">
                <!-- Group Header -->
                <div class="bold group-header selectable"
                     :class="{ 'c-pointer': (group as CommentaryLinkGroup).targetBookId !== undefined }"
                     :data-group-index="groupIndex"
                     @click="handleGroupClick(group as CommentaryLinkGroup)"
                     v-html="(group as CommentaryLinkGroup).groupName">
                </div>

                <!-- Commentary Links -->
                <div v-for="(link, linkIndex) in (group as CommentaryLinkGroup).links"
                     :key="linkIndex"
                     :data-group-index="groupIndex"
                     :data-link-index="linkIndex"
                     class="selectable line-1.6 justify link-item"
                     v-html="link.html">
                </div>
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, watch, nextTick } from 'vue'
import { useFocus, useEventListener } from '@vueuse/core'
import GenericSearch from './common/GenericSearch.vue'
import LoadingSpinner from './common/LoadingSpinner.vue'
import type { CommentaryLinkGroup } from '../services/bookCommentaryService'
import { useTabStore } from '../stores/tabStore'
import { useCategoryTreeStore } from '../stores/categoryTreeStore'
import { hasConnections } from '../types/Book'
import { scrollToElementTop } from '../composables/useScrollToElement'

const props = defineProps<{
    processedLinkGroups: CommentaryLinkGroup[]
    isLoading: boolean
    commentaryStyles: Record<string, string>
    filteredGroupOptions: Array<{ label: string; value: string | number }>
}>()

const emit = defineEmits<{
    (e: 'clearOtherSelections'): void
    (e: 'update:currentCommentary', payload: { bookId?: number; groupName?: string }): void
}>()

const tabStore = useTabStore()
const categoryTreeStore = useCategoryTreeStore()

const contentRef = ref<HTMLElement | null>(null)
const searchRef = ref<InstanceType<typeof GenericSearch> | null>(null)
const selectAllWasPressed = ref(false)

// UI State
const showAllCommentaries = ref(true) // Start with "show all" mode by default
const currentGroupIndex = ref(0)

// Search state
const isSearchOpen = ref(false)
const searchQuery = ref('')
const matches = ref<number[]>([])
const currentMatchIndex = ref(-1)
const totalMatches = computed(() => matches.value.length)

const { focused: hasFocus } = useFocus(contentRef)

// Computed: Sync combobox with currentGroupIndex
const comboboxSelectedValue = computed<string | number>({
    get: () => currentGroupIndex.value,
    set: (value: string | number) => {
        if (typeof value === 'number') {
            currentGroupIndex.value = value
        }
    }
})

const canNavigateToPreviousGroup = computed(() => {
    return props.processedLinkGroups.length > 0 && currentGroupIndex.value > 0
})

const canNavigateToNextGroup = computed(() => {
    return props.processedLinkGroups.length > 0 && currentGroupIndex.value < props.processedLinkGroups.length - 1
})

// Display groups based on mode
const displayGroups = computed(() => {
    if (!props.processedLinkGroups || props.processedLinkGroups.length === 0) {
        return []
    }

    if (showAllCommentaries.value) {
        return props.processedLinkGroups
    } else {
        // Single mode - show only selected group
        if (currentGroupIndex.value < 0 || currentGroupIndex.value >= props.processedLinkGroups.length) {
            return []
        }
        return [props.processedLinkGroups[currentGroupIndex.value]]
    }
})

// Watch for group changes and emit to parent
watch(currentGroupIndex, (newIndex) => {
    const group = props.processedLinkGroups[newIndex]
    if (group) {
        emit('update:currentCommentary', {
            bookId: group.targetBookId,
            groupName: group.groupName
        })
    }
})

// Watch for group changes in single mode and scroll to top
watch(() => currentGroupIndex.value, () => {
    if (!showAllCommentaries.value && contentRef.value) {
        contentRef.value.scrollTop = 0
    }
    // Clear search when switching groups
    if (isSearchOpen.value) {
        handleSearchClose()
    }
})

// Watch for mode changes and scroll to current group in all mode
watch(() => showAllCommentaries.value, async (isAll) => {
    if (isAll) {
        await nextTick()
        // Use setTimeout to avoid blocking UI
        setTimeout(() => {
            scrollToGroup(currentGroupIndex.value, true)
        }, 0)
    }
})

/**
 * Scroll to a specific group by index
 */
async function scrollToGroup(groupIndex: number, instant = false) {
    if (!showAllCommentaries.value) return
    if (!contentRef.value) return
    if (groupIndex < 0 || groupIndex >= props.processedLinkGroups.length) return

    await nextTick()

    const targetElement = contentRef.value.querySelector(`[data-group-index="${groupIndex}"]`) as HTMLElement
    if (targetElement) {
        await scrollToElementTop(targetElement, { behavior: instant ? 'instant' : 'smooth' })
    }
}

/**
 * Public method to scroll to group by index (called from parent)
 */
function scrollToGroupByIndex(groupIndex: number) {
    if (groupIndex < 0 || groupIndex >= props.processedLinkGroups.length) {
        return
    }

    currentGroupIndex.value = groupIndex

    // If in "show all" mode, scroll to the group
    if (showAllCommentaries.value) {
        scrollToGroup(groupIndex, true)
    }
}

/**
 * Public method to navigate to previous group
 */
function navigateToPreviousGroup() {
    if (!canNavigateToPreviousGroup.value) return
    currentGroupIndex.value = currentGroupIndex.value - 1
}

/**
 * Public method to navigate to next group
 */
function navigateToNextGroup() {
    if (!canNavigateToNextGroup.value) return
    currentGroupIndex.value = currentGroupIndex.value + 1
}

/**
 * Public method to toggle view mode
 */
function toggleViewMode() {
    showAllCommentaries.value = !showAllCommentaries.value

    // When switching to single mode, ensure we have a valid group selected
    if (!showAllCommentaries.value && props.processedLinkGroups.length > 0) {
        if (currentGroupIndex.value < 0 || currentGroupIndex.value >= props.processedLinkGroups.length) {
            currentGroupIndex.value = 0
        }
    }
}

/**
 * Handle search
 */
function handleSearch(query: string) {
    searchQuery.value = query
    matches.value = []
    currentMatchIndex.value = -1

    if (!query) return

    const container = contentRef.value
    if (!container) return

    // Remove existing highlights
    const highlighted = container.querySelectorAll('.search-highlight, .search-highlight-current')
    highlighted.forEach(el => {
        const parent = el.parentNode
        if (parent) {
            parent.replaceChild(document.createTextNode(el.textContent || ''), el)
            parent.normalize()
        }
    })

    // Search in headers and links
    const searchLower = query.toLowerCase()
    let matchIndex = 0

    const headers = container.querySelectorAll('.group-header')
    headers.forEach((headerEl) => {
        const text = headerEl.textContent || ''
        if (text.toLowerCase().includes(searchLower)) {
            matches.value.push(matchIndex++)
        }
    })

    const linkEls = container.querySelectorAll('.link-item')
    linkEls.forEach((linkEl) => {
        const text = linkEl.textContent || ''
        if (text.toLowerCase().includes(searchLower)) {
            matches.value.push(matchIndex++)
        }
    })

    if (matches.value.length > 0) {
        currentMatchIndex.value = 0
        highlightMatches()
    }
}

/**
 * Highlight search matches
 */
function highlightMatches() {
    if (!searchQuery.value) return

    const container = contentRef.value
    if (!container) return

    const searchLower = searchQuery.value.toLowerCase()
    let matchIndex = 0

    // Highlight in headers
    const headers = container.querySelectorAll('.group-header')
    headers.forEach((headerEl) => {
        highlightInElement(headerEl as HTMLElement, searchLower, matchIndex)
        const text = headerEl.textContent || ''
        if (text.toLowerCase().includes(searchLower)) {
            matchIndex++
        }
    })

    // Highlight in links
    const linkEls = container.querySelectorAll('.link-item')
    linkEls.forEach((linkEl) => {
        highlightInElement(linkEl as HTMLElement, searchLower, matchIndex)
        const text = linkEl.textContent || ''
        if (text.toLowerCase().includes(searchLower)) {
            matchIndex++
        }
    })
}

/**
 * Highlight search term in element
 */
function highlightInElement(element: HTMLElement, searchTerm: string, matchIndex: number) {
    const text = element.textContent || ''
    const lowerText = text.toLowerCase()
    const index = lowerText.indexOf(searchTerm)

    if (index === -1) return

    const isCurrent = matchIndex === currentMatchIndex.value
    const className = isCurrent ? 'search-highlight-current' : 'search-highlight'

    const before = text.substring(0, index)
    const match = text.substring(index, index + searchTerm.length)
    const after = text.substring(index + searchTerm.length)

    element.innerHTML = `${before}<span class="${className}">${match}</span>${after}`

    if (isCurrent) {
        element.scrollIntoView({ behavior: 'smooth', block: 'nearest' })
    }
}

/**
 * Navigate to next match
 */
function handleSearchNext() {
    if (matches.value.length === 0) return

    currentMatchIndex.value = (currentMatchIndex.value + 1) % matches.value.length
    highlightMatches()
}

/**
 * Navigate to previous match
 */
function handleSearchPrevious() {
    if (matches.value.length === 0) return

    currentMatchIndex.value = currentMatchIndex.value <= 0
        ? matches.value.length - 1
        : currentMatchIndex.value - 1
    highlightMatches()
}

/**
 * Open search
 */
function openSearch() {
    isSearchOpen.value = true
}

/**
 * Close search
 */
function handleSearchClose() {
    isSearchOpen.value = false
    searchQuery.value = ''
    matches.value = []
    currentMatchIndex.value = -1

    // Remove highlights
    if (contentRef.value) {
        const highlighted = contentRef.value.querySelectorAll('.search-highlight, .search-highlight-current')
        highlighted.forEach(el => {
            const parent = el.parentNode
            if (parent) {
                parent.replaceChild(document.createTextNode(el.textContent || ''), el)
                parent.normalize()
            }
        })
    }
}

/**
 * Handle group header click
 */
function handleGroupClick(group: CommentaryLinkGroup) {
    if (group.targetBookId !== undefined && group.targetLineIndex !== undefined) {
        const targetBook = categoryTreeStore.allBooks.find(book => book.id === group.targetBookId)
        const targetHasConnections = targetBook ? hasConnections(targetBook) : false

        tabStore.openBookInNewTab(
            group.groupName,
            group.targetBookId,
            targetHasConnections,
            group.targetLineIndex,
            true
        )
    }
}

/**
 * Handle keydown
 */
function handleKeyDown(e: KeyboardEvent) {
    if (e.key === ' ' && e.target instanceof HTMLElement && e.target.tagName !== 'INPUT') {
        e.preventDefault()
    }
}

/**
 * Select all content
 */
function selectAllInContainer() {
    const container = contentRef.value
    if (!container) return

    const selection = window.getSelection()
    if (!selection) return

    const range = document.createRange()
    range.selectNodeContents(container)
    selection.removeAllRanges()
    selection.addRange(range)

    emit('clearOtherSelections')
}

// Keyboard shortcuts
useEventListener('keydown', (event: KeyboardEvent) => {
    if (!hasFocus.value) return

    const hasCtrlOrMeta = event.ctrlKey || event.metaKey

    if (hasCtrlOrMeta && event.code === 'KeyF') {
        event.preventDefault()
        openSearch()
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

useEventListener(contentRef, 'mousedown', () => {
    selectAllWasPressed.value = false
})

useEventListener(document, 'selectionchange', () => {
    if (!hasFocus.value) return

    const selection = window.getSelection()
    if (!selection || selection.rangeCount === 0 || selection.isCollapsed) {
        selectAllWasPressed.value = false
    }
})

defineExpose({
    contentRef,
    searchRef,
    hasFocus,
    scrollToGroupByIndex,
    navigateToPreviousGroup,
    navigateToNextGroup,
    toggleViewMode,
    openSearch,
    isSearchOpen,
    currentGroupIndex,
    canNavigateToPreviousGroup,
    canNavigateToNextGroup,
    showAllCommentaries
})
</script>

<style scoped>
/* Shared styles */
.group-header {
    font-size: 1.1em;
    margin: 12px;
    margin-bottom: 16px;
    padding-bottom: 4px;
    border-bottom: 1px solid var(--border-color);
}

.link-item {
    margin: 0 12px 8px 12px;
    padding: 4px 0;
}

.commentary-loading,
.commentary-placeholder {
    padding: 24px;
}

.placeholder-text {
    font-size: 1.1em;
}

/* Search highlighting */
:deep(.search-highlight) {
    background-color: yellow;
    color: black;
}

:deep(.search-highlight-current) {
    background-color: orange;
    color: black;
    font-weight: bold;
}

.commentary-content {
    height: 100%;
    overflow-y: auto;
    overflow-x: hidden;
    position: relative;
}

.commentary-container {
    max-width: 100%;
}
</style>
