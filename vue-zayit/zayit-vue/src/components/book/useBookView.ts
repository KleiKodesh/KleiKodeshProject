import { ref, computed, watch } from 'vue'
import { useTabs } from '@/components/workspace/useTabs'
import { useCategoryTreeStore } from '@/data/stores/categoryTreeStore'
import { dbService } from '@/data/services/dbService'
import { buildTocFromFlat } from '@/data/services/bookTocService'
import type { AltTocLineEntry } from '@/data/services/bookTocService'
import type { TocEntry } from '@/data/types/BookToc'
import type LineView from '@/components/book/LineView.vue'

export function useBookViewPage(
    myTabId: () => number | undefined,
    lineViewerRef: () => InstanceType<typeof LineView> | null
) {
    const { tabs, activeTab, closeToc } = useTabs()
    const categoryTreeStore = useCategoryTreeStore()

    const myTab = computed(() => tabs.value.find(t => t.id === myTabId()))
    const toolbarPosition = computed(() => myTab.value?.bookState?.toolbarPosition || 'top')
    const altTocByLineIndex = ref<Map<number, AltTocLineEntry[]>>(new Map())
    const tocEntries = ref<TocEntry[]>([])
    const flatTocEntries = ref<TocEntry[]>([])
    const isTocLoading = ref(false)
    const currentCenterLineIndex = ref<number | null>(null)
    const currentTocEntryId = ref<number | undefined>(undefined)

    const currentBook = computed(() => {
        const bookId = myTab.value?.bookState?.bookId
        if (!bookId) return undefined
        return categoryTreeStore.allBooks.find(book => book.id === bookId)
    })

    const filteredTocEntries = computed(() => {
        let entries = tocEntries.value

        if (myTab.value?.bookState?.showAltToc === false) {
            entries = entries.filter(entry => !entry.isAltToc)
        }

        const bookTitle = currentBook.value?.title
        if (bookTitle && entries.length === 1) {
            const rootEntry = entries[0]
            if (rootEntry && (rootEntry.level === 0 || !rootEntry.parentId) &&
                rootEntry.text.trim().toLowerCase() === bookTitle.trim().toLowerCase() &&
                rootEntry.children && rootEntry.children.length > 0) {
                return rootEntry.children
            }
        }

        return entries
    })

    const loadTocData = async (bookId: number) => {
        isTocLoading.value = true
        try {
            const { tocEntriesFlat } = await dbService.getToc(bookId)
            const { tree, allTocs, altTocByLineIndex: altTocMap } = buildTocFromFlat(tocEntriesFlat)

            tocEntries.value = tree
            altTocByLineIndex.value = altTocMap
            flatTocEntries.value = allTocs
        } catch (error) {
            console.error('❌ Failed to load TOC data:', error)
            tocEntries.value = []
            altTocByLineIndex.value = new Map()
            flatTocEntries.value = []
        } finally {
            isTocLoading.value = false
        }
    }

    const handleTocSelection = (lineIndex: number) => {
        const viewer: any = lineViewerRef()
        if (viewer?.handleTocSelection) {
            viewer.handleTocSelection(lineIndex)
        } else if (viewer?.scrollToLine) {
            viewer.scrollToLine(lineIndex)
        }
    }

    const handleNavigateLine = (newIndex: number, tocEntryId?: number) => {
        if (myTab.value?.bookState) {
            myTab.value.bookState.selectedLineIndex = newIndex

            if (tocEntryId !== undefined) {
                myTab.value.bookState.selectedTocEntryId = tocEntryId
            }
        }

        const viewer: any = lineViewerRef()
        if (viewer?.scrollToLineIndex) {
            viewer.scrollToLineIndex(newIndex)
        } else if (viewer?.scrollToLine) {
            viewer.scrollToLine(newIndex)
        }
    }

    const handleNavigatePreviousLine = (bookId?: number) => {
        const currentLineIndex = myTab.value?.bookState?.selectedLineIndex
        if (currentLineIndex !== undefined && currentLineIndex > 0) {
            handleNavigateLine(currentLineIndex - 1)

            // Set the selected commentary to the one that emitted the event
            if (bookId !== undefined && myTab.value?.bookState) {
                myTab.value.bookState.commentaryScrollElementIndex = bookId
            }
        }
    }

    const handleNavigateNextLine = (bookId?: number) => {
        const currentLineIndex = myTab.value?.bookState?.selectedLineIndex
        if (currentLineIndex !== undefined) {
            handleNavigateLine(currentLineIndex + 1)

            // Set the selected commentary to the one that emitted the event
            if (bookId !== undefined && myTab.value?.bookState) {
                myTab.value.bookState.commentaryScrollElementIndex = bookId
            }
        }
    }

    const handleBackgroundClick = () => {
        if (myTab.value?.bookState?.isTocOpen && !myTab.value.bookState.isFirstTocOpen) {
            closeToc()
        }
    }

    const handleHighlightOnLoad = () => {
        if (myTab.value?.bookState?.shouldHighlight && myTab.value?.bookState?.initialLineIndex !== undefined) {
            console.log('[BookViewPage] Should highlight line:', myTab.value.bookState.initialLineIndex, 'with terms:', myTab.value?.searchState?.highlightTerms)

            setTimeout(() => {
                const viewer: any = lineViewerRef()
                const highlightTerms = myTab.value?.searchState?.highlightTerms
                const highlightSnippet = myTab.value?.searchState?.highlightSnippet

                console.log('[BookViewPage] Calling scrollToLineWithFadeHighlight with:', {
                    lineIndex: myTab.value!.bookState!.initialLineIndex,
                    highlightTerms,
                    highlightSnippet,
                    viewerExists: !!viewer,
                    methodExists: !!viewer?.scrollToLineWithFadeHighlight
                })

                if (viewer?.scrollToLineWithFadeHighlight) {
                    viewer.scrollToLineWithFadeHighlight(myTab.value!.bookState!.initialLineIndex!, highlightTerms, highlightSnippet)
                } else {
                    console.error('[BookViewPage] scrollToLineWithFadeHighlight not available on viewer')
                }

                if (myTab.value?.bookState) {
                    myTab.value.bookState.shouldHighlight = false
                }
                if (myTab.value?.searchState) {
                    myTab.value.searchState.highlightTerms = undefined
                    myTab.value.searchState.highlightSnippet = undefined
                }
            }, 500)
        } else {
            console.log('[BookViewPage] Not highlighting - shouldHighlight:', myTab.value?.bookState?.shouldHighlight, 'initialLineIndex:', myTab.value?.bookState?.initialLineIndex)
        }
    }

    watch(() => myTab.value?.bookState?.bookId, async (bookId) => {
        if (bookId) {
            await loadTocData(bookId)
            handleHighlightOnLoad()
        }
    }, { immediate: true })

    return {
        myTab,
        toolbarPosition,
        altTocByLineIndex,
        filteredTocEntries,
        flatTocEntries,
        isTocLoading,
        currentCenterLineIndex,
        currentTocEntryId,
        currentBook,
        handleTocSelection,
        handleNavigateLine,
        handleNavigatePreviousLine,
        handleNavigateNextLine,
        handleBackgroundClick
    }
}
