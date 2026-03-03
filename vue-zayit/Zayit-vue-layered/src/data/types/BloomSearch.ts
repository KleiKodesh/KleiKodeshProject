export interface BloomSearchResult {
    lineId: number
    bookId: number
    bookTitle: string
    tocText: string
    score: number
    proximityScore: number
    snippet: string
}

export interface IndexingProgress {
    isReady: boolean
    isIndexing: boolean
    processedChunks?: number
    totalChunks?: number
    percentage?: number
    eta?: string
}
