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
import { ref, computed, watch } from 'vue'
import { useEventListener } from '@vueuse/core'
import GenericSearch from '@/components/shared/GenericSearch.vue'
import LoadingSpinner from '@/components/shared/LoadingSpinner.vue'
import type { CommentaryLinkGroup } from '@/data/services/bookCommentaryService'
import { useCommentarySearch } from './useCommentarySearch'
import { useCommentaryContentView } from './useCommentaryContentView'

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

const contentRef = ref<HTMLElement | null>(null)
const searchRef = ref<InstanceType<typeof GenericSearch> | null>(null)
const selectAllWasPressed = ref(false)

// Search composable
const {
    isSearchOpen,
    currentMatchIndex,
    totalMatches,
    handleSearch,
    handleSearchNext,
    handleSearchPrevious,
    openSearch,
    handleSearchClose
} = useCommentarySearch(contentRef)

// Content view composable
const {
    showAllCommentaries,
    currentGroupIndex,
    hasFocus,
    canNavigateToPreviousGroup,
    canNavigateToNextGroup,
    displayGroups,
    scrollToGroupByIndex,
    navigateToPreviousGroup,
    navigateToNextGroup,
    toggleViewMode,
    handleGroupClick
} = useCommentaryContentView(
    () => contentRef.value,
    () => props.processedLinkGroups
)

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

// Watch for group changes and clear search
watch(() => currentGroupIndex.value, () => {
    if (isSearchOpen.value) {
        handleSearchClose()
    }
})

// Handle keydown
const handleKeyDown = (e: KeyboardEvent) => {
    if (e.key === ' ' && e.target instanceof HTMLElement && e.target.tagName !== 'INPUT') {
        e.preventDefault()
    }
}

// Select all content
const selectAllInContainer = () => {
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
    font-family: var(--commentary-header-font);
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
