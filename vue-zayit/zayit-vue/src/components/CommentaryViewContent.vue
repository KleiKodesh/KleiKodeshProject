<template>
    <div ref="contentRef"
         class="commentary-content"
         :style="commentaryStyles"
         tabindex="0"
         @keydown="handleKeyDown"
         @copy="handleCopy">

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
                {{ showAllCommentaries ? 'לא נמצרו קשרים בקטגוריה זו' : 'בחר פרשן מהרשימה' }}
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
    showAllCommentaries: boolean
    selectedGroupIndex: number
    currentGroupIndex: number
    scrollObserverEnabled: boolean
}>()

const emit = defineEmits<{
    (e: 'clearOtherSelections'): void
    (e: 'scrollUpdate', groupIndex: number): void
    (e: 'openSearch'): void
}>()

const tabStore = useTabStore()
const categoryTreeStore = useCategoryTreeStore()

const contentRef = ref<HTMLElement | null>(null)
const searchRef = ref<InstanceType<typeof GenericSearch> | null>(null)
const selectAllWasPressed = ref(false)

// Search state
const isSearchOpen = ref(false)
const searchQuery = ref('')
const matches = ref<number[]>([])
const currentMatchIndex = ref(-1)
const totalMatches = computed(() => matches.value.length)

const { focused: hasFocus } = useFocus(contentRef)

// Display groups based on mode
const displayGroups = computed(() => {
    if (props.showAllCommentaries) {
        return props.processedLinkGroups
    } else {
        // Single mode - show only selected group
        if (props.selectedGroupIndex < 0 || props.selectedGroupIndex >= props.processedLinkGroups.length) {
            return []
        }
        return [props.processedLinkGroups[props.selectedGroupIndex]]
    }
})

// Watch for group changes in single mode and scroll to top
watch(() => props.selectedGroupIndex, () => {
    if (!props.showAllCommentaries && contentRef.value) {
        contentRef.value.scrollTop = 0
    }
    // Clear search when switching groups
    if (isSearchOpen.value) {
        handleSearchClose()
    }
})

// Watch for mode changes and scroll to current group in all mode
watch(() => props.showAllCommentaries, async (isAll) => {
    if (isAll) {
        await nextTick()
        // Use setTimeout to avoid blocking UI
        setTimeout(() => {
            scrollToGroup(props.currentGroupIndex, true)
        }, 0)
    }
})

// Scroll observer for "All" mode
let scrollRafId: number | null = null
function onScroll() {
    if (!props.showAllCommentaries || !props.scrollObserverEnabled) return

    if (scrollRafId) {
        cancelAnimationFrame(scrollRafId)
    }
    scrollRafId = requestAnimationFrame(() => {
        const centerGroupIndex = findCenterCommentaryGroup()
        if (centerGroupIndex !== null) {
            emit('scrollUpdate', centerGroupIndex)
        }
        scrollRafId = null
    })
}

// Setup scroll listener
watch(contentRef, (newRef) => {
    if (newRef) {
        newRef.addEventListener('scroll', onScroll, { passive: true })
    }
}, { immediate: true })

/**
 * Find the commentary group at the center of the viewport
 */
function findCenterCommentaryGroup(): number | null {
    if (!contentRef.value) return null

    const containerRect = contentRef.value.getBoundingClientRect()
    const centerY = containerRect.top + containerRect.height / 2

    const items = contentRef.value.querySelectorAll('[data-group-index]')
    if (items.length === 0) return null

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

    const groupIndexAttr = (closestItem as Element).getAttribute('data-group-index')
    if (groupIndexAttr !== null) {
        return parseInt(groupIndexAttr, 10)
    }

    return null
}

/**
 * Scroll to a specific group by index
 */
async function scrollToGroup(groupIndex: number, instant = false) {
    if (!props.showAllCommentaries) return
    if (!contentRef.value) return
    if (groupIndex < 0 || groupIndex >= props.processedLinkGroups.length) return

    await nextTick()

    const targetElement = contentRef.value.querySelector(`[data-group-index="${groupIndex}"]`) as HTMLElement
    if (targetElement) {
        await scrollToElementTop(targetElement, { behavior: instant ? 'instant' : 'smooth' })
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
    emit('openSearch')
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

/**
 * Handle copy event
 */
function handleCopy(event: ClipboardEvent) {
    const selection = window.getSelection()
    if (!selection || selection.rangeCount === 0) return

    const containerEl = contentRef.value
    if (!containerEl) return

    const range = selection.getRangeAt(0)
    if (!containerEl.contains(range.commonAncestorContainer)) return

    // Check if full selection
    const isFullSelection = range.startContainer === containerEl ||
        containerEl.contains(range.startContainer) &&
        containerEl.contains(range.endContainer) &&
        range.toString().length > containerEl.textContent!.length * 0.95

    if (!isFullSelection) return

    // Get source commentary content
    let htmlContent = ''
    let textContent = ''

    displayGroups.value.filter((g): g is CommentaryLinkGroup => g !== undefined).forEach((group) => {
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
    scrollToGroup,
    openSearch,
    isSearchOpen
})
</script>

<style scoped>
@import './CommentaryViewShared.css';

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
