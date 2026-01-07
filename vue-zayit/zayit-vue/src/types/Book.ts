export interface Book {
    id: number
    categoryId: number
    title: string
    heShortDesc: string | null
    path?: string
    orderIndex: number
    totalLines: number
    hasTargumConnection: number
    hasReferenceConnection: number
    hasCommentaryConnection: number
    hasOtherConnection: number
}

export function hasConnections(book: Book): boolean {
    return book.hasTargumConnection > 0 ||
        book.hasReferenceConnection > 0 ||
        book.hasCommentaryConnection > 0 ||
        book.hasOtherConnection > 0;
}

export interface SearchResult {
    book: Book
    matchType: 'title' | 'description'
}
