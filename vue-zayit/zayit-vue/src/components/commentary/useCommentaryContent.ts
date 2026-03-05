import { ref, computed, watch, nextTick } from 'vue'
import { useFocus } from '@vueuse/core'
import { useTabStore } from '@/data/stores/tabStore'
import { useCategoryTreeStore } from '@/data/stores/categoryTreeStore'
import { hasConnections } from '@/data/types/Book'
import { scrollToElementTop } from '@/components/shared/useScrollToElement'
import type { CommentaryLinkGroup } from '@/data/services/bookCommentaryService'

export function useCommentaryContent(
    contentRef: () => HTMLElement | null,
    processedLinkGroups: () => CommentaryLinkGroup[]
) {
    const tabStore = useTabStore()
    const categoryTreeStore = useCategoryTreeStore()

    const showAllCommentaries = ref(true)
    const currentGroupIndex = ref(0)

    const { focused: hasFocus } = useFocus(computed(() => contentRef()))

    const comboboxSelectedValue = computed<string | number>({
        get: () => currentGroupIndex.value,
        set: (value: string | number) => {
            if (typeof value === 'number') {
                currentGroupIndex.value = value
            }
        }
    })

    const canNavigateToPreviousGroup = computed(() => {
        return processedLinkGroups().length > 0 && currentGroupIndex.value > 0
    })

    const canNavigateToNextGroup = computed(() => {
        return processedLinkGroups().length > 0 && currentGroupIndex.value < processedLinkGroups().length - 1
    })

    const displayGroups = computed(() => {
        if (!processedLinkGroups() || processedLinkGroups().length === 0) {
            return []
        }

        if (showAllCommentaries.value) {
            return processedLinkGroups()
        } else {
            if (currentGroupIndex.value < 0 || currentGroupIndex.value >= processedLinkGroups().length) {
                return []
            }
            return [processedLinkGroups()[currentGroupIndex.value]]
        }
    })

    const scrollToGroup = async (groupIndex: number, instant = false) => {
        if (!showAllCommentaries.value) return
        const container = contentRef()
        if (!container) return
        if (groupIndex < 0 || groupIndex >= processedLinkGroups().length) return

        await nextTick()

        const targetElement = container.querySelector(`[data-group-index="${groupIndex}"]`) as HTMLElement
        if (targetElement) {
            await scrollToElementTop(targetElement, { behavior: instant ? 'instant' : 'smooth' })
        }
    }

    const scrollToGroupByIndex = (groupIndex: number) => {
        if (groupIndex < 0 || groupIndex >= processedLinkGroups().length) {
            return
        }

        currentGroupIndex.value = groupIndex

        if (showAllCommentaries.value) {
            scrollToGroup(groupIndex, true)
        }
    }

    const navigateToPreviousGroup = () => {
        if (!canNavigateToPreviousGroup.value) return
        currentGroupIndex.value = currentGroupIndex.value - 1
    }

    const navigateToNextGroup = () => {
        if (!canNavigateToNextGroup.value) return
        currentGroupIndex.value = currentGroupIndex.value + 1
    }

    const toggleViewMode = () => {
        showAllCommentaries.value = !showAllCommentaries.value

        if (!showAllCommentaries.value && processedLinkGroups().length > 0) {
            if (currentGroupIndex.value < 0 || currentGroupIndex.value >= processedLinkGroups().length) {
                currentGroupIndex.value = 0
            }
        }
    }

    const handleGroupClick = (group: CommentaryLinkGroup) => {
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

    // Watch for group changes in single mode and scroll to top
    watch(() => currentGroupIndex.value, () => {
        if (!showAllCommentaries.value) {
            const container = contentRef()
            if (container) {
                container.scrollTop = 0
            }
        }
    })

    // Watch for mode changes and scroll to current group in all mode
    watch(() => showAllCommentaries.value, async (isAll) => {
        if (isAll) {
            await nextTick()
            setTimeout(() => {
                scrollToGroup(currentGroupIndex.value, true)
            }, 0)
        }
    })

    return {
        showAllCommentaries,
        currentGroupIndex,
        hasFocus,
        comboboxSelectedValue,
        canNavigateToPreviousGroup,
        canNavigateToNextGroup,
        displayGroups,
        scrollToGroupByIndex,
        navigateToPreviousGroup,
        navigateToNextGroup,
        toggleViewMode,
        handleGroupClick
    }
}
