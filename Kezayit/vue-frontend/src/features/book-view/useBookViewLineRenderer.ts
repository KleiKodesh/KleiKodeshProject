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
 * Find the start/end positions (in stripped-text coordinates) of the snippet within the line.
 * Retries by progressively dropping words from the edges if the full snippet isn't found.
 * Returns null if no match found with at least 2 words.
 */
function findSnippetRegion(
  strippedLine: string,
  strippedSnippet: string,
): { start: number; end: number } | null {
  const clean = strippedSnippet
    .replace(/^\.{2,}/, '')
    .replace(/\.{2,}$/, '')
    .trim()
  if (!clean) return null

  const words = clean.split(/\s+/).filter(Boolean)
  if (!words.length) return null

  // Try substrings: full → drop last → drop first → drop both → ...
  // Alternate dropping from end and start, keeping at least 2 words.
  const candidates: string[] = []
  let lo = 0, hi = words.length
  candidates.push(words.slice(lo, hi).join(' '))
  while (hi - lo > 2) {
    hi--
    candidates.push(words.slice(lo, hi).join(' '))
    if (hi - lo <= 2) break
    lo++
    candidates.push(words.slice(lo, hi).join(' '))
  }

  for (const candidate of candidates) {
    const idx = strippedLine.indexOf(candidate)
    if (idx !== -1) return { start: idx, end: idx + candidate.length }
  }
  return null
}

/**
 * Highlight search-result terms within the snippet region of a line.
 *
 * The C# backend strips HTML and all diacritics before matching, so both the snippet
 * and the terms are plain stripped text. We find where the snippet sits inside the
 * stripped line text, then highlight each term within that region using an
 * HTML-aware / diacritic-aware walk.
 *
 * raw is the pre-censoring content used for region detection (the C# snippet was built
 * from uncensored text). content is the post-censoring content used for the output walk.
 */
function highlightSearchResult(
  raw: string,
  content: string,
  snippet: string,
  terms: string[],
): string {
  if (!snippet || !terms.length) return content

  const strippedContent = removeDiacriticsForSearch(raw.replace(/<[^>]*>/g, ''))
  const strippedSnippet = removeDiacriticsForSearch(snippet)

  const region = findSnippetRegion(strippedContent, strippedSnippet)
  if (!region) return content

  const { start: regionStart, end: regionEnd } = region

  const matchRanges: Array<{ start: number; len: number }> = []
  for (const term of terms) {
    const t = removeDiacriticsForSearch(term)
    if (!t) continue
    let idx = regionStart
    while (idx < regionEnd && (idx = strippedContent.indexOf(t, idx)) !== -1 && idx < regionEnd) {
      matchRanges.push({ start: idx, len: t.length })
      idx++
    }
  }
  if (!matchRanges.length) return content

  matchRanges.sort((a, b) => a.start - b.start)

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
      while (rangeIdx < matchRanges.length && matchRanges[rangeIdx]!.start < strippedPos) rangeIdx++
      if (!inMatch && rangeIdx < matchRanges.length && matchRanges[rangeIdx]!.start === strippedPos) {
        out.push('<mark class="search-match">')
        matchEndPos = strippedPos + matchRanges[rangeIdx]!.len
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
  diacriticsState: ReturnType<typeof computed<number>>,
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
      if (p.searchHighlightSnippet && p.searchHighlightTerms?.length) {
        content = highlightSearchResult(raw, content, p.searchHighlightSnippet, p.searchHighlightTerms)
      } else if (p.searchHighlightQuery?.trim()) {
        content = highlightMatches(content, p.searchHighlightQuery, false, -1)
      }
    }

    renderCache.set(lineIndex, content)
    return content
  }

  return { lineContent }
}
