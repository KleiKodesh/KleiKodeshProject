<template>
    <div class="flex-column height-fill commentary-container"
         :class="commentaryToolbarPositionClass">
        <!-- Toolbar -->
        <CommentaryViewToolbar :title="commentaryTitle"
                               :can-navigate-to-previous-line="canNavigateToPreviousLine"
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
                               @navigate-previous-line="handleNavigateToPreviousLine"
                               @navigate-next-line="handleNavigateToNextLine"
                               @open-search="handleOpenSearch"
                               @connection-type-change="handleConnectionTypeChange"
                               @update:combobox-value="comboboxSelectedValue = $event"
                               @navigate-previous-group="handleNavigateToPreviousGroup"
                               @navigate-next-group="handleNavigateToNextGroup"
                               @toggle-view-mode="handleToggleViewMode"
                               @close="handleClose" />

        <!-- Content Area -->
        <div class="commentary-main-area">
            <CommentaryViewContent ref="commentaryViewContentRef"
                                   :processed-link-groups="processedLinkGroups"
                                   :is-loading="isLoading"
                                   :commentary-styles="commentaryStyles"
                                   :show-all-commentaries="showAllCommentaries"
                                   :selected-group-index="comboboxSelectedValue as number"
                                   :current-group-index="currentGroupIndex"
                                   :scroll-observer-enabled="scrollObserverEnabled"
                                   @clear-other-selections="emit('clearOtherSelections')"
                                   @scroll-update="handleScrollUpdate" />
        </div>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, watch, nextTick } from 'vue'
import { type ComboboxOption } from './common/Combobox.vue'
import CommentaryViewToolbar from './CommentaryViewToolbar.vue'
import CommentaryViewContent from './CommentaryViewContent.vue'
import { bookCommentaryService, type CommentaryLinkGroup } from '../services/bookCommentaryService'
import { dbService } from '../services/dbService'
import { useTabStore } from '../stores/tabStore'
import { useSettingsStore } from '../stores/settingsStore'
import { useCategoryTreeStore } from '../stores/categoryTreeStore'
import type { Book } from '../types/Book'

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

// ============================================
// REFS & STATE
// ============================================
const commentaryViewContentRef = ref<InstanceType<typeof CommentaryViewContent> | null>(null)

// Core State
const linkGroups = ref<CommentaryLinkGroup[]>([])
const isLoading = ref(false)

// Navigation State
const currentGroupIndex = ref(0)
const comboboxSelectedValue = ref<string | number>(0)
const isNavigatingToLine = ref(false)
const skipScrollRestore = ref(false)

// Scroll tracking flags
const isUpdatingFromScroll = ref(false)
const scrollObserverEnabled = ref(true)
const isLineNavigationInProgress = ref(false)

// View Mode State
const showAllCommentaries = ref(false)

// Filter Options
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

const canNavigateToPreviousGroup = computed(() => {
    return sortedLinkGroups.value.length > 0 && currentGroupIndex.value > 0
})

const canNavigateToNextGroup = computed(() => {
    return sortedLinkGroups.value.length > 0 && currentGroupIndex.value < sortedLinkGroups.value.length - 1
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

const commentaryTitle = computed(() => 'קשרים')

// Sort link groups alphabetically
const sortedLinkGroups = computed(() => {
    const sorted = [...linkGroups.value]
    sorted.sort((a, b) => a.groupName.localeCompare(b.groupName, 'he'))
    return sorted
})

// Process link groups with diacritics filtering and search highlighting
const processedLinkGroups = computed(() => {
    const activeTab = tabStore.activeTab
    const diacriticsState = activeTab?.bookState?.diacriticsState

    return sortedLinkGroups.value.map((group) => {
        return {
            ...group,
            links: group.links.map((link) => {
                let html = link.html

                // Apply diacritics filter
                if (diacriticsState && diacriticsState > 0) {
                    html = applyDiacriticsFilter(html, diacriticsState)
                }

                return { ...link, html }
            })
        }
    })
})

// Combobox options
const groupOptions = computed<ComboboxOption[]>(() => {
    return sortedLinkGroups.value.map((group, index) => ({
        label: group.groupName,
        value: index
    }))
})

const filteredGroupOptions = computed<ComboboxOption[]>(() => {
    return groupOptions.value
})

// ============================================
// WATCHERS
// ============================================
// Load commentary when props or connection type changes
watch([() => props.bookId, () => props.selectedLineIndex, () => tabStore.activeTab?.bookState?.selectedTocEntryId, selectedConnectionTypeId],
    async ([bookId, lineIndex], [oldBookId, oldLineIndex]) => {
        if (bookId !== undefined && lineIndex !== undefined) {
            const isLineNavigation = oldBookId === bookId && oldLineIndex !== undefined && oldLineIndex !== lineIndex
            await loadCommentaryLinks(bookId, lineIndex, isLineNavigation)
        }
    },
    { immediate: true }
)

// Handle combobox selection changes
watch(comboboxSelectedValue, (newValue) => {
    if (isLineNavigationInProgress.value) return
    if (isUpdatingFromScroll.value) return

    if (typeof newValue === 'number') {
        if (newValue !== currentGroupIndex.value) {
            currentGroupIndex.value = newValue
            if (showAllCommentaries.value) {
                scrollToGroup(newValue)
            }
        }
    } else if (typeof newValue === 'string') {
        const searchText = newValue.toLowerCase().trim()
        if (searchText) {
            const matchingGroup = sortedLinkGroups.value.find(group =>
                group.groupName.toLowerCase().includes(searchText)
            )
            if (matchingGroup) {
                const matchingIndex = sortedLinkGroups.value.indexOf(matchingGroup)
                if (matchingIndex !== -1 && matchingIndex !== currentGroupIndex.value) {
                    currentGroupIndex.value = matchingIndex
                    if (showAllCommentaries.value) {
                        scrollToGroup(matchingIndex)
                    }
                }
            }
        }
    }
})

// ============================================
// CORE FUNCTIONS
// ============================================
async function loadCommentaryLinks(bookId: number, lineIndex: number, isLineNavigation = false) {
    if (isLineNavigation) {
        isLineNavigationInProgress.value = true
        skipScrollRestore.value = true
    }

    isLoading.value = true
    currentGroupIndex.value = -1

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
            setTimeout(() => {
                isLineNavigationInProgress.value = false
                skipScrollRestore.value = false
            }, 1000)
        } else {
            await handleFirstLoadDefaultCommentary()
        }

    } catch (error) {
        console.error('❌ Failed to load commentary links:', error)
        linkGroups.value = []

        if (isLineNavigation) {
            isLineNavigationInProgress.value = false
            skipScrollRestore.value = false
        }
    } finally {
        isLoading.value = false
    }
}

async function scrollToCommentaryBookId(targetBookId: number, targetGroupName?: string) {
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

    await nextTick()
    comboboxSelectedValue.value = groupIndex
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
        if (sortedLinkGroups.value.length > 0) {
            comboboxSelectedValue.value = 0
        }
        return
    }

    await scrollToCommentaryBookId(targetBookId, currentCommentaryGroupName)
}

// ============================================
// NAVIGATION FUNCTIONS
// ============================================
async function findNextLineWithCommentary(startLine: number, maxScanLines = 50): Promise<number | null> {
    if (!props.bookId) return null

    const activeTab = tabStore.activeTab
    const currentCommentaryBookId = activeTab?.bookState?.currentCommentaryBookId
    if (!currentCommentaryBookId) return null

    const tabId = activeTab?.id?.toString() || ''
    const connectionTypeId = selectedConnectionTypeId.value

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
                return testLine
            }
        } catch (error) {
            break
        }
    }

    return null
}

async function findPreviousLineWithCommentary(startLine: number, maxScanLines = 50): Promise<number | null> {
    if (!props.bookId) return null

    const activeTab = tabStore.activeTab
    const currentCommentaryBookId = activeTab?.bookState?.currentCommentaryBookId
    if (!currentCommentaryBookId) return null

    const tabId = activeTab?.id?.toString() || ''
    const connectionTypeId = selectedConnectionTypeId.value

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
                return testLine
            }
        } catch (error) {
            continue
        }
    }

    return null
}

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

function handleNavigateToPreviousGroup() {
    if (!canNavigateToPreviousGroup.value) return

    const newIndex = currentGroupIndex.value - 1
    comboboxSelectedValue.value = newIndex
}

function handleNavigateToNextGroup() {
    if (!canNavigateToNextGroup.value) return

    const newIndex = currentGroupIndex.value + 1
    comboboxSelectedValue.value = newIndex
}

function handleConnectionTypeChange(connectionTypeId: number) {
    selectedConnectionTypeId.value = connectionTypeId
}

function handleToggleViewMode() {
    showAllCommentaries.value = !showAllCommentaries.value
}

function handleClose() {
    const activeTab = tabStore.activeTab
    if (activeTab?.bookState) {
        activeTab.bookState.showBottomPane = false
    }
}

// ============================================
// SCROLL FUNCTIONS
// ============================================
function handleScrollUpdate(centerGroupIndex: number) {
    if (isLineNavigationInProgress.value) return

    if (centerGroupIndex !== currentGroupIndex.value) {
        const centerGroup = sortedLinkGroups.value[centerGroupIndex]
        const activeTab = tabStore.activeTab

        if (centerGroup && activeTab?.bookState) {
            isUpdatingFromScroll.value = true

            currentGroupIndex.value = centerGroupIndex
            comboboxSelectedValue.value = centerGroupIndex

            activeTab.bookState.currentCommentaryBookId = centerGroup.targetBookId
            activeTab.bookState.currentCommentaryGroupName = centerGroup.groupName

            isUpdatingFromScroll.value = false
        }
    }
}

async function scrollToGroup(groupIndex: number) {
    if (!showAllCommentaries.value) return
    if (groupIndex < 0 || groupIndex >= sortedLinkGroups.value.length) return

    scrollObserverEnabled.value = false

    currentGroupIndex.value = groupIndex

    const targetGroup = sortedLinkGroups.value[groupIndex]
    const activeTab = tabStore.activeTab
    if (targetGroup && activeTab?.bookState) {
        activeTab.bookState.currentCommentaryBookId = targetGroup.targetBookId
        activeTab.bookState.currentCommentaryGroupName = targetGroup.groupName
    }

    await nextTick()
    if (commentaryViewContentRef.value) {
        await commentaryViewContentRef.value.scrollToGroup(groupIndex)
    }

    await nextTick()
    setTimeout(() => {
        scrollObserverEnabled.value = true
    }, 500)
}

// ============================================
// SEARCH FUNCTIONS (Delegate to subcomponent)
// ============================================
function handleOpenSearch() {
    commentaryViewContentRef.value?.openSearch()
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
