/**
 * Search Highlighting Utilities
 * Pure functions for highlighting search terms and snippets in HTML content
 */

import { removeDiacriticsForSearch, normalizeDashesForSearch, buildPositionMapForSearch } from './hebrewTextProcessing'

/**
 * Highlight global search with snippet background
 */
export function highlightGlobalSearchWithSnippet(htmlContent: string, searchTerms: string, snippet: string): string {
    if (!searchTerms || !htmlContent || htmlContent === '\u00A0') {
        return htmlContent
    }

    // First, find and wrap the snippet region with background span
    let contentWithSnippetBg = htmlContent

    if (snippet) {
        // Clean snippet - remove "..." from beginning and end
        const cleanedSnippet = snippet.replace(/^\.\.\./, '').replace(/\.\.\.$/, '').trim()

        if (cleanedSnippet) {
            contentWithSnippetBg = findAndWrapSnippet(htmlContent, cleanedSnippet)
        }
    }

    // Then apply individual word highlighting on top
    return highlightGlobalSearchTerms(contentWithSnippetBg, searchTerms)
}

/**
 * Find snippet in content and wrap it with background span
 */
function findAndWrapSnippet(htmlContent: string, snippet: string): string {
    const tempDiv = document.createElement('div')
    tempDiv.innerHTML = htmlContent

    // Get all text content
    const fullText = tempDiv.textContent || ''

    // Normalize both for matching
    const normalizedFullText = removeDiacriticsForSearch(fullText.toLowerCase())
    const normalizedSnippet = removeDiacriticsForSearch(snippet.toLowerCase())

    // Find best match position
    const matchPos = normalizedFullText.indexOf(normalizedSnippet)

    if (matchPos === -1) {
        // Snippet not found exactly - try word-by-word proximity matching
        return wrapBestProximityMatch(htmlContent, snippet)
    }

    // Build position map to find original positions
    const positionMap = buildPositionMapForSearch(fullText)
    const snippetStart = positionMap[matchPos] ?? 0
    const snippetEnd = positionMap[matchPos + normalizedSnippet.length] ?? fullText.length

    // Wrap the snippet region
    return wrapTextRange(htmlContent, snippetStart, snippetEnd)
}

/**
 * Wrap a text range with background span
 */
function wrapTextRange(htmlContent: string, start: number, end: number): string {
    const tempDiv = document.createElement('div')
    tempDiv.innerHTML = htmlContent

    const walker = document.createTreeWalker(
        tempDiv,
        NodeFilter.SHOW_TEXT,
        null
    )

    const textNodes: Text[] = []
    let node: Node | null
    while ((node = walker.nextNode())) {
        textNodes.push(node as Text)
    }

    let currentPos = 0
    const nodesToWrap: Array<{ node: Text; startOffset: number; endOffset: number }> = []

    textNodes.forEach(textNode => {
        const text = textNode.nodeValue || ''
        const nodeStart = currentPos
        const nodeEnd = currentPos + text.length

        // Check if this node overlaps with the range
        if (nodeEnd > start && nodeStart < end) {
            const startOffset = Math.max(0, start - nodeStart)
            const endOffset = Math.min(text.length, end - nodeStart)
            nodesToWrap.push({ node: textNode, startOffset, endOffset })
        }

        currentPos = nodeEnd
    })

    // Wrap the nodes
    nodesToWrap.forEach(({ node, startOffset, endOffset }) => {
        const text = node.nodeValue || ''
        const fragment = document.createDocumentFragment()

        // Before
        if (startOffset > 0) {
            fragment.appendChild(document.createTextNode(text.substring(0, startOffset)))
        }

        // Wrapped part
        const span = document.createElement('span')
        span.className = 'global-search-snippet-bg'
        span.textContent = text.substring(startOffset, endOffset)
        fragment.appendChild(span)

        // After
        if (endOffset < text.length) {
            fragment.appendChild(document.createTextNode(text.substring(endOffset)))
        }

        node.parentNode?.replaceChild(fragment, node)
    })

    return tempDiv.innerHTML
}

/**
 * Find best proximity match when exact snippet not found
 */
function wrapBestProximityMatch(htmlContent: string, snippet: string): string {
    // Split snippet into words
    const snippetWords = snippet.trim().split(/\s+/).filter(w => w.length > 0)
    if (snippetWords.length === 0) return htmlContent

    const tempDiv = document.createElement('div')
    tempDiv.innerHTML = htmlContent
    const fullText = tempDiv.textContent || ''
    const normalizedFullText = removeDiacriticsForSearch(fullText.toLowerCase())

    // Find all word positions
    const wordPositions: Array<{ word: string; start: number; end: number }> = []

    snippetWords.forEach(word => {
        const normalizedWord = removeDiacriticsForSearch(word.toLowerCase())
        let searchStart = 0

        while (true) {
            const foundAt = normalizedFullText.indexOf(normalizedWord, searchStart)
            if (foundAt === -1) break

            const positionMap = buildPositionMapForSearch(fullText)
            const originalStart = positionMap[foundAt] ?? 0
            const originalEnd = positionMap[foundAt + normalizedWord.length] ?? fullText.length

            wordPositions.push({ word, start: originalStart, end: originalEnd })
            searchStart = foundAt + 1
        }
    })

    if (wordPositions.length === 0) return htmlContent

    // Find the cluster with most words in closest proximity
    let bestStart = wordPositions[0]!.start
    let bestEnd = wordPositions[0]!.end
    let bestScore = 1

    for (let i = 0; i < wordPositions.length; i++) {
        const clusterStart = wordPositions[i]!.start
        let clusterEnd = wordPositions[i]!.end
        let wordsInCluster = 1

        // Expand cluster to include nearby words (within 200 chars)
        for (let j = 0; j < wordPositions.length; j++) {
            if (i !== j && wordPositions[j]!.start >= clusterStart && wordPositions[j]!.start <= clusterStart + 200) {
                clusterEnd = Math.max(clusterEnd, wordPositions[j]!.end)
                wordsInCluster++
            }
        }

        // Score: more words = better, shorter span = better
        const span = clusterEnd - clusterStart
        const score = wordsInCluster * 1000 - span

        if (score > bestScore) {
            bestScore = score
            bestStart = clusterStart
            bestEnd = clusterEnd
        }
    }

    return wrapTextRange(htmlContent, bestStart, bestEnd)
}

/**
 * Highlight global search terms (foreground color, not background)
 */
export function highlightGlobalSearchTerms(htmlContent: string, searchTerms: string): string {
    if (!searchTerms || !htmlContent || htmlContent === '\u00A0') {
        return htmlContent
    }

    const tempDiv = document.createElement('div')
    tempDiv.innerHTML = htmlContent

    const walker = document.createTreeWalker(
        tempDiv,
        NodeFilter.SHOW_TEXT,
        null
    )

    const textNodes: Text[] = []
    let node: Node | null
    while ((node = walker.nextNode())) {
        textNodes.push(node as Text)
    }

    // Split search terms into individual words
    const terms = searchTerms.trim().split(/\s+/).filter(term => term.length > 0)

    textNodes.forEach(textNode => {
        const text = textNode.nodeValue || ''

        // Collect all matches from all terms with their positions
        const allMatches: Array<{ start: number; end: number }> = []

        terms.forEach(term => {
            // Check if term contains spaces or dashes
            const termHasSpacesOrDashes = /[\s\u002D\u2013\u2014\u05BE]/.test(term)

            // Normalize term - dashes BEFORE diacritics removal
            let normalizedTerm = term.toLowerCase()
            if (termHasSpacesOrDashes) {
                normalizedTerm = normalizeDashesForSearch(normalizedTerm)
            }
            normalizedTerm = removeDiacriticsForSearch(normalizedTerm)

            // Normalize text the same way
            let normalizedText = text.toLowerCase()
            if (termHasSpacesOrDashes) {
                normalizedText = normalizeDashesForSearch(normalizedText)
            }
            normalizedText = removeDiacriticsForSearch(normalizedText)

            // Build position map for diacritics
            const positionMap = buildPositionMapForSearch(text)

            let searchStart = 0
            while (true) {
                const foundAt = normalizedText.indexOf(normalizedTerm, searchStart)
                if (foundAt === -1) break

                // Map back to original positions
                const originalStart = positionMap[foundAt] ?? 0
                const originalEnd = positionMap[foundAt + normalizedTerm.length] ?? text.length

                allMatches.push({ start: originalStart, end: originalEnd })
                searchStart = foundAt + 1
            }
        })

        // Sort matches by start position and merge overlapping ranges
        allMatches.sort((a, b) => a.start - b.start)
        const mergedMatches: Array<{ start: number; end: number }> = []

        allMatches.forEach(match => {
            if (mergedMatches.length === 0) {
                mergedMatches.push(match)
            } else {
                const last = mergedMatches[mergedMatches.length - 1]
                if (last && match.start <= last.end) {
                    // Overlapping or adjacent - merge
                    last.end = Math.max(last.end, match.end)
                } else {
                    mergedMatches.push(match)
                }
            }
        })

        // Apply highlighting to merged matches
        if (mergedMatches.length > 0) {
            const fragment = document.createDocumentFragment()
            let lastEnd = 0

            mergedMatches.forEach(match => {
                // Add text before match
                if (match.start > lastEnd) {
                    fragment.appendChild(document.createTextNode(text.substring(lastEnd, match.start)))
                }

                // Add highlighted match
                const span = document.createElement('span')
                span.className = 'global-search-highlight'
                span.textContent = text.substring(match.start, match.end)
                fragment.appendChild(span)

                lastEnd = match.end
            })

            // Add remaining text
            if (lastEnd < text.length) {
                fragment.appendChild(document.createTextNode(text.substring(lastEnd)))
            }

            textNode.parentNode?.replaceChild(fragment, textNode)
        }
    })

    return tempDiv.innerHTML
}
