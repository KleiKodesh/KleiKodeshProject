<template>
    <div ref="containerRef"
         class="overflow-y height-fill virtual-line-viewer"
         :style="containerStyles"
         tabindex="0"
         @scroll.passive="onScroll"
         @keydown="onKeyDown">
        <GenericSearch ref="searchRef"
                       :is-open="isSearchOpen"
                       @close="isSearchOpen = false"
                       @search-query-change="handleSearchQueryChange"
                       @navigate-to-match="handleNavigateToMatch" />
        <div :style="{ height: totalHeight + 'px', position: 'relative' }">
            <div :style="{ transform: `translateY(${topPadding}px)` }">
                <BookLine v-for="index in visibleIndexes"
                          :key="index"
                          :content="processedLines[index] || '\u00A0'"
                          :line-index="index"
                          :is-selected="selectedLineIndex === index"
                          :inline-mode="myTab?.bookState?.isLineDisplayInline || false"
                          :alt-toc-entries="props.altTocByLineIndex?.get(index)"
                          :show-alt-toc="myTab?.bookState?.showAltToc"
                          :class="{ 'show-selection': myTab?.bookState?.showBottomPane }"
                          @line-click="handleLineClick"
                          @contextmenu.prevent="handleLineContextMenu($event, index)" />
            </div>
        </div>
        <div v-if="contextMenuState.visible"
             class="virtual-context-menu"
             :style="{ left: contextMenuState.x + 'px', top: contextMenuState.y + 'px' }">
            <div class="context-item"
                 @click="() => { emit('clearOtherSelections'); closeContextMenu() }">Clear selections</div>
            <div class="context-item"
                 @click="() => { if (contextMenuState.lineIndex !== undefined) { handleLineClick(contextMenuState.lineIndex) }; closeContextMenu() }">
                Select line</div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, watch, nextTick, onMounted, onUnmounted } from 'vue'
import GenericSearch from './common/GenericSearch.vue'
import { useElementSize, useEventListener } from '@vueuse/core'
import BookLine from './BookLine.vue'
import { BookLineViewerState } from '../data/bookLineViewerState'
import { useTabStore } from '../stores/tabStore'
import { useSettingsStore } from '../stores/settingsStore'
import { useContentSearch } from '../composables/useContentSearch'

const props = defineProps<{
    tabId?: number
    altTocByLineIndex?: Map<number, import('../data/tocBuilder').AltTocLineEntry[]>
}>()

const emit = defineEmits<{
    updateScrollPosition: [lineIndex: number]
    placeholdersReady: []
    lineClick: [lineIndex: number]
    clearOtherSelections: []
}>()

const tabStore = useTabStore()
const settingsStore = useSettingsStore()
// (search already initialized above)

const viewerState = new BookLineViewerState()
const containerRef = ref<HTMLElement | null>(null)
const { height: containerHeight } = useElementSize(containerRef)
const searchRef = ref<InstanceType<typeof GenericSearch> | null>(null)

const ESTIMATED_LINE_HEIGHT = 36 // adjust if needed
const OVERSCAN = 8

const myTab = computed(() => tabStore.tabs.find(t => t.id === props.tabId))
// Reactive dark mode detection for container styles
const isDarkMode = ref(false)
const updateDarkMode = () => {
    isDarkMode.value = document.documentElement.classList.contains('dark')
}
onMounted(() => {
    updateDarkMode()
    const observer = new MutationObserver(updateDarkMode)
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ['class'] })
    onUnmounted(() => observer.disconnect())
})

const containerStyles = computed(() => ({
    backgroundColor: !isDarkMode.value && settingsStore.readingBackgroundColor
        ? settingsStore.readingBackgroundColor
        : 'var(--bg-primary)',
    color: !isDarkMode.value && settingsStore.readingBackgroundColor
        ? 'var(--reading-text-color)'
        : 'var(--text-primary)'
}))
const selectedLineIndex = ref<number | null>(null)

// Sync selection from tab state (so external navigation updates virtual view)
watch(() => myTab.value?.bookState?.selectedLineIndex, (newIdx) => {
    if (newIdx !== undefined && newIdx !== selectedLineIndex.value) {
        selectedLineIndex.value = newIdx
    }
})

// Process lines with diacritics filtering and search highlighting (mirror non-virtualized behavior)
const search = useContentSearch()
const processedLines = computed(() => {
    const lines = viewerState.lines.value || {}
    const diacriticsState = myTab.value?.bookState?.diacriticsState
    const query = search.searchQuery.value
    const currentMatch = search.currentMatch.value

    const processed: Record<number, string> = {}
    const total = totalLines.value || 0
    for (let lineIndex = 0; lineIndex < total; lineIndex++) {
        const line = lines[lineIndex]
        if (!line || line === '\u00A0') {
            processed[lineIndex] = '\u00A0'
            continue
        }

        let processedLine = line

        if (diacriticsState && diacriticsState > 0) {
            processedLine = applyDiacriticsFilter(processedLine, diacriticsState)
        }

        if (query) {
            const currentOccurrence = currentMatch?.itemIndex === lineIndex ? currentMatch.occurrence : -1
            processedLine = search.highlightMatches(processedLine, query, currentOccurrence)
        }

        processed[lineIndex] = processedLine
    }

    return processed
})

const totalLines = computed(() => viewerState.totalLines.value)
const totalHeight = computed(() => (totalLines.value || 0) * ESTIMATED_LINE_HEIGHT)

const scrollTop = ref(0)

function onScroll() {
    if (!containerRef.value) return
    scrollTop.value = containerRef.value.scrollTop
    const topLine = Math.floor(scrollTop.value / ESTIMATED_LINE_HEIGHT)
    if (myTab.value?.bookState) {
        myTab.value.bookState.initialLineIndex = topLine
    }
    emit('updateScrollPosition', topLine)
}

// Handle search input (mirror non-virtualized behavior)
const isSearchOpen = computed({
    get: () => myTab.value?.bookState?.isSearchOpen || false,
    set: (v: boolean) => { if (myTab.value?.bookState) myTab.value.bookState.isSearchOpen = v }
})

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

    if (settingsStore.enableVirtualization) {
        const searchResults = await viewerState.searchInDB(query)
        search.searchInItems(searchResults, query)
        searchRef.value?.setMatches(search.totalMatches.value)
    } else {
        const allLines = await viewerState.getSearchData()
        search.searchInItems(allLines, query)
        searchRef.value?.setMatches(search.totalMatches.value)
    }
}

function handleNavigateToMatch(matchIndex: number) {
    search.navigateToMatch(matchIndex)
    const match = search.currentMatch.value
    if (match) {
        scrollToLine(match.itemIndex)
        setTimeout(() => {
            const currentMark = document.querySelector('.virtual-line-viewer mark.current')
            if (currentMark && containerRef.value) {
                const markRect = currentMark.getBoundingClientRect()
                const containerRect = containerRef.value.getBoundingClientRect()
                const searchBarOffset = 60
                const effectiveTop = containerRect.top + searchBarOffset
                const isVisible = markRect.top >= effectiveTop && markRect.bottom <= containerRect.bottom
                if (!isVisible) {
                    const offset = markRect.top - containerRect.top - (containerRect.height / 2) + (markRect.height / 2)
                    containerRef.value.scrollTop += offset
                }
            }
        }, 50)
    }
}

// Helper function to apply diacritics filtering to HTML content
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

// Select all helper
function selectAllInContainer() {
    if (!containerRef.value) return
    const selection = window.getSelection()
    if (!selection) return
    const range = document.createRange()
    range.selectNodeContents(containerRef.value)
    selection.removeAllRanges()
    selection.addRange(range)
    emit('clearOtherSelections')
}

// Compute visible window
const startIndex = computed(() => Math.max(0, Math.floor(scrollTop.value / ESTIMATED_LINE_HEIGHT) - OVERSCAN))
const endIndex = computed(() => Math.min(Math.max(0, totalLines.value - 1), Math.ceil((scrollTop.value + (containerHeight.value || 0)) / ESTIMATED_LINE_HEIGHT) + OVERSCAN))
const visibleIndexes = computed(() => {
    const arr: number[] = []
    for (let i = startIndex.value; i <= endIndex.value; i++) arr.push(i)
    return arr
})

const topPadding = computed(() => (startIndex.value) * ESTIMATED_LINE_HEIGHT)

// Load lines when range changes - delegate to viewerState to preserve dynamic loading
let pendingLoadToken = 0
watch([startIndex, endIndex], async ([s, e]) => {
    if (s === undefined || e === undefined) return
    // Load around center of visible window
    const center = Math.floor((s + e) / 2)
    pendingLoadToken++
    const myTok = pendingLoadToken
    // small debounce
    await new Promise(r => setTimeout(r, 50))
    if (myTok !== pendingLoadToken) return
    if (settingsStore.enableVirtualization) {
        await viewerState.loadLinesAround(center, Math.max(50, e - s))
    } else {
        await viewerState.loadLinesAround(center, Math.max(200, e - s))
    }
})

// Expose programmatic scroll
async function scrollToLine(lineIndex: number) {
    if (!containerRef.value) return
    const ch = containerHeight.value || 0
    // Center the line in the viewport when possible
    const desiredTop = Math.max(0, lineIndex * ESTIMATED_LINE_HEIGHT - Math.max(0, (ch - ESTIMATED_LINE_HEIGHT) / 2))
    const maxTop = Math.max(0, totalHeight.value - ch)
    const newTop = Math.max(0, Math.min(desiredTop, maxTop))
    containerRef.value.scrollTop = newTop
    await nextTick()
}

function handleLineClick(lineIndex: number) {
    selectedLineIndex.value = lineIndex
    if (myTab.value?.bookState) myTab.value.bookState.selectedLineIndex = lineIndex
    emit('lineClick', lineIndex)
}

// Keyboard navigation for virtualized viewer
function onKeyDown(e: KeyboardEvent) {
    if (!containerRef.value) return

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

    const total = totalLines.value || 0
    let idx = (myTab.value?.bookState?.selectedLineIndex ?? selectedLineIndex.value) ?? 0

    switch (e.key) {
        case 'ArrowDown':
            idx = Math.min(total - 1, idx + 1)
            break
        case 'ArrowUp':
            idx = Math.max(0, idx - 1)
            break
        case 'PageDown':
            idx = Math.min(total - 1, idx + Math.max(1, Math.floor((containerHeight.value || 1) / ESTIMATED_LINE_HEIGHT)))
            break
        case 'PageUp':
            idx = Math.max(0, idx - Math.max(1, Math.floor((containerHeight.value || 1) / ESTIMATED_LINE_HEIGHT)))
            break
        case 'Home':
            idx = 0
            break
        case 'End':
            idx = Math.max(0, total - 1)
            break
        default:
            return
    }

    e.preventDefault()
    handleLineClick(idx)
    if (myTab.value?.bookState) myTab.value.bookState.selectedLineIndex = idx
    scrollToLine(idx).catch(() => { })
}

// Context menu handling
const contextMenuState = ref<{ visible: boolean; x: number; y: number; lineIndex?: number }>({ visible: false, x: 0, y: 0 })

function handleLineContextMenu(e: MouseEvent, lineIndex: number) {
    e.preventDefault()
    contextMenuState.value = { visible: true, x: e.clientX, y: e.clientY, lineIndex }
}

function closeContextMenu() {
    contextMenuState.value.visible = false
}

// Close on outside click / escape
useEventListener('click', (ev: MouseEvent) => {
    if (!contextMenuState.value.visible) return
    const target = ev.target as Node | null
    // if click outside, close
    if (target && containerRef.value && !containerRef.value.contains(target)) {
        closeContextMenu()
    }
})

useEventListener('keydown', (ev: KeyboardEvent) => {
    if (ev.key === 'Escape' && contextMenuState.value.visible) closeContextMenu()
})

onMounted(async () => {
    // Set virtualization mode from settings then load book
    viewerState.setVirtualizationMode(settingsStore.enableVirtualization)
    const bookId = myTab.value?.bookState?.bookId
    if (bookId) {
        await viewerState.loadBook(bookId, false, myTab.value?.bookState?.initialLineIndex)
        await nextTick()
        emit('placeholdersReady')
        if (myTab.value?.bookState?.initialLineIndex !== undefined) {
            await scrollToLine(myTab.value.bookState.initialLineIndex)
        }
    }
})

// Respond to virtualization setting changes
watch(() => settingsStore.enableVirtualization, async (enableVirtualization, wasEnabled) => {
    const bookId = myTab.value?.bookState?.bookId
    if (!bookId || viewerState.totalLines.value === 0) return

    viewerState.setVirtualizationMode(enableVirtualization)

    if (!enableVirtualization && wasEnabled) {
        // Switching from virtualized to non-virtualized: start progressive loading
        await viewerState.startProgressiveLoading()
    }
})

onUnmounted(() => {
    viewerState.cleanup()
    // Save top visible line into tab state
    if (containerRef.value && myTab.value?.bookState) {
        const topLine = Math.floor((containerRef.value.scrollTop || 0) / ESTIMATED_LINE_HEIGHT)
        myTab.value.bookState.initialLineIndex = topLine
        emit('updateScrollPosition', topLine)
    }
})

// (exposed below together)

async function handleTocSelection(lineIndex: number) {
    await viewerState.handleTocSelection(lineIndex)
    await nextTick()
    await scrollToLine(lineIndex)
}

defineExpose({
    handleTocSelection,
    scrollToLine,
    async scrollToLineIndex(index?: number | null) {
        const target = index !== undefined && index !== null ? index : selectedLineIndex.value
        if (target === undefined || target === null) return
        await scrollToLine(target)
    }
})
</script>

<style scoped>
.virtual-line-viewer {
    padding: 25px 15px;
}

.virtual-context-menu {
    position: fixed;
    background: var(--bg-primary);
    border: 1px solid var(--border-color);
    box-shadow: 0 6px 18px rgba(0, 0, 0, 0.12);
    z-index: 1000;
    padding: 6px 0;
    border-radius: 4px;
}

.virtual-context-menu .context-item {
    padding: 6px 12px;
    cursor: pointer;
    color: var(--text-primary);
}

.virtual-context-menu .context-item:hover {
    background: var(--hover-bg);
}
</style>
