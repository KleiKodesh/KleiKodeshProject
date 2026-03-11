/**
 * Hebrew Text Processing Utilities
 * Pure functions for processing Hebrew text - diacritics removal
 */

/**
 * Apply diacritics filtering to HTML content
 * State 0: Full diacritics (nikkud + cantillation)
 * State 1: Remove cantillations only
 * State 2: Remove nikkud as well
 */
export function applyDiacriticsFilter(htmlContent: string, state: number): string {
    if (state === 0 || !htmlContent || htmlContent === '\u00A0') {
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

    textNodes.forEach(textNode => {
        let text = textNode.nodeValue || ''

        // State 1: Remove cantillations only (U+0591-U+05AF)
        if (state >= 1) {
            text = text.replace(/[\u0591-\u05AF]/g, '')
        }

        // State 2: Remove nikkud as well (U+05B0-U+05BD, U+05C1, U+05C2, U+05C4, U+05C5)
        if (state >= 2) {
            text = text.replace(/[\u05B0-\u05BD\u05C1\u05C2\u05C4\u05C5]/g, '')
            // Replace ? and ! with . and remove em dash (—)
            text = text.replace(/[?!]/g, '.').replace(/—/g, '')
        }

        textNode.nodeValue = text
    })

    return tempDiv.innerHTML
}

/**
 * Remove diacritics for search matching
 */
export function removeDiacriticsForSearch(text: string): string {
    // Remove Hebrew diacritics (nikkud and cantillation marks)
    return text.replace(/[\u0591-\u05C7]/g, '')
}

/**
 * Normalize dashes for search matching
 */
export function normalizeDashesForSearch(text: string): string {
    // Replace all dash types with a single dash type for matching
    return text.replace(/[\u002D\u2013\u2014\u05BE]/g, '-')
}

/**
 * Build position map for search (maps normalized position to original position)
 */
export function buildPositionMapForSearch(text: string): number[] {
    const map: number[] = []
    let originalPos = 0

    for (let i = 0; i < text.length; i++) {
        const char = text[i]
        // Check if this is a diacritic that will be removed
        if (char && /[\u0591-\u05C7]/.test(char)) {
            // Don't add to map - this character will be skipped in normalized text
        } else {
            map.push(originalPos)
        }
        originalPos++
    }

    // Add final position
    map.push(originalPos)

    return map
}

/**
 * Normalize text for search by removing all non-word characters
 * This allows flexible matching of abbreviations and words with punctuation
 * 
 * USAGE: ONLY for open book page search (LineView) and Hebrew books search
 * DO NOT use for commentary search or other search features
 * 
 * Examples: 
 * - רשב"א matches רשבא (removes gershayim)
 * - רשב'א matches רשבא (removes geresh)
 * - Removes quotes, punctuation, and other non-word characters
 */
export function normalizeTextForSearch(text: string): string {
    return text
        .replace(/[\u05F3\u05F4]/g, '')  // Remove Hebrew geresh (׳) and gershayim (״)
        .replace(/['"״׳]/g, '')          // Remove quotes (ASCII and Hebrew)
        .replace(/[־\-.,;:!?()[\]{}]/g, '') // Remove punctuation and separators
}

