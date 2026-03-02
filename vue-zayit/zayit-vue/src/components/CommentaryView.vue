<template>
    <div class="flex-column height-fill commentary-container"
         :class="commentaryToolbarPositionClass">
        <!-- Toolbar -->
        <CommentaryViewToolbar :can-navigate-to-previous-line="canNavigateToPreviousLine"
                               :can-navigate-to-next-line="canNavigateToNextLine"
                               :is-navigating-to-line="isNavigatingToLine"
                               :can-navigate-to-previous-group="canNavigateToPreviousGroup"
                               :can-navigate-to-next-group="canNavigateToNextGroup"
                               :book="book"
                               :selected-connection-type-id="selectedConnectionTypeId"
                               :available-filter-options="availableFilterOptions"
                               :combobox-selected-value="comboboxSelectedValue"
                               :filtered-group-options="filteredGroupOptions"
                               :show-all-commentaries="showAllCommentaries"
                               :available-categories="availableCategories"
                               :selected-category-filter="selectedCategoryFilter"
                               @navigate-previous-line="handleNavigateToPreviousLine"
                               @navigate-next-line="handleNavigateToNextLine"
                               @open-search="handleOpenSearch"
                               @connection-type-change="handleConnectionTypeChange"
                               @update:combobox-value="handleComboboxValueChange"
                               @update:category-filter="selectedCategoryFilter = $event"
                               @navigate-previous-group="handleNavigateToPreviousGroup"
                               @navigate-next-group="handleNavigateToNextGroup"
                               @toggle-view-mode="handleToggleViewMode"
                               @close="handleClose" />

        <!-- Content Area -->
        <div class="commentary-main-area">
            <CommentaryContentView ref="commentaryViewContentRef"
                                   :processed-link-groups="processedLinkGroups"
                                   :is-loading="isLoading"
                                   :commentary-styles="commentaryStyles"
                                   :filtered-group-options="filteredGroupOptions"
                                   @clear-other-selections="emit('clearOtherSelections')"
                                   @update:current-commentary="handleCurrentCommentaryChange" />
        </div>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, watch, nextTick } from 'vue'
import CommentaryViewToolbar from './CommentaryViewToolbar.vue'
import CommentaryContentView from './CommentaryContentView.vue'
import { bookCommentaryService, type CommentaryLinkGroup } from '../services/bookCommentaryService'
import { dbService } from '../services/dbService'
import { useTabStore } from '../stores/tabStore'
import { useSettingsStore } from '../stores/settingsStore'
import { useCategoryTreeStore } from '../stores/categoryTreeStore'
import type { Book } from '../types/Book'
import type { ComboboxOption } from './common/Combobox.vue'

// ============================================
// PROPS & EMITS
// ============================================
const props = withDefaults(defineProps<{
    bookId?: number
    selectedLineIndex?: number
    book?: Book
    flatTocEntries?: Array<{ id: number; lineIndex?: number; isAltToc?: boolean }>
}>(), {
    bookId: undefined,
    selectedLineIndex: undefined,
    book: undefined,
    flatTocEntries: () => []
})

const emit = defineEmits<{
    (e: 'clearOtherSelections'): void
    (e: 'navigate-line', newIndex: number, tocEntryId?: number): void
}>()

// ============================================
// STORES
// ============================================
const tabStore = useTabStore()
const settingsStore = useSettingsStore()

// ============================================
// REFS & STATE
// ============================================
const commentaryViewContentRef = ref<InstanceType<typeof CommentaryContentView> | null>(null)
const linkGroups = ref<CommentaryLinkGroup[]>([])
const isLoading = ref(false)
const selectedCategoryFilter = ref<string | null>(null)
const isNavigatingToLine = ref(false)
const availableFilterOptions = ref<Array<{ label: string; value: number }>>([])

// ============================================
// COMPUTED PROPERTIES
// ============================================
const commentaryToolbarPosition = computed(() => settingsStore.commentaryToolbarPosition)
const commentaryToolbarPositionClass = computed(() => `commentary-toolbar-${commentaryToolbarPosition.value}`)

const canNavigateToPreviousLine = computed(() => {
    return props.selectedLineIndex !== undefined && props.selectedLineIndex > 0
})

const canNavigateToNextLine = computed(() => {
    return props.selectedLineIndex !== undefined && props.bookId !== undefined
})

// Computed properties that delegate to content viewer
const comboboxSelectedValue = computed(() => {
    return commentaryViewContentRef.value?.currentGroupIndex ?? 0
})

const canNavigateToPreviousGroup = computed(() => {
    return commentaryViewContentRef.value?.canNavigateToPreviousGroup ?? false
})

const canNavigateToNextGroup = computed(() => {
    return commentaryViewContentRef.value?.canNavigateToNextGroup ?? false
})

const showAllCommentaries = computed(() => {
    return commentaryViewContentRef.value?.showAllCommentaries ?? false
})

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

const commentaryStyles = computed(() => {
    const zoom = tabStore.activeTab?.bookState?.zoom || 100
    return {
        backgroundColor: 'var(--reading-bg-primary)',
        color: 'var(--reading-text-primary)',
        fontFamily: settingsStore.textFont,
        fontSize: `calc(${settingsStore.fontSize}% * ${zoom / 100})`,
        lineHeight: settingsStore.linePadding.toString()
    }
})

const sortedLinkGroups = computed(() => {
    const sorted = [...linkGroups.value]
    sorted.sort((a, b) => a.groupName.localeCompare(b.groupName, 'he'))
    return sorted
})

const processedLinkGroups = computed(() => {
    const activeTab = tabStore.activeTab
    const diacriticsState = activeTab?.bookState?.diacriticsState

    return sortedLinkGroups.value.map((group) => {
        return {
            ...group,
            links: group.links.map((link) => {
                let html = link.html

                if (diacriticsState && diacriticsState > 0) {
                    html = applyDiacriticsFilter(html, diacriticsState)
                }

                return { ...link, html }
            })
        }
    })
})

const filteredGroupOptions = computed<ComboboxOption[]>(() => {
    const categoryTreeStore = useCategoryTreeStore()

    if (selectedCategoryFilter.value) {
        const filtered = sortedLinkGroups.value
            .map((group, index) => {
                const book = group.targetBookId ? categoryTreeStore.allBooks.find(b => b.id === group.targetBookId) : null
                const period = book?.period || 'אחר'
                return { group, index, period }
            })
            .filter(item => item.period === selectedCategoryFilter.value)

        filtered.sort((a, b) => a.group.groupName.localeCompare(b.group.groupName, 'he'))

        return filtered.map(({ group, index }) => ({
            label: group.groupName,
            value: index
        }))
    }

    const allItems = sortedLinkGroups.value.map((group, index) => ({
        label: group.groupName,
        value: index
    }))

    allItems.sort((a, b) => a.label.localeCompare(b.label, 'he'))

    return allItems
})

const availableCategories = computed(() => {
    const categoryTreeStore = useCategoryTreeStore()
    const categories = new Set<string>()

    sortedLinkGroups.value.forEach((group) => {
        const book = group.targetBookId ? categoryTreeStore.allBooks.find(b => b.id === group.targetBookId) : null
        const period = book?.period || 'אחר'
        categories.add(period)
    })

    const periodOrder = ['תנ"ך', 'ספרות חז"ל', 'גאונים', 'ראשונים', 'אחרונים', 'קבלה', 'מוסר וחסידות', 'הלכה', 'אחר']
    return periodOrder.filter(p => categories.has(p))
})

// ============================================
// WATCHERS
// ============================================
watch([() => props.bookId, () => props.selectedLineIndex, () => tabStore.activeTab?.bookState?.selectedTocEntryId, selectedConnectionTypeId],
    async ([bookId, lineIndex], [oldBookId, oldLineIndex]) => {
        if (bookId !== undefined && lineIndex !== undefined) {
            const isLineNavigation = oldBookId === bookId && oldLineIndex !== undefined && oldLineIndex !== lineIndex
            await loadCommentaryLinks(bookId, lineIndex, isLineNavigation)
        }
    },
    { immediate: true }
)

// ============================================
// CORE FUNCTIONS
// ============================================
async function loadCommentaryLinks(bookId: number, lineIndex: number, isLineNavigation = false) {
    isLoading.value = true

    try {
        const activeTab = tabStore.activeTab
        const tocEntryId = activeTab?.bookState?.selectedTocEntryId

        if (tocEntryId !== undefined) {
            const lineIdResults = await dbService.getLineIdsByTocEntry(bookId, tocEntryId)
            const lineIds = lineIdResults.map(r => r.lineId)

            if (lineIds.length > 0) {
                const connectionTypeId = selectedConnectionTypeId.value
                const linksPromises = lineIds.map(lineId =>
                    dbService.getLinks(lineId, tabStore.activeTab?.id?.toString() || '', bookId, connectionTypeId)
                )
                const allLinksArrays = await Promise.all(linksPromises)
                const allLinks = allLinksArrays.flat()

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
            linkGroups.value = await bookCommentaryService.loadCommentaryLinks(
                bookId,
                lineIndex,
                tabStore.activeTab?.id?.toString() || '',
                { connectionTypeId: selectedConnectionTypeId.value }
            )
        }

        if (tocEntryId !== undefined) {
            if (props.book) {
                availableFilterOptions.value = bookCommentaryService.getAvailableFilterOptions(props.book)
            }
        } else {
            computeAvailableFilterOptions(bookId, lineIndex).catch(() => { })
        }

        await nextTick()

        if (isLineNavigation) {
            await handleLineNavigationCommentary()
        } else {
            await handleFirstLoadDefaultCommentary()
        }

    } catch (error) {
        console.error('❌ Failed to load commentary links:', error)
        linkGroups.value = []
    } finally {
        isLoading.value = false
    }
}

async function scrollToCommentaryBookId(targetBookId: number, targetGroupName?: string) {
    await nextTick()

    let groupIndex = -1

    if (targetGroupName) {
        groupIndex = sortedLinkGroups.value.findIndex(
            group => group.targetBookId === targetBookId && group.groupName === targetGroupName
        )
    }

    if (groupIndex === -1) {
        groupIndex = sortedLinkGroups.value.findIndex(
            group => group.targetBookId === targetBookId
        )
    }

    if (groupIndex === -1) {
        groupIndex = 0
    }

    // Tell content viewer to scroll to this group
    if (commentaryViewContentRef.value) {
        commentaryViewContentRef.value.scrollToGroupByIndex(groupIndex)
    }
}

async function handleFirstLoadDefaultCommentary() {
    const activeTab = tabStore.activeTab
    const currentCommentaryBookId = activeTab?.bookState?.currentCommentaryBookId
    const defaultCommentaryBookId = props.book?.defaultCommentatorBookId

    const targetBookId = currentCommentaryBookId || defaultCommentaryBookId

    if (!targetBookId) return

    await scrollToCommentaryBookId(targetBookId)
}

async function handleLineNavigationCommentary() {
    const activeTab = tabStore.activeTab
    const currentCommentaryBookId = activeTab?.bookState?.currentCommentaryBookId
    const currentCommentaryGroupName = activeTab?.bookState?.currentCommentaryGroupName
    const defaultCommentaryBookId = props.book?.defaultCommentatorBookId

    const targetBookId = currentCommentaryBookId || defaultCommentaryBookId
    if (!targetBookId) {
        if (sortedLinkGroups.value.length > 0 && commentaryViewContentRef.value) {
            commentaryViewContentRef.value.scrollToGroupByIndex(0)
        }
        return
    }

    await scrollToCommentaryBookId(targetBookId, currentCommentaryGroupName)
}

function handleCurrentCommentaryChange(payload: { bookId?: number; groupName?: string }) {
    const activeTab = tabStore.activeTab
    if (activeTab?.bookState) {
        activeTab.bookState.currentCommentaryBookId = payload.bookId
        activeTab.bookState.currentCommentaryGroupName = payload.groupName
    }
}

// ============================================
// NAVIGATION FUNCTIONS
// ============================================
async function findNextLineWithCommentary(startLine: number, maxScanLines = 50): Promise<{ lineIndex: number; tocEntryId?: number } | null> {
    if (!props.bookId) return null

    const activeTab = tabStore.activeTab
    const currentCommentaryBookId = activeTab?.bookState?.currentCommentaryBookId
    if (!currentCommentaryBookId) return null

    const tabId = activeTab?.id?.toString() || ''
    const connectionTypeId = selectedConnectionTypeId.value
    const isInTocMode = activeTab?.bookState?.selectedTocEntryId !== undefined

    if (isInTocMode && props.flatTocEntries && props.flatTocEntries.length > 0) {
        const currentTocIndex = props.flatTocEntries.findIndex(
            toc => toc.lineIndex === startLine && !toc.isAltToc
        )

        const startIndex = currentTocIndex >= 0 ? currentTocIndex + 1 : 0

        for (let i = startIndex; i < props.flatTocEntries.length; i++) {
            const tocEntry = props.flatTocEntries[i]
            if (!tocEntry || tocEntry.isAltToc || tocEntry.lineIndex === undefined) continue

            try {
                const lineIdResults = await dbService.getLineIdsByTocEntry(props.bookId, tocEntry.id)
                const lineIds = lineIdResults.map(r => r.lineId)

                if (lineIds.length > 0) {
                    const linksPromises = lineIds.map(lineId =>
                        dbService.getLinks(lineId, tabId, props.bookId!, connectionTypeId)
                    )
                    const allLinksArrays = await Promise.all(linksPromises)
                    const allLinks = allLinksArrays.flat()

                    const hasCommentary = allLinks.some(link => link.targetBookId === currentCommentaryBookId)
                    if (hasCommentary) {
                        return { lineIndex: tocEntry.lineIndex, tocEntryId: tocEntry.id }
                    }
                }
            } catch (error) {
                continue
            }
        }

        return null
    } else {
        for (let offset = 1; offset <= maxScanLines; offset++) {
            const testLine = startLine + offset

            try {
                const testGroups = await bookCommentaryService.loadCommentaryLinks(
                    props.bookId,
                    testLine,
                    tabId,
                    { connectionTypeId }
                )

                const hasCommentary = testGroups.some(group => group.targetBookId === currentCommentaryBookId)
                if (hasCommentary) {
                    return { lineIndex: testLine }
                }
            } catch (error) {
                break
            }
        }

        return null
    }
}

async function findPreviousLineWithCommentary(startLine: number, maxScanLines = 50): Promise<{ lineIndex: number; tocEntryId?: number } | null> {
    if (!props.bookId) return null

    const activeTab = tabStore.activeTab
    const currentCommentaryBookId = activeTab?.bookState?.currentCommentaryBookId
    if (!currentCommentaryBookId) return null

    const tabId = activeTab?.id?.toString() || ''
    const connectionTypeId = selectedConnectionTypeId.value
    const isInTocMode = activeTab?.bookState?.selectedTocEntryId !== undefined

    if (isInTocMode && props.flatTocEntries && props.flatTocEntries.length > 0) {
        const currentTocIndex = props.flatTocEntries.findIndex(
            toc => toc.lineIndex === startLine && !toc.isAltToc
        )

        const startIndex = currentTocIndex > 0 ? currentTocIndex - 1 : props.flatTocEntries.length - 1

        for (let i = startIndex; i >= 0; i--) {
            const tocEntry = props.flatTocEntries[i]
            if (!tocEntry || tocEntry.isAltToc || tocEntry.lineIndex === undefined) continue

            try {
                const lineIdResults = await dbService.getLineIdsByTocEntry(props.bookId, tocEntry.id)
                const lineIds = lineIdResults.map(r => r.lineId)

                if (lineIds.length > 0) {
                    const linksPromises = lineIds.map(lineId =>
                        dbService.getLinks(lineId, tabId, props.bookId!, connectionTypeId)
                    )
                    const allLinksArrays = await Promise.all(linksPromises)
                    const allLinks = allLinksArrays.flat()

                    const hasCommentary = allLinks.some(link => link.targetBookId === currentCommentaryBookId)
                    if (hasCommentary) {
                        return { lineIndex: tocEntry.lineIndex, tocEntryId: tocEntry.id }
                    }
                }
            } catch (error) {
                continue
            }
        }

        return null
    } else {
        for (let offset = 1; offset <= maxScanLines; offset++) {
            const testLine = startLine - offset
            if (testLine < 0) break

            try {
                const testGroups = await bookCommentaryService.loadCommentaryLinks(
                    props.bookId,
                    testLine,
                    tabId,
                    { connectionTypeId }
                )

                const hasCommentary = testGroups.some(group => group.targetBookId === currentCommentaryBookId)
                if (hasCommentary) {
                    return { lineIndex: testLine }
                }
            } catch (error) {
                continue
            }
        }

        return null
    }
}

async function handleNavigateToNextLine() {
    if (!canNavigateToNextLine.value || props.selectedLineIndex === undefined || isNavigatingToLine.value) return

    isNavigatingToLine.value = true

    try {
        const result = await findNextLineWithCommentary(props.selectedLineIndex)

        if (result !== null) {
            emit('navigate-line', result.lineIndex, result.tocEntryId)
        }
    } finally {
        isNavigatingToLine.value = false
    }
}

async function handleNavigateToPreviousLine() {
    if (!canNavigateToPreviousLine.value || props.selectedLineIndex === undefined || isNavigatingToLine.value) return

    isNavigatingToLine.value = true

    try {
        const result = await findPreviousLineWithCommentary(props.selectedLineIndex)

        if (result !== null) {
            emit('navigate-line', result.lineIndex, result.tocEntryId)
        }
    } finally {
        isNavigatingToLine.value = false
    }
}

function handleConnectionTypeChange(connectionTypeId: number) {
    selectedConnectionTypeId.value = connectionTypeId
}

function handleNavigateToPreviousGroup() {
    commentaryViewContentRef.value?.navigateToPreviousGroup()
}

function handleNavigateToNextGroup() {
    commentaryViewContentRef.value?.navigateToNextGroup()
}

function handleComboboxValueChange(value: string | number) {
    if (typeof value === 'number' && commentaryViewContentRef.value) {
        commentaryViewContentRef.value.currentGroupIndex = value
    }
}

function handleToggleViewMode() {
    commentaryViewContentRef.value?.toggleViewMode()
}

function handleOpenSearch() {
    commentaryViewContentRef.value?.openSearch()
}

function handleClose() {
    const activeTab = tabStore.activeTab
    if (activeTab?.bookState) {
        activeTab.bookState.showBottomPane = false
    }
}

// ============================================
// UTILITY FUNCTIONS
// ============================================
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
            // Ignore errors
        }
    }

    availableFilterOptions.value = results
}

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
</script>

<style scoped>
.commentary-container {
    position: relative;
    overflow: hidden;
}

.commentary-toolbar-top {
    flex-direction: column;
}

.commentary-toolbar-bottom {
    flex-direction: column-reverse;
}

.commentary-main-area {
    position: relative;
    flex: 1;
    overflow: hidden;
}
</style>
