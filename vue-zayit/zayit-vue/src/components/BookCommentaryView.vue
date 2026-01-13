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
            <span class="bold smaller-em commentary-title">קשרים</span>

            <div class="flex-row flex-center commentary-navigation"
                 v-if="linkGroups.length > 0">

                <button class="flex-center c-pointer nav-btn"
                        @click="openSearch"
                        title="חיפוש (Ctrl+F)">
                    <Icon icon="fluent:search-28-regular"
                          class="small-icon" />
                </button>

                <Combobox v-model="currentGroupIndex"
                          :options="groupOptions"
                          :placeholder="currentGroupName"
                          dir="rtl" />

                <button class="flex-center c-pointer nav-btn"
                        @click="previousGroup"
                        :disabled="currentGroupIndex === 0"
                        title="קבוצה קודמת">
                    <Icon icon="fluent:chevron-right-28-regular"
                          class="small-icon" />
                </button>

                <button class="flex-center c-pointer nav-btn"
                        @click="nextGroup"
                        :disabled="currentGroupIndex === linkGroups.length - 1"
                        title="קבוצה הבאה">
                    <Icon icon="fluent:chevron-left-28-regular"
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
             :style="{ backgroundColor: settingsStore.readingBackgroundColor || 'var(--bg-primary)' }"
             ref="commentaryContentRef"
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
                <div class="bold placeholder-text">בחר שורה לצפייה בקשרים</div>
            </div>

            <div v-else
                 class="flex-column commentary-links">
                <div v-for="(group, groupIndex) in processedLinkGroups"
                     :key="groupIndex"
                     class="flex-column commentary-group"
                     :ref="el => setGroupRef(el, groupIndex)">
                    <div class="bold group-header"
                         :class="{ 'c-pointer': group.targetBookId !== undefined }"
                         @click="handleGroupClick(group)">
                        {{ group.groupName }}
                    </div>
                    <div v-for="(link, linkIndex) in group.links"
                         :key="linkIndex"
                         class="selectable line-1.6 justify link-item"
                         v-html="link.html"></div>
                </div>
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, watch, type ComponentPublicInstance } from 'vue'
import Combobox, { type ComboboxOption } from './common/Combobox.vue'
import GenericSearch from './common/GenericSearch.vue'
import { Icon } from '@iconify/vue'

import { useContentSearch } from '../composables/useContentSearch'
import { commentaryManager, type CommentaryLinkGroup } from '../data/commentaryManager'
import { useTabStore } from '../stores/tabStore'
import { useSettingsStore } from '../stores/settingsStore'

const props = withDefaults(defineProps<{
    bookId?: number
    selectedLineIndex?: number
}>(), {
    bookId: undefined,
    selectedLineIndex: undefined
})

const emit = defineEmits<{
    clearOtherSelections: []
}>()

const tabStore = useTabStore()
const settingsStore = useSettingsStore()

// Commentary state
const linkGroups = ref<CommentaryLinkGroup[]>([])
const isLoading = ref(false)

// Search state
const searchRef = ref<InstanceType<typeof GenericSearch> | null>(null)
const isSearchOpen = ref(false)
const search = useContentSearch()

function openSearch() {
    isSearchOpen.value = true
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

// Load commentary when props change
watch([() => props.bookId, () => props.selectedLineIndex], async ([bookId, lineIndex]) => {
    if (bookId !== undefined && lineIndex !== undefined) {
        await loadCommentaryLinks(bookId, lineIndex)
    }
}, { immediate: true })

async function loadCommentaryLinks(bookId: number, lineIndex: number) {
    isLoading.value = true
    try {
        linkGroups.value = await commentaryManager.loadCommentaryLinks(
            bookId,
            lineIndex,
            tabStore.activeTab?.id?.toString() || ''
        )

        // Restore saved group index if valid
        const savedGroupIndex = tabStore.activeTab?.bookState?.commentaryGroupIndex
        if (savedGroupIndex !== undefined && savedGroupIndex < linkGroups.value.length) {
            currentGroupIndex.value = savedGroupIndex
        }
    } catch (error) {
        console.error('❌ Failed to load commentary links:', error)
        linkGroups.value = []
    } finally {
        isLoading.value = false
    }
}

function handleGroupClick(group: CommentaryLinkGroup) {
    if (group.targetBookId !== undefined && group.targetLineIndex !== undefined) {
        // Use the new method to create a tab directly with book state
        tabStore.openBookInNewTab(group.groupName, group.targetBookId, undefined, group.targetLineIndex)
        console.log(`[Commentary] Created new tab for book: ${group.groupName} (ID: ${group.targetBookId}) at line ${group.targetLineIndex}`)
    }
}

function handleClose() {
    // Close the bottom pane by updating tab state directly
    const activeTab = tabStore.activeTab
    if (activeTab?.bookState) {
        activeTab.bookState.showBottomPane = false
    }
}

// Internal state
const currentGroupIndex = ref(0)
const savedScrollPosition = ref(0)
const commentaryContentRef = ref<HTMLElement | null>(null)
const groupRefs = ref<Map<number, HTMLElement>>(new Map())

// Computed property for current group name
const currentGroupName = computed(() => {
    if (processedLinkGroups.value.length > 0 && currentGroupIndex.value < processedLinkGroups.value.length) {
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

// Set group ref for scrolling
const setGroupRef = (el: Element | ComponentPublicInstance | null, index: number) => {
    if (el && el instanceof HTMLElement) {
        groupRefs.value.set(index, el)
    }
}

// Scroll to specific group
const scrollToGroup = (index: number) => {
    const groupElement = groupRefs.value.get(index)
    if (groupElement && commentaryContentRef.value) {
        commentaryContentRef.value.removeEventListener('scroll', handleCommentaryScroll)
        groupElement.scrollIntoView({ behavior: 'auto', block: 'start' })
        setTimeout(() => {
            if (commentaryContentRef.value) {
                commentaryContentRef.value.addEventListener('scroll', handleCommentaryScroll)
            }
        }, 50)
    }
}

// Handle commentary scroll to update dropdown
const handleCommentaryScroll = () => {
    if (!commentaryContentRef.value || linkGroups.value.length === 0 || groupRefs.value.size === 0) return

    const containerRect = commentaryContentRef.value.getBoundingClientRect()
    const containerTop = containerRect.top + 50

    let activeIndex = 0
    groupRefs.value.forEach((groupElement, index) => {
        const headerRect = groupElement.getBoundingClientRect()
        if (headerRect.top <= containerTop) {
            activeIndex = index
        }
    })

    if (currentGroupIndex.value !== activeIndex) {
        currentGroupIndex.value = activeIndex
        saveGroupIndexToTab()
    }
}

// Navigation functions
const previousGroup = () => {
    if (currentGroupIndex.value > 0) {
        currentGroupIndex.value--
        saveGroupIndexToTab()
        scrollToGroup(currentGroupIndex.value)
    }
}

const nextGroup = () => {
    if (currentGroupIndex.value < linkGroups.value.length - 1) {
        currentGroupIndex.value++
        saveGroupIndexToTab()
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

function clearSelection() {
    const selection = window.getSelection()
    if (selection) {
        selection.removeAllRanges()
    }
}





onMounted(() => {
    if (commentaryContentRef.value) {
        commentaryContentRef.value.addEventListener('scroll', () => {
            if (commentaryContentRef.value) {
                savedScrollPosition.value = commentaryContentRef.value.scrollTop
            }
        })
    }
})

// Save group index to tab state
function saveGroupIndexToTab() {
    const activeTab = tabStore.activeTab
    if (activeTab?.bookState) {
        activeTab.bookState.commentaryGroupIndex = currentGroupIndex.value
    }
}

// Reset group index when new commentary loads
watch(() => linkGroups.value, () => {
    currentGroupIndex.value = 0
    saveGroupIndexToTab()
}, { immediate: true })

watch(currentGroupIndex, (newIndex) => {
    saveGroupIndexToTab()
    scrollToGroup(newIndex)
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
