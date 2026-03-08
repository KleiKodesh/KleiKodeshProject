/**
 * Unified Text Transformations Utility
 * Applies transformations in consistent order: diacritics → search
 * Used by both LineView and CommentaryView
 * 
 * Note: Shem Hashem censoring is currently applied at the data layer (dbService)
 * and requires page reload, so it's not included here.
 */

import { applyDiacriticsFilter } from '@/utils/hebrewTextProcessing'

export interface TransformOptions {
    diacriticsState?: number
    searchQuery?: string
    isCurrentSearchMatch?: boolean
    currentMatchIndex?: number // Which match within the line is current (0-based)
}

/**
 * Apply all text transformations in the correct order
 * @param html - Original HTML content
 * @param options - Transformation options
 * @returns Transformed HTML content
 */
export function transformText(html: string, options: TransformOptions): string {
    // Skip transformations for empty content
    if (!html || html === '\u00A0') {
        return html
    }

    let result = html

    // 1. Apply diacritics filter (state 0 = full, 1 = no cantillation, 2 = no nikkud)
    if (options.diacriticsState && options.diacriticsState > 0) {
        result = applyDiacriticsFilter(result, options.diacriticsState)
    }

    // 2. Apply search highlighting
    if (options.searchQuery && options.searchQuery.trim().length >= 2) {
        result = highlightSearchMatches(
            result,
            options.searchQuery,
            options.isCurrentSearchMatch || false,
            options.currentMatchIndex
        )
    }

    return result
}

/**
 * Highlight search matches in HTML content
 * Handles Hebrew text with diacritics and HTML tags
 */
function highlightSearchMatches(html: string, query: string, isCurrent: boolean, currentMatchIndex?: number): string {
    // Remove diacritics from query for matching
    const normalizedQuery = removeDiacritics(query.toLowerCase())

    // Parse HTML to work with text nodes
    const tempDiv = document.createElement('div')
    tempDiv.innerHTML = html

    // Collect all text content and build a map of positions
    const walker = document.createTreeWalker(tempDiv, NodeFilter.SHOW_TEXT, null)
    const textNodes: Text[] = []
    let node: Node | null
    while ((node = walker.nextNode())) {
        textNodes.push(node as Text)
    }

    // Build combined text from all text nodes
    let combinedText = ''
    const nodePositions: Array<{ node: Text; start: number; end: number; originalText: string }> = []

    textNodes.forEach(textNode => {
        const text = textNode.textContent || ''
        const start = combinedText.length
        combinedText += text
        const end = combinedText.length
        nodePositions.push({ node: textNode, start, end, originalText: text })
    })

    // Normalize combined text for searching
    const normalizedCombined = removeDiacritics(combinedText.toLowerCase())

    // Find all matches in combined text
    const matches: Array<{ start: number; end: number; matchIndex: number }> = []
    let searchStart = 0
    let matchCount = 0

    while (true) {
        const matchIndex = normalizedCombined.indexOf(normalizedQuery, searchStart)
        if (matchIndex === -1) break

        // Check word boundaries
        const charBefore = matchIndex > 0 ? normalizedCombined[matchIndex - 1] : null
        const charAfter = matchIndex + normalizedQuery.length < normalizedCombined.length
            ? normalizedCombined[matchIndex + normalizedQuery.length]
            : null

        const isWordBoundaryBefore = !charBefore || /[\s־׳״׃.,;:!?()[\]{}\-–—]/.test(charBefore)
        const isWordBoundaryAfter = !charAfter || /[\s־׳״׃.,;:!?()[\]{}\-–—]/.test(charAfter)

        if (isWordBoundaryBefore && isWordBoundaryAfter) {
            // Map back to original position (accounting for diacritics)
            const originalStart = mapToOriginalPosition(combinedText, matchIndex)
            const originalEnd = mapToOriginalPosition(combinedText, matchIndex + normalizedQuery.length)
            matches.push({ start: originalStart, end: originalEnd, matchIndex: matchCount })
            matchCount++
        }

        searchStart = matchIndex + 1
    }

    // Apply highlights to text nodes
    matches.forEach(match => {
        // Find which text nodes this match spans
        nodePositions.forEach(nodePos => {
            const matchStart = Math.max(match.start, nodePos.start)
            const matchEnd = Math.min(match.end, nodePos.end)

            // Check if this match overlaps with this text node
            if (matchStart < matchEnd) {
                const localStart = matchStart - nodePos.start
                const localEnd = matchEnd - nodePos.start
                const text = nodePos.originalText

                // Build highlighted text for this node
                const before = text.substring(0, localStart)
                const matchText = text.substring(localStart, localEnd)
                const after = text.substring(localEnd)

                // Determine if this is the current match
                const isThisMatchCurrent = isCurrent && currentMatchIndex !== undefined && match.matchIndex === currentMatchIndex
                const className = isThisMatchCurrent ? 'current' : ''
                const classAttr = className ? ` class="${className}"` : ''

                // Replace text node with highlighted HTML
                const span = document.createElement('span')
                span.innerHTML = before + `<mark${classAttr}>${matchText}</mark>` + after
                nodePos.node.replaceWith(span)
            }
        })
    })

    return tempDiv.innerHTML
}

/**
 * Remove Hebrew diacritics for matching
 */
function removeDiacritics(text: string): string {
    return text.replace(/[\u0591-\u05C7]/g, '')
}

/**
 * Map normalized position back to original position (accounting for diacritics)
 */
function mapToOriginalPosition(text: string, normalizedPos: number): number {
    let originalPos = 0
    let normalizedCount = 0

    for (let i = 0; i < text.length; i++) {
        const char = text[i]!
        const isDiacritic = /[\u0591-\u05C7]/.test(char)

        if (!isDiacritic) {
            if (normalizedCount === normalizedPos) {
                return originalPos
            }
            normalizedCount++
        }
        originalPos++
    }

    return originalPos
}
