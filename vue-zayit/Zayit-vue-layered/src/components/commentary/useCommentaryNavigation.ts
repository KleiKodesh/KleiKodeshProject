/**
 * Commentary Navigation Composable
 * Handles navigation between lines with commentary
 */

import { ref } from 'vue'
import { bookCommentaryService } from '@/data/services/bookCommentaryService'
import { dbService } from '@/data/services/dbService'
import type { TocEntry } from '@/data/types/BookToc'

export function useCommentaryNavigation() {
    const isNavigatingToLine = ref(false)

    /**
     * Find next line with commentary for current commentary book
     */
    async function findNextLineWithCommentary(
        bookId: number,
        startLine: number,
        currentCommentaryBookId: number,
        tabId: string,
        connectionTypeId: number | undefined,
        flatTocEntries: TocEntry[],
        selectedTocEntryId: number | undefined,
        maxScanLines = 50
    ): Promise<{ lineIndex: number; tocEntryId?: number } | null> {
        const isInTocMode = selectedTocEntryId !== undefined

        if (isInTocMode && flatTocEntries.length > 0) {
            const currentTocIndex = flatTocEntries.findIndex(
                toc => toc.lineIndex === startLine && !toc.isAltToc
            )

            const startIndex = currentTocIndex >= 0 ? currentTocIndex + 1 : 0

            for (let i = startIndex; i < flatTocEntries.length; i++) {
                const tocEntry = flatTocEntries[i]
                if (!tocEntry || tocEntry.isAltToc || tocEntry.lineIndex === undefined) continue

                try {
                    const lineIdResults = await dbService.getLineIdsByTocEntry(bookId, tocEntry.id)
                    const lineIds = lineIdResults.map(r => r.lineId)

                    if (lineIds.length > 0) {
                        const linksPromises = lineIds.map(lineId =>
                            dbService.getLinks(lineId, tabId, bookId, connectionTypeId)
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
                        bookId,
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

    /**
     * Find previous line with commentary for current commentary book
     */
    async function findPreviousLineWithCommentary(
        bookId: number,
        startLine: number,
        currentCommentaryBookId: number,
        tabId: string,
        connectionTypeId: number | undefined,
        flatTocEntries: TocEntry[],
        selectedTocEntryId: number | undefined,
        maxScanLines = 50
    ): Promise<{ lineIndex: number; tocEntryId?: number } | null> {
        const isInTocMode = selectedTocEntryId !== undefined

        if (isInTocMode && flatTocEntries.length > 0) {
            const currentTocIndex = flatTocEntries.findIndex(
                toc => toc.lineIndex === startLine && !toc.isAltToc
            )

            const startIndex = currentTocIndex > 0 ? currentTocIndex - 1 : flatTocEntries.length - 1

            for (let i = startIndex; i >= 0; i--) {
                const tocEntry = flatTocEntries[i]
                if (!tocEntry || tocEntry.isAltToc || tocEntry.lineIndex === undefined) continue

                try {
                    const lineIdResults = await dbService.getLineIdsByTocEntry(bookId, tocEntry.id)
                    const lineIds = lineIdResults.map(r => r.lineId)

                    if (lineIds.length > 0) {
                        const linksPromises = lineIds.map(lineId =>
                            dbService.getLinks(lineId, tabId, bookId, connectionTypeId)
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
                        bookId,
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

    return {
        isNavigatingToLine,
        findNextLineWithCommentary,
        findPreviousLineWithCommentary
    }
}
