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

    const loadTocData = async (bookId: number) => {
        isTocLoading.value = true
        try {
            const { tocEntriesFlat } = await dbService.getToc(bookId)

            // Use bookTitle from tab state (always available) instead of currentBook (may not be loaded yet)
            const bookTitle = myTab.value?.bookState?.bookTitle || currentBook.value?.title
            console.log('[useBookView] Loading TOC for book:', bookId)
            console.log('[useBookView] Book title:', bookTitle)
            console.log('[useBookView] TOC entries count:', tocEntriesFlat.length)

            const { tree, allTocs, altTocByLineIndex: altTocMap } = buildTocFromFlat(tocEntriesFlat, bookTitle)

            tocEntries.value = tree
            altTocByLineIndex.value = altTocMap
            flatTocEntries.value = allTocs
            console.log('[useBookView] TOC loaded - tree count:', tree.length, 'allTocs count:', allTocs.length)
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

        // Update tab title when selecting TOC item
        if (myTab.value && flatTocEntries.value.length) {
            let closestTocEntry: TocEntry | undefined
            for (const entry of flatTocEntries.value) {
                if (entry.isAltToc) continue
                if (entry.lineIndex <= lineIndex) {
                    if (!closestTocEntry || entry.lineIndex > closestTocEntry.lineIndex) {
                        closestTocEntry = entry
                    }
                }
            }

            if (closestTocEntry) {
                const bookTitle = currentBook.value?.title
                if (bookTitle) {
                    const fullTocPath = closestTocEntry.path
                        ? `${closestTocEntry.path} - ${closestTocEntry.text}`
                        : closestTocEntry.text
                    myTab.value.title = fullTocPath ? `${bookTitle} - ${fullTocPath}` : bookTitle
                }
            }
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

    const handleNavigatePreviousLine = async (bookId?: number) => {
        const currentLineIndex = myTab.value?.bookState?.selectedLineIndex
        const sourceBookId = myTab.value?.bookState?.bookId
        const connectionTypeId = myTab.value?.bookState?.commentaryFilterConnectionTypeId
        const selectedTocEntryId = myTab.value?.bookState?.selectedTocEntryId

        if (currentLineIndex !== undefined && sourceBookId !== undefined && bookId !== undefined) {
            // Check if we're in TOC mode
            const isInTocMode = selectedTocEntryId !== undefined

            if (isInTocMode && flatTocEntries.value.length > 0) {
                // TOC mode: Navigate to previous TOC entry with this commentary
                const currentTocIndex = flatTocEntries.value.findIndex(
                    toc => toc.lineIndex === currentLineIndex && !toc.isAltToc
                )

                const startIndex = currentTocIndex > 0 ? currentTocIndex - 1 : flatTocEntries.value.length - 1

                for (let i = startIndex; i >= 0; i--) {
                    const tocEntry = flatTocEntries.value[i]
                    if (!tocEntry || tocEntry.isAltToc || tocEntry.lineIndex === undefined) continue

                    try {
                        const lineIdResults = await dbService.getLineIdsByTocEntry(sourceBookId, tocEntry.id)
                        const lineIds = lineIdResults.map(r => r.lineId)

                        if (lineIds.length > 0) {
                            const linksPromises = lineIds.map(lineId =>
                                dbService.getLinks(lineId, '', sourceBookId, connectionTypeId)
                            )
                            const allLinksArrays = await Promise.all(linksPromises)
                            const allLinks = allLinksArrays.flat()

                            const hasCommentary = allLinks.some(link => link.targetBookId === bookId)
                            if (hasCommentary) {
                                handleNavigateLine(tocEntry.lineIndex, tocEntry.id)

                                // Set the selected commentary to the one that emitted the event
                                if (myTab.value?.bookState) {
                                    myTab.value.bookState.currentCommentaryBookId = bookId
                                }
                                return
                            }
                        }
                    } catch (error) {
                        continue
                    }
                }
            } else {
                // Line mode: Find previous line with this specific commentary
                const previousLineIndex = await dbService.findPreviousLineWithCommentary(
                    sourceBookId,
                    currentLineIndex,
                    bookId,
                    connectionTypeId
                )

                if (previousLineIndex !== null) {
                    handleNavigateLine(previousLineIndex)

                    // Set the selected commentary to the one that emitted the event
                    if (myTab.value?.bookState) {
                        myTab.value.bookState.currentCommentaryBookId = bookId
                    }
                }
            }
        }
    }

    const handleNavigateNextLine = async (bookId?: number) => {
        const currentLineIndex = myTab.value?.bookState?.selectedLineIndex
        const sourceBookId = myTab.value?.bookState?.bookId
        const connectionTypeId = myTab.value?.bookState?.commentaryFilterConnectionTypeId
        const selectedTocEntryId = myTab.value?.bookState?.selectedTocEntryId

        if (currentLineIndex !== undefined && sourceBookId !== undefined && bookId !== undefined) {
            // Check if we're in TOC mode
            const isInTocMode = selectedTocEntryId !== undefined

            if (isInTocMode && flatTocEntries.value.length > 0) {
                // TOC mode: Navigate to next TOC entry with this commentary
                const currentTocIndex = flatTocEntries.value.findIndex(
                    toc => toc.lineIndex === currentLineIndex && !toc.isAltToc
                )

                const startIndex = currentTocIndex >= 0 ? currentTocIndex + 1 : 0

                for (let i = startIndex; i < flatTocEntries.value.length; i++) {
                    const tocEntry = flatTocEntries.value[i]
                    if (!tocEntry || tocEntry.isAltToc || tocEntry.lineIndex === undefined) continue

                    try {
                        const lineIdResults = await dbService.getLineIdsByTocEntry(sourceBookId, tocEntry.id)
                        const lineIds = lineIdResults.map(r => r.lineId)

                        if (lineIds.length > 0) {
                            const linksPromises = lineIds.map(lineId =>
                                dbService.getLinks(lineId, '', sourceBookId, connectionTypeId)
                            )
                            const allLinksArrays = await Promise.all(linksPromises)
                            const allLinks = allLinksArrays.flat()

                            const hasCommentary = allLinks.some(link => link.targetBookId === bookId)
                            if (hasCommentary) {
                                handleNavigateLine(tocEntry.lineIndex, tocEntry.id)

                                // Set the selected commentary to the one that emitted the event
                                if (myTab.value?.bookState) {
                                    myTab.value.bookState.currentCommentaryBookId = bookId
                                }
                                return
                            }
                        }
                    } catch (error) {
                        continue
                    }
                }
            } else {
                // Line mode: Find next line with this specific commentary
                const nextLineIndex = await dbService.findNextLineWithCommentary(
                    sourceBookId,
                    currentLineIndex,
                    bookId,
                    connectionTypeId
                )

                if (nextLineIndex !== null) {
                    handleNavigateLine(nextLineIndex)

                    // Set the selected commentary to the one that emitted the event
                    if (myTab.value?.bookState) {
                        myTab.value.bookState.currentCommentaryBookId = bookId
                    }
                }
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

    // Update tab title and TOC selection based on center line
    watch(currentCenterLineIndex, (lineIndex) => {
        if (lineIndex === null || !flatTocEntries.value.length || !myTab.value) return

        // Find the closest TOC entry at or before this line (excluding alt TOC)
        let closestTocEntry: TocEntry | undefined
        for (const entry of flatTocEntries.value) {
            if (entry.isAltToc) continue
            if (entry.lineIndex <= lineIndex) {
                if (!closestTocEntry || entry.lineIndex > closestTocEntry.lineIndex) {
                    closestTocEntry = entry
                }
            }
        }

        if (closestTocEntry) {
            // Update current TOC entry ID for tree selection
            currentTocEntryId.value = closestTocEntry.id

            // Update tab title with full TOC path only when TOC is not open and book title is available
            const bookTitle = currentBook.value?.title
            if (!myTab.value.bookState?.isTocOpen && bookTitle) {
                const fullTocPath = closestTocEntry.path
                    ? `${closestTocEntry.path} - ${closestTocEntry.text}`
                    : closestTocEntry.text
                myTab.value.title = fullTocPath ? `${bookTitle} - ${fullTocPath}` : bookTitle
            }
        }
    })

    return {
        myTab,
        toolbarPosition,
        altTocByLineIndex,
        tocEntries,
        flatTocEntries,
        isTocLoading,
        currentCenterLineIndex,
        currentTocEntryId,
        currentBook,
        book: currentBook, // Alias for toolbar
        handleTocSelection,
        handleNavigateLine,
        handleNavigatePreviousLine,
        handleNavigateNextLine,
        handleBackgroundClick
    }
}
