import { removeDiacriticsForSearch, stripHtmlForSearch, isSearchIgnoredMark } from '@/utils/hebrewTextProcessing'

/**
 * Wraps every occurrence of `query` in an HTML fragment in <mark class="search-match">, with the
 * app's in-text search normalisation (removeDiacriticsForSearch: niqqud / teamim ignored, maqaf
 * as a space, quote glyphs folded): positions are found in the stripped text and mapped back
 * through a walk that skips tags and keeps entities atomic. Shared by the book view's lines and
 * the FastDocView page.
 */
export function highlightSearchMatches(
  content: string,
  query: string,
  options: { ignoreCase?: boolean } = {},
): string {
  // ignoreCase: the FastDocView search folds case (library MatchCase false); lowercasing keeps
  // positions for the scripts it matters for (Latin, Hebrew has no case).
  const fold = (text: string) => (options.ignoreCase ? text.toLowerCase() : text)
  const q = fold(removeDiacriticsForSearch(query.trim()))
  if (!q) return content

  const stripped = fold(stripHtmlForSearch(content))

  const matchStarts = new Set<number>()
  let idx = 0
  while ((idx = stripped.indexOf(q, idx)) !== -1) {
    matchStarts.add(idx)
    idx++
  }
  if (!matchStarts.size) return content

  const out: string[] = []
  let strippedPos = 0,
    inTag2 = false,
    inMatch = false,
    matchStrippedCount = 0
  let i = 0
  while (i < content.length) {
    const ch = content[i]!
    if (ch === '<') { inTag2 = true; out.push(ch); i++; continue }
    if (ch === '>') { inTag2 = false; out.push(ch); i++; continue }
    if (inTag2) { out.push(ch); i++; continue }

    // Mirror the entity lookahead from stripHtmlForSearch.
    if (ch === '&') {
      let entityEnd = -1
      for (let j = i + 1; j < content.length && j <= i + 12; j++) {
        const c = content[j]!
        if (c === ';') { entityEnd = j; break }
        if (c === ' ' || c === '\t' || c === '\n' || c === '<') break
      }
      if (entityEnd !== -1) {
        // Real entity — emit it atomically, never open/close a mark inside it.
        if (!inMatch && matchStarts.has(strippedPos)) {
          out.push('<mark class="search-match">')
          inMatch = true
          matchStrippedCount = 0
        }
        for (let j = i; j <= entityEnd; j++) out.push(content[j]!)
        i = entityEnd + 1
        if (inMatch && ++matchStrippedCount === q.length) {
          out.push('</mark>')
          inMatch = false
        }
        strippedPos++
        continue
      }
      // Bare & — treat as regular character.
    }

    const isDiacritic = isSearchIgnoredMark(ch)
    if (!isDiacritic && matchStarts.has(strippedPos) && !inMatch) {
      out.push('<mark class="search-match">')
      inMatch = true
      matchStrippedCount = 0
    }
    out.push(ch)
    if (!isDiacritic) {
      if (inMatch && ++matchStrippedCount === q.length) {
        out.push('</mark>')
        inMatch = false
      }
      strippedPos++
    }
    i++
  }
  return out.join('')
}

/**
 * Move the `.current` class to the nth `<mark class="search-match">` inside
 * the rendered line at `lineIndex` within `container`. Scopes the query to
 * the scroller's row element with `data-index` so `occurrence` is per-line,
 * matching `occurrenceInLine` from the search scan.
 *
 * Pass `lineIndex = -1` to clear all `.current` marks across the container.
 *
 * @param container  The scroller element
 * @param lineIndex  The row's data-index (its line index), or -1 to clear all
 * @param occurrence 0-based per-line occurrence index
 */
export function setCurrentMark(container: HTMLElement, lineIndex: number, occurrence: number): void {
  // Clear any existing .current marks across the whole scroller first.
  container.querySelectorAll<HTMLElement>('mark.search-match.current').forEach((mark) => {
    mark.classList.remove('current')
  })
  if (lineIndex === -1) return

  const row = container.querySelector<HTMLElement>(`[data-index="${lineIndex}"]`)
  if (!row) return

  const marks = row.querySelectorAll<HTMLElement>('mark.search-match')
  marks[occurrence]?.classList.add('current')
}
