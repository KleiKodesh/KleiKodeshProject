import { ref, computed, watch, nextTick } from 'vue'
import { useTabStore } from '@/data/stores/tabStore'
import { useSettingsStore } from '@/data/stores/settingsStore'
import { bookCommentaryService } from '@/data/services/bookCommentaryService'
import { applyDiacriticsFilter } from '@/utils/hebrewTextProcessing'
import type { Book } from '@/data/types/Book'
import type { TocEntry } from '@/data/types/BookToc'
import type { CommentaryLinkGroup } from '@/data/services/bookCommentaryService'

export function useCommentaryView(
    props: {
        bookId?: number
        selectedLineIndex?: number
        book?: Book
        flatTocEntries?: TocEntry[]
    },
    sortedLinkGroups: () => CommentaryLinkGroup[]
) {
    const tabStore = useTabStore()
    const settingsStore = useSettingsStore()

    const commentaryToolbarPosition = computed(() => settingsStore.commentaryToolbarPosition)
    const commentaryToolbarPositionClass = computed(() => `commentary-toolbar-${commentaryToolbarPosition.value}`)

    const canNavigateToPreviousLine = computed(() => {
        return props.selectedLineIndex !== undefined && props.selectedLineIndex > 0
    })

    const canNavigateToNextLine = computed(() => {
        return props.selectedLineIndex !== undefined && props.bookId !== undefined
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

        return sortedLinkGroups().map((group) => {
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

    const handleClose = () => {
        const activeTab = tabStore.activeTab
        if (activeTab?.bookState) {
            activeTab.bookState.showBottomPane = false
        }
    }

    return {
        commentaryToolbarPosition,
        commentaryToolbarPositionClass,
        canNavigateToPreviousLine,
        canNavigateToNextLine,
        selectedConnectionTypeId,
        commentaryStyles,
        processedLinkGroups,
        handleClose
    }
}
