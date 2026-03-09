export interface Book {
    id: number
    categoryId: number
    title: string
    heShortDesc: string | null
    path?: string
    orderIndex: number
    totalLines: number
    hasSourceConnection: number
    hasTargumConnection: number
    hasCommentaryConnection: number
    hasReferenceConnection: number
    hasOtherConnection: number
    defaultCommentatorBookId: number | null
    period?: string // Category period (chronological): תנ"ך, ספרות חז"ל, גאונים, ראשונים, אחרונים, קבלה, מוסר וחסידות, הלכה, אחר
    pubDate?: string | null // Publication date from pub_date table
}

export function hasConnections(book: Book): boolean {
    return book.hasTargumConnection > 0 ||
        book.hasReferenceConnection > 0 ||
        book.hasCommentaryConnection > 0 ||
        book.hasOtherConnection > 0 ||
        book.hasSourceConnection > 0;
}

export interface SearchResult {
    book: Book
    matchType: 'title' | 'description'
}
