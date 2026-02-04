export interface HebrewBook {
    id: string
    title: string
    author: string
    printingPlace: string
    printingYear: string
    pages: string
    userScore?: number // Track user interaction score
    lastAccessed?: number // Timestamp of last access
    _csvTags?: string // Internal: raw CSV tags for search/display only
}
