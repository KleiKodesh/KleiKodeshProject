import type { RegexFindOptions, SearchResult } from '@/types/regex-find'

export class RegexService {
  // Mock implementation - in real app this would communicate with C# backend
  static async search(options: RegexFindOptions): Promise<SearchResult[]> {
    // Simulate API delay
    await new Promise(resolve => setTimeout(resolve, 300))
    
    if (!options.text.trim()) {
      return []
    }

    // Mock search results for testing
    const mockText = `This is a sample document with multiple occurrences of the search term "${options.text}". 
    Here is another instance of ${options.text} in the middle of a sentence. 
    And finally, one more ${options.text} at the end of this paragraph.
    
    The document continues with more content that might contain ${options.text} scattered throughout.
    Sometimes the ${options.text} appears with different formatting or context.`

    const results: SearchResult[] = []
    let match
    const regex = new RegExp(this.buildPattern(options), 'gi')
    
    while ((match = regex.exec(mockText)) !== null) {
      const start = match.index
      const end = start + match[0].length
      const snippetLength = 50
      
      const beforeStart = Math.max(0, start - snippetLength)
      const afterEnd = Math.min(mockText.length, end + snippetLength)
      
      results.push({
        start,
        end,
        text: match[0],
        before: mockText.substring(beforeStart, start),
        after: mockText.substring(end, afterEnd)
      })
    }

    return results
  }

  static async replaceAll(options: RegexFindOptions): Promise<boolean> {
    // Simulate API delay
    await new Promise(resolve => setTimeout(resolve, 200))
    
    console.log('Mock: Replace all occurrences', {
      searchText: options.text,
      replaceText: options.replace.text,
      formatting: {
        bold: options.replace.bold,
        italic: options.replace.italic,
        underline: options.replace.underline
      }
    })
    
    return true
  }

  static async replaceCurrent(options: RegexFindOptions, result: SearchResult): Promise<boolean> {
    // Simulate API delay
    await new Promise(resolve => setTimeout(resolve, 200))
    
    console.log('Mock: Replace current occurrence', {
      position: `${result.start}-${result.end}`,
      originalText: result.text,
      replaceText: options.replace.text
    })
    
    return true
  }

  static async selectInDocument(result: SearchResult): Promise<boolean> {
    // Simulate API delay
    await new Promise(resolve => setTimeout(resolve, 100))
    
    console.log('Mock: Select text in Word document', {
      position: `${result.start}-${result.end}`,
      text: result.text
    })
    
    return true
  }

  private static buildPattern(options: RegexFindOptions): string {
    let pattern = options.text

    if (options.useWildcards) {
      // Convert wildcards to regex
      pattern = pattern.replace(/\*/g, '.*').replace(/\?/g, '.')
    } else {
      // Escape regex special characters
      pattern = pattern.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
    }

    if (options.slop > 0) {
      // Handle slop (word proximity)
      const words = options.text.split(/\s+/)
      if (words.length > 1) {
        const escapedWords = words.map(word => 
          options.useWildcards 
            ? word.replace(/\*/g, '.*').replace(/\?/g, '.')
            : word.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
        )
        const slopPattern = `\\b(?:${escapedWords.join(`\\b\\s+(?:\\w+\\s+){0,${options.slop}}\\b`)})\\b`
        pattern = slopPattern
      }
    }

    return pattern
  }
}