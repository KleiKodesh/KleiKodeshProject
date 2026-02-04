/**
 * Hebrew Book Tags Utility
 * 
 * Simple utility to extract tags from CSV data for display and search.
 */

/**
 * Parse tags from CSV string format
 */
export function parseTagsFromCsv(tagsString: string): string[] {
    if (!tagsString || tagsString.trim() === '') {
        return []
    }

    return tagsString
        .split(';')
        .map(tag => tag.trim())
        .filter(tag => tag.length > 0)
}

/**
 * Format tags for display
 */
export function formatTagsForDisplay(tagsString: string): string {
    const tags = parseTagsFromCsv(tagsString)
    return tags.join(' \\ ')
}