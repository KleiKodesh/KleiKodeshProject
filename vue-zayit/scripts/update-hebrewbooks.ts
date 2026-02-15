/**
 * HebrewBooks CSV Updater
 * 
 * Fetches book metadata from hebrewbooks.org and updates the CSV file.
 * Creates a backup before updating and supports incremental updates.
 * 
 * Usage:
 *   npm run update-hebrewbooks
 */

import * as fs from 'fs'
import * as path from 'path'
import * as https from 'https'
import * as http from 'http'

// Configuration
const CSV_PATH = path.join(__dirname, '../zayit-vue/public/HebrewBooks.csv')
const NEW_DATA_PATH = path.join(__dirname, '../zayit-vue/public/HebrewBooks_new.csv')
const BACKUP_DIR = path.join(__dirname, '../backups')
const BASE_URL = 'https://beta.hebrewbooks.org'
const MAX_CONSECUTIVE_EMPTY = 10
const REQUEST_DELAY_MS = 1000 // Delay between requests to be respectful

interface BookMetadata {
    id: number
    title: string
    author: string
    printingPlace: string
    printingYear: string
    pages: string
    tags: string
}

// Simple HTML parser (no external dependencies)
function extractText(html: string, spanId: string): string | null {
    const regex = new RegExp(`<span[^>]*id=['"]${spanId}['"][^>]*>([^<]*)</span>`, 'i')
    const match = html.match(regex)
    return match ? match[1].trim().replace(/\n/g, ' ') : null
}

function extractTags(html: string): string[] {
    const tags: string[] = []
    const tagRegex = /<div[^>]*id=['"]cpMstr_pnltag['"][^>]*>([\s\S]*?)<\/div>/i
    const tagContainerMatch = html.match(tagRegex)

    if (tagContainerMatch) {
        const tagContent = tagContainerMatch[1]
        const individualTagRegex = /<span[^>]*class=['"][^'"]*tag[^'"]*['"][^>]*>([^<]*)<\/span>/gi
        let tagMatch
        while ((tagMatch = individualTagRegex.exec(tagContent)) !== null) {
            const tag = tagMatch[1].trim()
            if (tag) tags.push(tag)
        }
    }

    return tags
}

// HTTP(S) request helper
function fetchUrl(url: string): Promise<string> {
    return new Promise((resolve, reject) => {
        const protocol = url.startsWith('https') ? https : http

        protocol.get(url, {
            headers: {
                'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36'
            }
        }, (res) => {
            if (res.statusCode !== 200) {
                reject(new Error(`HTTP ${res.statusCode}`))
                return
            }

            let data = ''
            res.on('data', chunk => data += chunk)
            res.on('end', () => resolve(data))
        }).on('error', reject)
    })
}

// Extract book metadata from HTML
async function fetchBookMetadata(bookId: number): Promise<BookMetadata | null> {
    const url = `${BASE_URL}/${bookId}`

    try {
        const html = await fetchUrl(url)

        // Check if blocked
        if (html.includes('Sorry, you have been blocked')) {
            console.error('IP address blocked by hebrewbooks.org')
            process.exit(1)
        }

        const title = extractText(html, 'cpMstr_lblHebSefername')
        const author = extractText(html, 'cpMstr_lblHebAuth')
        const printingPlace = extractText(html, 'cpMstr_lblHebPlace')
        const printingYear = extractText(html, 'cpMstr_lblHebDate')
        const pages = extractText(html, 'cpMstr_lblPages')
        const tags = extractTags(html)

        // If no meaningful data, return null
        if (!title && !author && !printingPlace && !printingYear && !pages && tags.length === 0) {
            return null
        }

        return {
            id: bookId,
            title: (title || '').replace(/,/g, ' -'),
            author: (author || '').replace(/,/g, ' -'),
            printingPlace: printingPlace || '',
            printingYear: printingYear || '',
            pages: pages || '',
            tags: tags.join(';')
        }
    } catch (error) {
        console.error(`Error fetching book ${bookId}:`, error)
        return null
    }
}

// Read existing CSV and get max ID
function getMaxIdFromCsv(): number {
    if (!fs.existsSync(CSV_PATH)) {
        return 0
    }

    const content = fs.readFileSync(CSV_PATH, 'utf-8')
    const lines = content.trim().split('\n')

    let maxId = 0
    for (const line of lines) {
        const id = parseInt(line.split(',')[0])
        if (!isNaN(id) && id > maxId) {
            maxId = id
        }
    }

    return maxId
}

// Create backup
function createBackup(): string {
    if (!fs.existsSync(CSV_PATH)) {
        console.log('No existing CSV file to backup')
        return ''
    }

    if (!fs.existsSync(BACKUP_DIR)) {
        fs.mkdirSync(BACKUP_DIR, { recursive: true })
    }

    const timestamp = new Date().toISOString().replace(/[:.]/g, '-').slice(0, -5)
    const backupPath = path.join(BACKUP_DIR, `HebrewBooks_${timestamp}.csv`)

    fs.copyFileSync(CSV_PATH, backupPath)
    console.log(`Backup created: ${backupPath}`)

    return backupPath
}

// Append book to new data file
function appendBookToNewData(book: BookMetadata): void {
    const line = `${book.id},${book.title},${book.author},${book.printingPlace},${book.printingYear},${book.pages},${book.tags}\n`
    fs.appendFileSync(NEW_DATA_PATH, line, 'utf-8')
}

// Merge new data with original CSV
function mergeNewDataWithOriginal(): void {
    if (!fs.existsSync(NEW_DATA_PATH)) {
        console.log('No new data to merge')
        return
    }

    let originalContent = fs.readFileSync(CSV_PATH, 'utf-8')
    const newContent = fs.readFileSync(NEW_DATA_PATH, 'utf-8')

    // Ensure original content ends with newline before appending
    if (originalContent.length > 0 && !originalContent.endsWith('\n')) {
        originalContent += '\n'
    }

    // Append new data to original
    fs.writeFileSync(CSV_PATH, originalContent + newContent, 'utf-8')

    // Remove the temporary new data file
    fs.unlinkSync(NEW_DATA_PATH)
    console.log('New data merged with original CSV')
}

// Delay helper
function delay(ms: number): Promise<void> {
    return new Promise(resolve => setTimeout(resolve, ms))
}

// Main update function
async function updateHebrewBooks(): Promise<void> {
    console.log('Starting HebrewBooks CSV update...')
    console.log('Original data will be preserved - new entries written to temporary file')

    // Create backup
    const backupPath = createBackup()

    // Clean up any existing new data file from previous run
    if (fs.existsSync(NEW_DATA_PATH)) {
        fs.unlinkSync(NEW_DATA_PATH)
        console.log('Cleaned up previous temporary file')
    }

    // Get starting ID
    const lastId = getMaxIdFromCsv()
    console.log(`Last book ID in CSV: ${lastId}`)
    console.log(`Starting from ID: ${lastId + 1}`)

    let currentId = lastId + 1
    let consecutiveEmpty = 0
    let booksAdded = 0

    try {
        while (consecutiveEmpty < MAX_CONSECUTIVE_EMPTY) {
            console.log(`Fetching book ${currentId}...`)

            const book = await fetchBookMetadata(currentId)

            if (book) {
                appendBookToNewData(book)
                booksAdded++
                consecutiveEmpty = 0
                console.log(`✓ Added: ${book.title} (ID: ${book.id})`)
            } else {
                consecutiveEmpty++
                console.log(`✗ No data for book ${currentId} (${consecutiveEmpty}/${MAX_CONSECUTIVE_EMPTY})`)
            }

            currentId++

            // Be respectful - delay between requests
            await delay(REQUEST_DELAY_MS)
        }

        console.log('\n=== Fetching Complete ===')
        console.log(`Books fetched: ${booksAdded}`)
        console.log(`Final book ID: ${currentId - 1}`)

        // Merge new data with original
        if (booksAdded > 0) {
            console.log('\nMerging new data with original CSV...')
            mergeNewDataWithOriginal()
            console.log('\n=== Update Complete ===')
            console.log(`CSV file updated: ${CSV_PATH}`)
            console.log(`Total books added: ${booksAdded}`)
        } else {
            console.log('\nNo new books to add')
        }

        if (backupPath) {
            console.log(`Backup: ${backupPath}`)
        }

    } catch (error) {
        console.error('\n=== Update Failed ===')
        console.error(error)

        // Clean up temporary file
        if (fs.existsSync(NEW_DATA_PATH)) {
            fs.unlinkSync(NEW_DATA_PATH)
            console.log('Temporary file cleaned up')
        }

        // Note: Original CSV is never modified until merge, so no restore needed
        console.log('Original CSV file was not modified')

        process.exit(1)
    }
}

// Run if executed directly
if (require.main === module) {
    updateHebrewBooks().catch(console.error)
}

export { updateHebrewBooks, fetchBookMetadata }
