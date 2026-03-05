import { ref, computed, watch, nextTick, onMounted, onUnmounted } from 'vue'
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
    const isProgrammaticScroll = ref(false)
    let scrollTimeout: ReturnType<typeof setTimeout> | null = null

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

        isProgrammaticScroll.value = true
        await nextTick()

        const targetElement = container.querySelector(`[data-group-index="${groupIndex}"]`) as HTMLElement
        if (targetElement) {
            await scrollToElementTop(targetElement, { behavior: instant ? 'instant' : 'smooth' })
        }

        setTimeout(() => {
            isProgrammaticScroll.value = false
        }, 300)
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
        const newIndex = currentGroupIndex.value - 1
        currentGroupIndex.value = newIndex
        if (showAllCommentaries.value) {
            scrollToGroup(newIndex)
        }
    }

    const navigateToNextGroup = () => {
        if (!canNavigateToNextGroup.value) return
        const newIndex = currentGroupIndex.value + 1
        currentGroupIndex.value = newIndex
        if (showAllCommentaries.value) {
            scrollToGroup(newIndex)
        }
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
            setupScrollListener()
        } else {
            cleanupScrollListener()
        }
    })

    // Detect which group is at the center of the viewport
    const detectCenterGroup = () => {
        if (!showAllCommentaries.value || isProgrammaticScroll.value) return

        const container = contentRef()
        if (!container) return

        const containerRect = container.getBoundingClientRect()
        const centerY = containerRect.top + containerRect.height / 2

        const groupHeaders = container.querySelectorAll('[data-group-index]')
        let closestGroup = -1
        let closestDistance = Infinity

        groupHeaders.forEach((header) => {
            const groupIndex = parseInt(header.getAttribute('data-group-index') || '-1')
            if (groupIndex < 0) return

            const rect = header.getBoundingClientRect()
            const headerCenter = rect.top + rect.height / 2
            const distance = Math.abs(headerCenter - centerY)

            if (distance < closestDistance) {
                closestDistance = distance
                closestGroup = groupIndex
            }
        })

        if (closestGroup >= 0 && closestGroup !== currentGroupIndex.value) {
            currentGroupIndex.value = closestGroup
        }
    }

    const handleScroll = () => {
        if (scrollTimeout) {
            clearTimeout(scrollTimeout)
        }

        scrollTimeout = setTimeout(() => {
            detectCenterGroup()
        }, 100)
    }

    const setupScrollListener = () => {
        cleanupScrollListener()
        const container = contentRef()
        if (container && showAllCommentaries.value) {
            container.addEventListener('scroll', handleScroll, { passive: true })
        }
    }

    const cleanupScrollListener = () => {
        if (scrollTimeout) {
            clearTimeout(scrollTimeout)
            scrollTimeout = null
        }
        const container = contentRef()
        if (container) {
            container.removeEventListener('scroll', handleScroll)
        }
    }

    // Setup scroll listener when groups change
    watch(() => processedLinkGroups().length, async () => {
        if (showAllCommentaries.value) {
            await nextTick()
            setTimeout(() => {
                setupScrollListener()
            }, 100)
        }
    })

    onMounted(() => {
        if (showAllCommentaries.value) {
            setTimeout(() => {
                setupScrollListener()
            }, 100)
        }
    })

    onUnmounted(() => {
        cleanupScrollListener()
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
