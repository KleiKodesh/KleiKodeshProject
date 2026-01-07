export interface HebrewBook {
    ID_Book: string
    Title: string
    Author: string
    Printing_Place: string
    Printing_Year: string
    Pages: string
    Tags: string
    userScore?: number // Track user interaction score
    lastAccessed?: number // Timestamp of last access
}
