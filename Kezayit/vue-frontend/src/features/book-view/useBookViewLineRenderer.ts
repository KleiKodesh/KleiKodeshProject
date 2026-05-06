import { computed } from 'vue'
import { applyDiacriticsFilter, removeDiacriticsForSearch } from '@/utils/hebrewTextProcessing'
import { censorDivineNames } from '@/utils/censorDivineNames'
import type { useSettingsStore } from '@/stores/settingsStore'

type SettingsStore = ReturnType<typeof useSettingsStore>

interface LineRenderProps {
  searchQuery?: string
  currentMatchLineIndex?: number
  currentMatchOccurrence?: number
  searchHighlightLineIndex?: number
  searchHighlightQuery?: string
  searchHighlightSnippet?: string
  searchHighlightTerms?: string[]
}

// ── Highlight helpers ─────────────────────────────────────────────────────────

function highlightMatches(
  content: string,
  query: string,
  isCurrentLine: boolean,
  currentOccurrence: number,
): string {
  const q = removeDiacriticsForSearch(query.trim())
  if (!q) return content
  const stripped = removeDiacriticsForSearch(content.replace(/<[^>]*>/g, ''))
  const matchStarts = new Set<number>()
  let idx = 0
  while ((idx = stripped.indexOf(q, idx)) !== -1) {
    matchStarts.add(idx)
    idx++
  }
  if (!matchStarts.size) return content

  const out: string[] = []
  let strippedPos = 0,
    inTag = false,
    inMatch = false,
    matchStrippedCount = 0,
    matchOccurrence = 0
  for (let i = 0; i < content.length; i++) {
    const ch = content[i]!
    if (ch === '<') { inTag = true; out.push(ch); continue }
    if (ch === '>') { inTag = false; out.push(ch); continue }
    if (inTag) { out.push(ch); continue }
    const isDiacritic = /[\u0591-\u05C7]/.test(ch)
    if (!isDiacritic && matchStarts.has(strippedPos) && !inMatch) {
      out.push(
        `<mark class="search-match${isCurrentLine && matchOccurrence === currentOccurrence ? ' current' : ''}">`,
      )
      inMatch = true
      matchStrippedCount = 0
    }
    out.push(ch)
    if (!isDiacritic) {
      if (inMatch && ++matchStrippedCount === q.length) {
        out.push('</mark>')
        inMatch = false
        matchOccurrence++
      }
      strippedPos++
    }
  }
  return out.join('')
}

/**
 * Extract the highlighted terms from a C# snippet by pulling text inside <mark>…</mark> tags.
 * Returns a deduplicated list of stripped (no-diacritics) terms in the order they appear.
 */
function extractSnippetTerms(snippet: string): string[] {
  const terms: string[] = []
  const seen = new Set<string>()
  const re = /<mark>([\s\S]*?)<\/mark>/g
  let match: RegExpExecArray | null
  while ((match = re.exec(snippet)) !== null) {
    const term = removeDiacriticsForSearch(match[1]!.replace(/<[^>]*>/g, '').trim())
    if (term && !seen.has(term)) {
      seen.add(term)
      terms.push(term)
    }
  }
  return terms
}

/**
 * Highlight all occurrences of the snippet's marked terms anywhere in the full line content.
 * Uses the same diacritic-aware, HTML-aware walk as highlightMatches.
 */
function highlightFromSnippet(
  content: string,
  snippet: string,
): string {
  const terms = extractSnippetTerms(snippet)
  if (!terms.length) return content

  const strippedContent = removeDiacriticsForSearch(content.replace(/<[^>]*>/g, ''))

  // Collect all match start positions for all terms
  const matchRanges: Array<{ start: number; len: number }> = []
  for (const term of terms) {
    let idx = 0
    while ((idx = strippedContent.indexOf(term, idx)) !== -1) {
      matchRanges.push({ start: idx, len: term.length })
      idx++
    }
  }
  if (!matchRanges.length) return content

  // Sort and merge overlapping ranges
  matchRanges.sort((a, b) => a.start - b.start)
  const merged: Array<{ start: number; end: number }> = []
  for (const r of matchRanges) {
    const last = merged[merged.length - 1]
    if (last && r.start < last.end) {
      last.end = Math.max(last.end, r.start + r.len)
    } else {
      merged.push({ start: r.start, end: r.start + r.len })
    }
  }

  // Walk the content HTML, inserting <mark> tags at the right stripped-text positions
  const out: string[] = []
  let strippedPos = 0, inTag = false, inMatch = false, matchEndPos = 0, rangeIdx = 0

  for (let i = 0; i < content.length; i++) {
    const ch = content[i]!
    if (ch === '<') { inTag = true; out.push(ch); continue }
    if (ch === '>') { inTag = false; out.push(ch); continue }
    if (inTag) { out.push(ch); continue }

    const isDiacritic = /[\u0591-\u05C7]/.test(ch)

    if (!isDiacritic) {
      if (inMatch && strippedPos >= matchEndPos) { out.push('</mark>'); inMatch = false }
      while (rangeIdx < merged.length && merged[rangeIdx]!.start < strippedPos) rangeIdx++
      if (!inMatch && rangeIdx < merged.length && merged[rangeIdx]!.start === strippedPos) {
        out.push('<mark class="search-match">')
        matchEndPos = merged[rangeIdx]!.end
        inMatch = true
        rangeIdx++
      }
    }

    out.push(ch)
    if (!isDiacritic) strippedPos++
  }

  if (inMatch) out.push('</mark>')
  return out.join('')
}

// ── Composable ────────────────────────────────────────────────────────────────

export function useBookViewLineRenderer(
  settings: SettingsStore,
  diacriticsState: import('vue').ComputedRef<number>,
  getProps: () => LineRenderProps,
) {
  // Cache rendered HTML per line — avoids re-running applyDiacriticsFilter (DOM TreeWalker)
  // and censorDivineNames (6 regexes) on every render cycle for unchanged lines.
  // The cache is invalidated as a whole whenever any rendering input changes.
  const renderCache = new Map<number, string>()
  let renderCacheKey = ''

  function getCacheKey(): string {
    const p = getProps()
    return `${diacriticsState.value}|${settings.censorDivineNames}|${p.searchQuery ?? ''}|${p.currentMatchLineIndex ?? -1}|${p.currentMatchOccurrence ?? 0}|${p.searchHighlightLineIndex ?? -1}|${p.searchHighlightQuery ?? ''}|${p.searchHighlightSnippet ?? ''}|${p.searchHighlightTerms?.join(',') ?? ''}`
  }

  function lineContent(raw: string, lineIndex: number): string {
    const key = getCacheKey()
    if (key !== renderCacheKey) {
      renderCache.clear()
      renderCacheKey = key
    }
    const cached = renderCache.get(lineIndex)
    if (cached !== undefined) return cached

    const p = getProps()
    let content = diacriticsState.value === 0 ? raw : applyDiacriticsFilter(raw, diacriticsState.value)
    if (settings.censorDivineNames) content = censorDivineNames(content)
    if (p.searchQuery?.trim())
      content = highlightMatches(
        content,
        p.searchQuery,
        lineIndex === p.currentMatchLineIndex,
        p.currentMatchOccurrence ?? 0,
      )
    if (lineIndex === p.searchHighlightLineIndex) {
      if (p.searchHighlightSnippet) {
        content = highlightFromSnippet(content, p.searchHighlightSnippet)
      } else if (p.searchHighlightQuery?.trim()) {
        content = highlightMatches(content, p.searchHighlightQuery, false, -1)
      }
    }

    renderCache.set(lineIndex, content)
    return content
  }

  return { lineContent }
}
