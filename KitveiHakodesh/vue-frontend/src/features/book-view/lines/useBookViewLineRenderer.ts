import { computed } from 'vue'
import { applyDiacriticsFilter, removeDiacriticsForSearch, stripHtmlForSearch } from '@/utils/hebrewTextProcessing'
import { cleanHebrewText } from '@/utils/hebrewTextCleaning'
import { censorDivineNames } from '@/utils/censorDivineNames'
import { argbToCssColor, highlightColorToThemeColor } from '../lines/bookViewAnnotationColors'
import type { useSettingsStore } from '@/stores/settingsStore'
import type { Highlight } from '../lines/useBookViewHighlights'
import type { Note } from '../lines/useBookViewNotes'

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
  getNotesForLine?: (lineId: number) => Note[]
}

// Precompiled diacritic range check — faster than /[\u0591-\u05C7]/.test(ch) in tight loops.
export function isDiacriticChar(ch: string): boolean {
  const code = ch.charCodeAt(0)
  return code >= 0x0591 && code <= 0x05C7
}

function highlightMatches(
  content: string,
  query: string,
): string {
  const q = removeDiacriticsForSearch(query.trim())
  if (!q) return content

  const stripped = stripHtmlForSearch(content)

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

    const isDiacritic = isDiacriticChar(ch)
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

  // Build stripped text entity-aware (same logic as highlightMatches).
  const strippedContent = stripHtmlForSearch(content)

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

  // Walk the content HTML, inserting <mark> tags at the right stripped-text positions.
  // Entities are treated as single atomic characters — never split by a mark boundary.
  const out: string[] = []
  let strippedPos = 0, inTag = false, inMatch = false, matchEndPos = 0, rangeIdx = 0
  let i = 0
  while (i < content.length) {
    const ch = content[i]!
    if (ch === '<') { inTag = true; out.push(ch); i++; continue }
    if (ch === '>') { inTag = false; out.push(ch); i++; continue }
    if (inTag) { out.push(ch); i++; continue }

    if (ch === '&') {
      let entityEnd = -1
      for (let j = i + 1; j < content.length && j <= i + 12; j++) {
        const c = content[j]!
        if (c === ';') { entityEnd = j; break }
        if (c === ' ' || c === '\t' || c === '\n' || c === '<') break
      }
      if (entityEnd !== -1) {
        if (!inMatch) {
          while (rangeIdx < merged.length && merged[rangeIdx]!.start < strippedPos) rangeIdx++
          if (rangeIdx < merged.length && merged[rangeIdx]!.start === strippedPos) {
            out.push('<mark class="search-match">')
            matchEndPos = merged[rangeIdx]!.end
            inMatch = true
            rangeIdx++
          }
        }
        for (let j = i; j <= entityEnd; j++) out.push(content[j]!)
        i = entityEnd + 1
        strippedPos++
        if (inMatch && strippedPos >= matchEndPos) { out.push('</mark>'); inMatch = false }
        continue
      }
    }

    const isDiacritic = isDiacriticChar(ch)

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
    i++
  }

  if (inMatch) out.push('</mark>')
  return out.join('')
}

// ── User highlight injection ──────────────────────────────────────────────────
// NOTE: applyUserHighlights is also used by useCommentaryRender.ts — keep it
// exported so both renderers can share the same implementation.

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
export function applyUserHighlights(content: string, highlights: Highlight[]): string {
  if (!highlights.length) return content

  interface MarkEvent {
    pos: number
    type: 'open' | 'close'
    color: string
    order: number
  }

  const events: MarkEvent[] = []
  for (const h of highlights) {
    if (h.startOffset >= h.endOffset) continue
    const themeColor = highlightColorToThemeColor(h.colorArgb)
    events.push({ pos: h.startOffset, type: 'open', color: themeColor, order: 1 })
    events.push({ pos: h.endOffset, type: 'close', color: themeColor, order: 0 })
  }
  events.sort((a, b) => a.pos - b.pos || a.order - b.order)
  if (!events.length) return content

  const out: string[] = []
  let strippedPos = 0, inTag = false, eventIndex = 0
  const openStack: string[] = []
  let i = 0

  while (i < content.length) {
    const ch = content[i]!
    if (ch === '<') { inTag = true; out.push(ch); i++; continue }
    if (ch === '>') { inTag = false; out.push(ch); i++; continue }
    if (inTag) { out.push(ch); i++; continue }

    if (ch === '&') {
      let entityEnd = -1
      for (let j = i + 1; j < content.length && j <= i + 12; j++) {
        const c = content[j]!
        if (c === ';') { entityEnd = j; break }
        if (c === ' ' || c === '\t' || c === '\n' || c === '<') break
      }
      if (entityEnd !== -1) {
        // Flush events at this position before emitting the entity atomically.
        while (eventIndex < events.length && events[eventIndex]!.pos === strippedPos) {
          const event = events[eventIndex]!
          if (event.type === 'close') {
            out.push('</mark>')
            openStack.pop()
            if (openStack.length) out.push(`<mark class="user-highlight" style="background:${openStack[openStack.length - 1]}">`)
          } else {
            if (openStack.length) out.push('</mark>')
            out.push(`<mark class="user-highlight" style="background:${event.color}">`)
            openStack.push(event.color)
          }
          eventIndex++
        }
        for (let j = i; j <= entityEnd; j++) out.push(content[j]!)
        i = entityEnd + 1
        strippedPos++
        continue
      }
    }

    const isDiacritic = isDiacriticChar(ch)
    if (!isDiacritic) {
      while (eventIndex < events.length && events[eventIndex]!.pos === strippedPos) {
        const event = events[eventIndex]!
        if (event.type === 'close') {
          out.push('</mark>')
          openStack.pop()
          if (openStack.length) out.push(`<mark class="user-highlight" style="background:${openStack[openStack.length - 1]}">`)
        } else {
          if (openStack.length) out.push('</mark>')
          out.push(`<mark class="user-highlight" style="background:${event.color}">`)
          openStack.push(event.color)
        }
        eventIndex++
      }
    }

    out.push(ch)
    if (!isDiacritic) strippedPos++
    i++
  }

  while (eventIndex < events.length && events[eventIndex]!.type === 'close') eventIndex++
  for (let k = openStack.length - 1; k >= 0; k--) out.push('</mark>')
  return out.join('')
}

// ── User note marker injection ────────────────────────────────────────────────
// NOTE: also used by useCommentaryRender.ts — keep exported.

/**
 * Injects a superscript footnote marker at the endOffset position of each note.
 * Uses the same HTML-aware, diacritic-aware, entity-aware walk as applyUserHighlights.
 */
export function applyUserNoteMarkers(content: string, notes: Note[]): string {
  if (!notes.length) return content

  interface NoteEvent {
    pos: number
    noteId: number
    noteText: string
    label: string
  }

  const events: NoteEvent[] = []
  for (const n of notes) {
    if (n.startOffset >= n.endOffset) continue
    events.push({ pos: n.endOffset, noteId: n.id, noteText: n.note, label: '[*]' })
  }
  events.sort((a, b) => a.pos - b.pos)
  if (!events.length) return content

  const out: string[] = []
  let strippedPos = 0, inTag = false, eventIndex = 0
  let i = 0

  while (i < content.length) {
    const ch = content[i]!
    if (ch === '<') { inTag = true; out.push(ch); i++; continue }
    if (ch === '>') { inTag = false; out.push(ch); i++; continue }
    if (inTag) { out.push(ch); i++; continue }

    if (ch === '&') {
      let entityEnd = -1
      for (let j = i + 1; j < content.length && j <= i + 12; j++) {
        const c = content[j]!
        if (c === ';') { entityEnd = j; break }
        if (c === ' ' || c === '\t' || c === '\n' || c === '<') break
      }
      if (entityEnd !== -1) {
        for (let j = i; j <= entityEnd; j++) out.push(content[j]!)
        i = entityEnd + 1
        strippedPos++
        continue
      }
    }

    const isDiacritic = isDiacriticChar(ch)
    if (!isDiacritic) {
      while (eventIndex < events.length && events[eventIndex]!.pos === strippedPos) {
        const event = events[eventIndex]!
        const escapedText = event.noteText
          .replace(/&/g, '&amp;')
          .replace(/"/g, '&quot;')
          .replace(/</g, '&lt;')
          .replace(/>/g, '&gt;')
        out.push(`<sup class="user-note-marker" data-note-id="${event.noteId}" title="${escapedText}">${event.label}</sup>`)
        eventIndex++
      }
    }

    out.push(ch)
    if (!isDiacritic) strippedPos++
    i++
  }

  return out.join('')
}

// ── Composable ────────────────────────────────────────────────────────────────

export function useBookViewLineRenderer(
  settings: SettingsStore,
  diacriticsState: import('vue').ComputedRef<number>,
  getProps: () => LineRenderProps,
) {
  // Two-tier cache:
  //   globalCacheKey — covers inputs that affect ALL lines (diacritics, censor, search).
  //                    When this changes the whole cache is wiped.
  //   perLineSuffix  — per-line Map<lineId, string> storing the highlights+notes
  //                    signature last used to render that line. When only one line's
  //                    annotations change, only that entry is evicted rather than
  //                    clearing every rendered line.
  // currentMatchOccurrence is intentionally excluded — handled by setCurrentMark().
  const renderCache = new Map<number, string>()
  const perLineAnnotationKey = new Map<number, string>()
  let globalCacheKey = ''

  function getGlobalKey(): string {
    const p = getProps()
    return `${diacriticsState.value}|${settings.censorDivineNames}|${p.searchQuery ?? ''}|${p.searchHighlightLineIndex ?? -1}|${p.searchHighlightQuery ?? ''}|${p.searchHighlightSnippet ?? ''}|${p.searchHighlightTerms?.join(',') ?? ''}`
  }

  function getAnnotationKey(lineId: number): string {
    const p = getProps()
    const lineHighlights = p.getHighlightsForLine?.(lineId) ?? []
    const highlightsSig = lineHighlights.map((h) => `${h.id}:${h.startOffset}:${h.endOffset}:${h.colorArgb}`).join(',')
    const lineNotes = p.getNotesForLine?.(lineId) ?? []
    const notesSig = lineNotes.map((n) => `${n.id}:${n.startOffset}:${n.endOffset}:${n.updatedAt}`).join(',')
    return `${highlightsSig}|${notesSig}`
  }

  function lineContent(raw: string, lineIndex: number, lineId: number): string {
    const globalKey = getGlobalKey()
    if (globalKey !== globalCacheKey) {
      renderCache.clear()
      perLineAnnotationKey.clear()
      globalCacheKey = globalKey
    }

    const annotationKey = getAnnotationKey(lineId)
    if (perLineAnnotationKey.get(lineId) !== annotationKey) {
      renderCache.delete(lineIndex)
      perLineAnnotationKey.set(lineId, annotationKey)
    }

    const cached = renderCache.get(lineIndex)
    if (cached !== undefined) return cached

    const p = getProps()
    let content = diacriticsState.value === 0 ? raw : diacriticsState.value === 2 ? cleanHebrewText(raw) : applyDiacriticsFilter(raw, diacriticsState.value)
    if (settings.censorDivineNames) content = censorDivineNames(content)

    // Apply user highlights first (underneath search marks and note markers)
    const lineHighlights = p.getHighlightsForLine?.(lineId) ?? []
    if (lineHighlights.length) {
      content = applyUserHighlights(content, lineHighlights)
    }

    // Apply note markers on top of highlights, underneath search marks
    const lineNotes = p.getNotesForLine?.(lineId) ?? []
    if (lineNotes.length) {
      content = applyUserNoteMarkers(content, lineNotes)
    }

    if (p.searchQuery?.trim())
      content = highlightMatches(content, p.searchQuery)
    if (lineIndex === p.searchHighlightLineIndex) {
      if (p.searchHighlightSnippet) {
        content = highlightFromSnippet(content, p.searchHighlightSnippet)
      } else if (p.searchHighlightQuery?.trim()) {
        content = highlightMatches(content, p.searchHighlightQuery)
      }
    }

    renderCache.set(lineIndex, content)
    return content
  }

  return { lineContent }
}

/**
 * Move the `.current` class to the nth `<mark class="search-match">` inside
 * the rendered line at `lineIndex` within `container`. Scopes the query to
 * the virtualizer row element with `data-index` so `occurrence` is per-line,
 * matching `occurrenceInLine` from the search scan.
 *
 * Pass `lineIndex = -1` to clear all `.current` marks across the container.
 *
 * @param container  The scroller element
 * @param lineIndex  The virtualizer data-index of the target line, or -1 to clear all
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
