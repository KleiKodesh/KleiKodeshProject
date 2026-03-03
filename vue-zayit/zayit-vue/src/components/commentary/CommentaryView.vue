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
import { bookCommentaryService, type CommentaryLinkGroup } from '@/data/services/bookCommentaryService'
import { useTabStore } from '@/data/stores/tabStore'
import { useSettingsStore } from '@/data/stores/settingsStore'
import { useCommentaryNavigation } from './useCommentaryNavigation'
import { useCommentaryFilters } from './useCommentaryFilters'
import { useCommentaryLoader } from './useCommentaryLoader'
import { applyDiacriticsFilter } from '@/utils/hebrewTextProcessing'
import type { Book } from '@/data/types/Book'
import type { TocEntry } from '@/data/types/BookToc'

// ============================================
// PROPS & EMITS
// ============================================
const props = withDefaults(defineProps<{
    bookId?: number
    selectedLineIndex?: number
    book?: Book
    flatTocEntries?: TocEntry[]
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

// Composables
const { isNavigatingToLine, findNextLineWithCommentary, findPreviousLineWithCommentary } = useCommentaryNavigation()
const { linkGroups, isLoading, availableFilterOptions, loadCommentaryLinks: loadLinks } = useCommentaryLoader()

// Computed for sorted groups
const sortedLinkGroups = computed(() => {
    const sorted = [...linkGroups.value]
    sorted.sort((a, b) => a.groupName.localeCompare(b.groupName, 'he'))
    return sorted
})

// Filters composable
const { selectedCategoryFilter, filteredGroupOptions, availableCategories } = useCommentaryFilters(sortedLinkGroups)

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
        fontFamily: settingsStore.commentaryTextFont,
        fontSize: `calc(${settingsStore.commentaryFontSize}% * ${zoom / 100})`,
        lineHeight: settingsStore.commentaryLinePadding.toString()
    }
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

// ============================================
// WATCHERS
// ============================================
watch([() => props.bookId, () => props.selectedLineIndex, () => tabStore.activeTab?.bookState?.selectedTocEntryId, selectedConnectionTypeId],
    async ([bookId, lineIndex], [oldBookId, oldLineIndex]) => {
        if (bookId !== undefined && lineIndex !== undefined) {
            const isLineNavigation = oldBookId === bookId && oldLineIndex !== undefined && oldLineIndex !== lineIndex
            
            await loadLinks(
                bookId,
                lineIndex,
                tabStore.activeTab?.id?.toString() || '',
                selectedConnectionTypeId.value,
                tabStore.activeTab?.bookState?.selectedTocEntryId,
                props.book
            )

            await nextTick()

            if (isLineNavigation) {
                await handleLineNavigationCommentary()
            } else {
                await handleFirstLoadDefaultCommentary()
            }
        }
    },
    { immediate: true }
)

// ============================================
// CORE FUNCTIONS
// ============================================
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
async function handleNavigateToNextLine() {
    if (!canNavigateToNextLine.value || props.selectedLineIndex === undefined || isNavigatingToLine.value) return
    if (!props.bookId) return

    const activeTab = tabStore.activeTab
    const currentCommentaryBookId = activeTab?.bookState?.currentCommentaryBookId
    if (!currentCommentaryBookId) return

    isNavigatingToLine.value = true

    try {
        const result = await findNextLineWithCommentary(
            props.bookId,
            props.selectedLineIndex,
            currentCommentaryBookId,
            activeTab?.id?.toString() || '',
            selectedConnectionTypeId.value,
            props.flatTocEntries,
            activeTab?.bookState?.selectedTocEntryId
        )

        if (result !== null) {
            emit('navigate-line', result.lineIndex, result.tocEntryId)
        }
    } finally {
        isNavigatingToLine.value = false
    }
}

async function handleNavigateToPreviousLine() {
    if (!canNavigateToPreviousLine.value || props.selectedLineIndex === undefined || isNavigatingToLine.value) return
    if (!props.bookId) return

    const activeTab = tabStore.activeTab
    const currentCommentaryBookId = activeTab?.bookState?.currentCommentaryBookId
    if (!currentCommentaryBookId) return

    isNavigatingToLine.value = true

    try {
        const result = await findPreviousLineWithCommentary(
            props.bookId,
            props.selectedLineIndex,
            currentCommentaryBookId,
            activeTab?.id?.toString() || '',
            selectedConnectionTypeId.value,
            props.flatTocEntries,
            activeTab?.bookState?.selectedTocEntryId
        )

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
