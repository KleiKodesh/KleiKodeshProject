/**
 * HebrewBooks CSV Updater (Puppeteer Version)
 * 
 * Uses headless browser to bypass anti-bot protection.
 * Fetches book metadata from hebrewbooks.org and updates the CSV file.
 * Creates a backup before updating and supports incremental updates.
 * 
 * Usage:
 *   npm run update-hebrewbooks
 */

import * as fs from 'fs'
import * as path from 'path'
import type { Page, Browser } from 'puppeteer'
const puppeteer = require('puppeteer')

// Configuration
const CSV_PATH = path.join(__dirname, '../zayit-vue/public/HebrewBooks.csv')
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

// Extract book metadata using Puppeteer
async function fetchBookMetadata(page: Page, bookId: number): Promise<BookMetadata | null> {
    const url = `${BASE_URL}/${bookId}`

    try {
        await page.goto(url, { waitUntil: 'networkidle2', timeout: 30000 })

        // Check if blocked
        const isBlocked = await page.evaluate(() => {
            const h1 = document.querySelector('h1')
            return h1?.textContent?.includes('Sorry, you have been blocked') || false
        })

        if (isBlocked) {
            console.error('IP address blocked by hebrewbooks.org')
            process.exit(1)
        }

        // Extract data from page
        const data = await page.evaluate(() => {
            function getText(id: string): string | null {
                const element = document.getElementById(id)
                return element?.textContent?.trim().replace(/\n/g, ' ') || null
            }

            function getTags(): string[] {
                const tags: string[] = []
                const tagContainer = document.getElementById('cpMstr_pnltag')
                if (tagContainer) {
                    const tagElements = tagContainer.querySelectorAll('.tag')
                    tagElements.forEach(el => {
                        const tag = el.textContent?.trim()
                        if (tag) tags.push(tag)
                    })
                }
                return tags
            }

            return {
                title: getText('cpMstr_lblHebSefername'),
                author: getText('cpMstr_lblHebAuth'),
                printingPlace: getText('cpMstr_lblHebPlace'),
                printingYear: getText('cpMstr_lblHebDate'),
                pages: getText('cpMstr_lblPages'),
                tags: getTags()
            }
        })

        // If no meaningful data, return null
        if (!data.title && !data.author && !data.printingPlace &&
            !data.printingYear && !data.pages && data.tags.length === 0) {
            return null
        }

        return {
            id: bookId,
            title: (data.title || '').replace(/,/g, ' -'),
            author: (data.author || '').replace(/,/g, ' -'),
            printingPlace: data.printingPlace || '',
            printingYear: data.printingYear || '',
            pages: data.pages || '',
            tags: data.tags.join(';')
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

// Append book to CSV
function appendBookToCsv(book: BookMetadata): void {
    const line = `${book.id},${book.title},${book.author},${book.printingPlace},${book.printingYear},${book.pages},${book.tags}\n`
    fs.appendFileSync(CSV_PATH, line, 'utf-8')
}

// Delay helper
function delay(ms: number): Promise<void> {
    return new Promise(resolve => setTimeout(resolve, ms))
}

// Main update function
async function updateHebrewBooks(): Promise<void> {
    console.log('Starting HebrewBooks CSV update...')

    // Create backup
    const backupPath = createBackup()

    // Get starting ID
    const lastId = getMaxIdFromCsv()
    console.log(`Last book ID in CSV: ${lastId}`)
    console.log(`Starting from ID: ${lastId + 1}`)

    let currentId = lastId + 1
    let consecutiveEmpty = 0
    let booksAdded = 0

    // Launch browser
    console.log('Launching browser...')
    const browser = await puppeteer.launch({
        headless: true,
        args: ['--no-sandbox', '--disable-setuid-sandbox']
    })

    try {
        const page = await browser.newPage()

        // Set viewport and user agent
        await page.setViewport({ width: 1920, height: 1080 })
        await page.setUserAgent('Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36')

        while (consecutiveEmpty < MAX_CONSECUTIVE_EMPTY) {
            console.log(`Fetching book ${currentId}...`)

            const book = await fetchBookMetadata(page, currentId)

            if (book) {
                appendBookToCsv(book)
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

        console.log('\n=== Update Complete ===')
        console.log(`Books added: ${booksAdded}`)
        console.log(`Final book ID: ${currentId - 1}`)
        console.log(`CSV file: ${CSV_PATH}`)
        if (backupPath) {
            console.log(`Backup: ${backupPath}`)
        }

    } catch (error) {
        console.error('\n=== Update Failed ===')
        console.error(error)

        if (backupPath && fs.existsSync(backupPath)) {
            console.log('\nRestoring from backup...')
            fs.copyFileSync(backupPath, CSV_PATH)
            console.log('Backup restored successfully')
        }

        throw error

    } finally {
        await browser.close()
    }
}

// Run if executed directly
if (require.main === module) {
    updateHebrewBooks()
        .then(() => process.exit(0))
        .catch((error) => {
            console.error(error)
            process.exit(1)
        })
}

export { updateHebrewBooks, fetchBookMetadata }
