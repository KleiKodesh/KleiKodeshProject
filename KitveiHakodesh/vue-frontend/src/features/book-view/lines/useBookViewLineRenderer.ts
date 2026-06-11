import { computed } from 'vue'
import { applyDiacriticsFilter, removeDiacriticsForSearch } from '@/utils/hebrewTextProcessing'
import { censorDivineNames } from '@/utils/censorDivineNames'
import { argbToCssColor, highlightColorToThemeColor } from '../lines/bookViewAnnotationColors'
import type { useSettingsStore } from '@/stores/settingsStore'
import type { Highlight } from '../lines/useBookViewHighlights'

type SettingsStore = ReturnType<typeof useSettingsStore>

interface LineRenderProps {
  searchQuery?: string
  currentMatchLineIndex?: number
  currentMatchOccurrence?: number
  searchHighlightLineIndex?: number
  searchHighlightQuery?: string
  searchHighlightSnippet?: string
  searchHighlightTerms?: string[]
  getHighlightsForLine?: (lineId: number) => Highlight[]
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

// ── User highlight injection ──────────────────────────────────────────────────

/**
 * Injects <mark class="user-highlight"> spans into the HTML content for each
 * user highlight on the line. Offsets are character-based (stripped of diacritics
 * and HTML tags) — same coordinate space used by Zayit.
 *
 * The walk is HTML-aware: tag characters are skipped when counting stripped positions
 * so offsets remain consistent regardless of inline markup.
 *
 * Color translation: Converts the Material Design color stored in the DB to a
 * theme-friendly Fluent variant for rendering. This keeps the DB value unchanged
 * while adapting the visual appearance to match the app's design system.
 *
 * HTML entities: Treated like tags — skipped in character counting so highlight
 * boundaries never split entities (e.g., &thinsp;). If a highlight's start/end
 * falls in the middle of an entity, the entire entity stays outside the mark tag.
 */
function applyUserHighlights(content: string, highlights: Highlight[]): string {
  if (!highlights.length) return content

  // Build a list of open/close events sorted by stripped character position
  interface MarkEvent {
    pos: number
    type: 'open' | 'close'
    color: string
    // For ordering: closes before opens at same position
    order: number
  }

  const events: MarkEvent[] = []
  for (const h of highlights) {
    if (h.startOffset >= h.endOffset) continue
    // Translate Material Design color to theme-adjusted color for display
    const themeColor = highlightColorToThemeColor(h.colorArgb)
    events.push({ pos: h.startOffset, type: 'open', color: themeColor, order: 1 })
    events.push({ pos: h.endOffset, type: 'close', color: themeColor, order: 0 })
  }
  events.sort((a, b) => a.pos - b.pos || a.order - b.order)

  if (!events.length) return content

  const out: string[] = []
  let strippedPos = 0
  let inTag = false
  let inEntity = false
  let eventIndex = 0
  const openStack: string[] = [] // track open colors for nesting

  for (let i = 0; i < content.length; i++) {
    const ch = content[i]!

    if (ch === '<') { inTag = true; out.push(ch); continue }
    if (ch === '>') { inTag = false; out.push(ch); continue }
    if (inTag) { out.push(ch); continue }

    // Detect start of HTML entity (& followed by semicolon to mark end)
    if (ch === '&') { inEntity = true; out.push(ch); continue }
    // Detect end of HTML entity
    if (inEntity && ch === ';') { inEntity = false; out.push(ch); continue }
    // Skip counting for characters inside entities
    if (inEntity) { out.push(ch); continue }

    const isDiacritic = /[\u0591-\u05C7]/.test(ch)

    if (!isDiacritic) {
      // Flush all events at this position
      while (eventIndex < events.length && events[eventIndex]!.pos === strippedPos) {
        const event = events[eventIndex]!
        if (event.type === 'close') {
          out.push('</mark>')
          openStack.pop()
          // Re-open any remaining open marks (handles overlapping highlights)
          if (openStack.length) {
            out.push(`<mark class="user-highlight" style="background:${openStack[openStack.length - 1]}">`)
          }
        } else {
          // Close current open mark before opening new one (prevent nesting)
          if (openStack.length) out.push('</mark>')
          out.push(`<mark class="user-highlight" style="background:${event.color}">`)
          openStack.push(event.color)
        }
        eventIndex++
      }
    }

    out.push(ch)
    if (!isDiacritic) strippedPos++
  }

  // Close any marks that extend to/past end of string
  while (eventIndex < events.length && events[eventIndex]!.type === 'close') eventIndex++
  for (let i = openStack.length - 1; i >= 0; i--) out.push('</mark>')

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

  function getCacheKey(lineId: number): string {
    const p = getProps()
    const lineHighlights = p.getHighlightsForLine?.(lineId) ?? []
    const highlightsSig = lineHighlights.map((h) => `${h.id}:${h.startOffset}:${h.endOffset}:${h.colorArgb}`).join(',')
    return `${diacriticsState.value}|${settings.censorDivineNames}|${p.searchQuery ?? ''}|${p.currentMatchLineIndex ?? -1}|${p.currentMatchOccurrence ?? 0}|${p.searchHighlightLineIndex ?? -1}|${p.searchHighlightQuery ?? ''}|${p.searchHighlightSnippet ?? ''}|${p.searchHighlightTerms?.join(',') ?? ''}|${highlightsSig}`
  }

  function lineContent(raw: string, lineIndex: number, lineId: number): string {
    const key = getCacheKey(lineId)
    if (key !== renderCacheKey) {
      renderCache.clear()
      renderCacheKey = key
    }
    const cached = renderCache.get(lineIndex)
    if (cached !== undefined) return cached

    const p = getProps()
    let content = diacriticsState.value === 0 ? raw : applyDiacriticsFilter(raw, diacriticsState.value)
    if (settings.censorDivineNames) content = censorDivineNames(content)

    // Apply user highlights first (underneath search marks)
    const lineHighlights = p.getHighlightsForLine?.(lineId) ?? []
    if (lineHighlights.length) {
      console.log('[renderer] applying highlights to line', lineId, ':', lineHighlights)
      content = applyUserHighlights(content, lineHighlights)
    }

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
