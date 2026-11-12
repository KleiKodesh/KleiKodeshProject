import { computed, watch } from 'vue'
import { useSettingsStore } from '@/stores/settingsStore'
import { removeDiacriticsForSearch, stripHtmlForSearch, isSearchIgnoredMark, cleanTextFlagsFor } from '@/utils/hebrewTextProcessing'
import { cleanHebrewText } from '@/utils/hebrewTextCleaning'
import { censorDivineNames } from '@/utils/censorDivineNames'
import { applyUserHighlights, applyUserNoteMarkers, setCurrentMark, annotationOffsetLength, contentInAnnotationOffsetSpace } from '../lines/useBookViewLineRenderer'
import { applyWordLinkAnchors, wordLinkAnchorsSig } from '../lines/wordLinkAnchors'
import { appendUnmarkedNotesMarker, lineNotesSig } from '../lines/lineNotes'
import type { LineNote, WordLinkAnchor } from '@/webview-host/queries.types'
import type { Highlight } from '../lines/useBookViewHighlights'
import type { Note } from '../lines/useBookViewNotes'

/**
 * Manages content rendering for commentary lines: diacritics filtering, divine name censoring,
 * user highlights, search highlighting, and render caching to avoid re-running expensive DOM
 * operations on every render cycle for unchanged commentary lines.
 *
 * `getCommentaryZoom` must be a pane-scoped getter — never pass the store-level
 * `commentaryZoom` computed here, as that always reads the globally active tab and
 * will cross-contaminate pane 2's render cycle when split view is active.
 */
export function useCommentaryRender(
  groups: () => any[],
  getCommentaryZoom: () => number,
  getHighlightsForLine?: (lineId: number) => Highlight[],
  getNotesForLine?: (lineId: number) => Note[],
  getWordLinkAnchorsForLine?: (lineId: number) => WordLinkAnchor[],
  getLineNotesForLine?: (lineId: number) => LineNote[],
) {
  const settingsStore = useSettingsStore()

  // Commentary text: a short plain heading before a dash is a dibbur hamatchil too.
  const cleanTextFlags = computed(() => cleanTextFlagsFor(settingsStore.cleanTextFlags, true))
  const commentaryFontPx = computed(
    () => (getCommentaryZoom() / 100) * (settingsStore.commentaryFontSize / 100) * 15,
  )

  // Two-tier cache — same pattern as useBookViewLineRenderer: a rendered row is kept with
  // the inputs it was rendered from, and a row whose inputs did not change is served the
  // same string.
  //   globalCacheKey  — clean-text, censor; wipes all on change.
  //   perLineAnnotationKey — highlights+notes per lineId; evicts only that entry.
  //   searchQueryByFlat — the search query a row was marked with: the panel search's query
  //                    for a row in its result set, '' otherwise. A new search re-renders
  //                    only the result rows; every other row keeps its render.
  const renderCache = new Map<number, string>()
  const perLineAnnotationKey = new Map<number, string>()
  const searchQueryByFlat = new Map<number, string>()
  // Source content that produced each cached entry — section clicks render group
  // structure first and backfill line text in place, so a slot's content can
  // change without the groups array identity changing.
  const renderSource = new Map<number, string>()
  // The line each cached row was rendered for: a trim keeps the annotation keys of the
  // rows it keeps and drops the rest.
  const lineIdByFlat = new Map<number, number>()
  let globalCacheKey = ''

  function getGlobalKey(): string {
    return `${cleanTextFlags.value}|${settingsStore.censorCacheKey}`
  }

  function getAnnotationKey(lineId: number): string {
    const highlightsSig = getHighlightsForLine
      ? (getHighlightsForLine(lineId) ?? [])
          .map((h) => `${h.id}:${h.startOffset}:${h.endOffset}:${h.colorArgb}`)
          .join(',')
      : ''
    const notesSig = getNotesForLine
      ? (getNotesForLine(lineId) ?? [])
          .map((n) => `${n.id}:${n.startOffset}:${n.endOffset}:${n.updatedAt}`)
          .join(',')
      : ''
    const anchorsSig = wordLinkAnchorsSig(getWordLinkAnchorsForLine?.(lineId) ?? [])
    const lineNotesKey = lineNotesSig(getLineNotesForLine?.(lineId) ?? [])
    return `${highlightsSig}|${notesSig}|${anchorsSig}|${lineNotesKey}`
  }

  function highlightMatches(
    content: string,
    query: string,
  ): string {
    const q = removeDiacriticsForSearch(query.trim())
    if (!q) return content

    const stripped = stripHtmlForSearch(content)
    if (!stripped.includes(q)) return content

    const matchStarts = new Set<number>()
    let idx = 0
    while ((idx = stripped.indexOf(q, idx)) !== -1) {
      matchStarts.add(idx)
      idx++
    }

    const out: string[] = []
    let strippedPos = 0,
      inTag2 = false,
      inMatch = false,
      matchCount = 0
    let i = 0
    while (i < content.length) {
      const ch = content[i]!
      if (ch === '<') { inTag2 = true; out.push(ch); i++; continue }
      if (ch === '>') { inTag2 = false; out.push(ch); i++; continue }
      if (inTag2) { out.push(ch); i++; continue }

      if (ch === '&') {
        let entityEnd = -1
        for (let j = i + 1; j < content.length && j <= i + 12; j++) {
          const c = content[j]!
          if (c === ';') { entityEnd = j; break }
          if (c === ' ' || c === '\t' || c === '\n' || c === '<') break
        }
        if (entityEnd !== -1) {
          if (!inMatch && matchStarts.has(strippedPos)) {
            out.push('<mark class="search-match">')
            inMatch = true
            matchCount = 0
          }
          for (let j = i; j <= entityEnd; j++) out.push(content[j]!)
          i = entityEnd + 1
          if (inMatch && ++matchCount === q.length) {
            out.push('</mark>')
            inMatch = false
          }
          strippedPos++
          continue
        }
      }

      const isDiacritic = isSearchIgnoredMark(ch)
      if (!isDiacritic && matchStarts.has(strippedPos) && !inMatch) {
        out.push('<mark class="search-match">')
        inMatch = true
        matchCount = 0
      }
      out.push(ch)
      if (!isDiacritic) {
        if (inMatch && ++matchCount === q.length) {
          out.push('</mark>')
          inMatch = false
        }
        strippedPos++
      }
      i++
    }
    return out.join('')
  }

  function renderContent(
    content: string,
    flatIndex: number,
    lineId: number | undefined,
    /** The query to mark this row with: the panel search's, for a row in its result set only. */
    searchQuery: string | undefined,
  ): string {
    const globalKey = getGlobalKey()
    if (globalKey !== globalCacheKey) {
      renderCache.clear()
      perLineAnnotationKey.clear()
      searchQueryByFlat.clear()
      lineIdByFlat.clear()
      globalCacheKey = globalKey
    }

    const rowQuery = searchQuery?.trim() ? searchQuery : ''
    if ((searchQueryByFlat.get(flatIndex) ?? '') !== rowQuery) {
      renderCache.delete(flatIndex)
      searchQueryByFlat.set(flatIndex, rowQuery)
    }

    if (lineId != null) {
      const annotationKey = getAnnotationKey(lineId)
      if (perLineAnnotationKey.get(lineId) !== annotationKey) {
        renderCache.delete(flatIndex)
        perLineAnnotationKey.set(lineId, annotationKey)
      }
    }

    if (renderSource.get(flatIndex) !== content) {
      renderCache.delete(flatIndex)
      renderSource.set(flatIndex, content)
    }

    const cached = renderCache.get(flatIndex)
    if (cached !== undefined) return cached

    // Word-link splicing runs FIRST, on the raw content (see useBookViewLineRenderer —
    // anchor offsets are in upstream's visible-char convention, pre-filter/pre-censor).
    const anchors = lineId != null ? (getWordLinkAnchorsForLine?.(lineId) ?? []) : []
    let result = anchors.length ? applyWordLinkAnchors(content, anchors) : content
    result = cleanHebrewText(result, cleanTextFlags.value)
    result = censorDivineNames(result, settingsStore.censorOptions)

    // Apply user highlights before search marks so search marks render on top
    if (lineId != null && getHighlightsForLine) {
      const lineHighlights = getHighlightsForLine(lineId)
      if (lineHighlights.length) result = applyUserHighlights(result, lineHighlights)
    }

    // Apply note markers on top of highlights, underneath search marks
    if (lineId != null && getNotesForLine) {
      const lineNotes = getNotesForLine(lineId)
      if (lineNotes.length) result = applyUserNoteMarkers(result, lineNotes)
    }

    if (rowQuery) result = highlightMatches(result, rowQuery)

    // Inline notes without a marker: one empty end-of-line marker, after every offset walk.
    if (lineId != null) result = appendUnmarkedNotesMarker(result, getLineNotesForLine?.(lineId) ?? [])

    renderCache.set(flatIndex, result)
    if (lineId != null) lineIdByFlat.set(flatIndex, lineId)
    return result
  }

  /**
   * Bounds the cache to the rows near the scroller's window: every row `keep` rejects is
   * dropped, with the inputs it was keyed by. A dropped row scrolled back into view is
   * rendered again from its line, giving the same string.
   */
  function trimRenderCache(keep: (flatIndex: number) => boolean) {
    for (const flatIndex of renderCache.keys()) {
      if (keep(flatIndex)) continue
      renderCache.delete(flatIndex)
      searchQueryByFlat.delete(flatIndex)
      renderSource.delete(flatIndex)
      lineIdByFlat.delete(flatIndex)
    }
    // Rows whose cache entry was already dropped (invalidated) keep no keys either.
    for (const flatIndex of renderSource.keys()) {
      if (renderCache.has(flatIndex)) continue
      searchQueryByFlat.delete(flatIndex)
      renderSource.delete(flatIndex)
      lineIdByFlat.delete(flatIndex)
    }
    const keptLineIds = new Set(lineIdByFlat.values())
    for (const lineId of perLineAnnotationKey.keys()) {
      if (!keptLineIds.has(lineId)) perLineAnnotationKey.delete(lineId)
    }
  }

  // Invalidate render cache when groups change (new line content loaded)
  watch(
    groups,
    () => {
      renderCache.clear()
      perLineAnnotationKey.clear()
      renderSource.clear()
      lineIdByFlat.clear()
      globalCacheKey = ''
    },
    { flush: 'sync' },
  )

  /**
   * How many annotation offsets a line's model content spans — the offset that
   * addresses its end, which is what a whole-line highlight or note uses.
   *
   * Lives here because the answer depends on the same live diacritics and censor
   * settings renderContent applies: annotation offsets are measured against the text
   * AFTER those transforms, and the censor changes the character count.
   */
  function annotationOffsetLengthOf(content: string): number {
    return annotationOffsetLength(
      contentInAnnotationOffsetSpace(content, cleanTextFlags.value, settingsStore.censorOptions),
    )
  }

  return {
    cleanTextFlags,
    commentaryFontPx,
    renderContent,
    trimRenderCache,
    setCurrentMark,
    annotationOffsetLengthOf,
  }
}
