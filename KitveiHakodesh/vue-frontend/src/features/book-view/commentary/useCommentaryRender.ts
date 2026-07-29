import { computed, watch } from 'vue'
import { useSettingsStore } from '@/stores/settingsStore'
import { applyDiacriticsFilter, removeDiacriticsForSearch, stripHtmlForSearch } from '@/utils/hebrewTextProcessing'
import { cleanHebrewText } from '@/utils/hebrewTextCleaning'
import { censorDivineNames } from '@/utils/censorDivineNames'
import { applyUserHighlights, applyUserNoteMarkers, setCurrentMark, isDiacriticChar } from '../lines/useBookViewLineRenderer'
import { applyWordLinkAnchors, wordLinkAnchorsSig } from '../lines/wordLinkAnchors'
import type { WordLinkAnchor } from '@/webview-host/queries.types'
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
) {
  const settingsStore = useSettingsStore()

  const diacriticsState = computed(() => settingsStore.diacriticsState)
  const commentaryFontPx = computed(() => {
    const effectiveFontSize = settingsStore.useSeparateCommentarySettings
      ? settingsStore.commentaryFontSize
      : settingsStore.fontSize
    return (getCommentaryZoom() / 100) * (effectiveFontSize / 100) * 15
  })

  // Two-tier cache — same pattern as useBookViewLineRenderer:
  //   globalCacheKey  — diacritics, censor, searchQuery; wipes all on change.
  //   perLineAnnotationKey — highlights+notes per lineId; evicts only that entry.
  const renderCache = new Map<number, string>()
  const perLineAnnotationKey = new Map<number, string>()
  // Source content that produced each cached entry — section clicks render group
  // structure first and backfill line text in place, so a slot's content can
  // change without the groups array identity changing.
  const renderSource = new Map<number, string>()
  let globalCacheKey = ''

  function getGlobalKey(searchQuery: string | undefined): string {
    return `${diacriticsState.value}|${settingsStore.censorCacheKey}|${searchQuery ?? ''}`
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
    return `${highlightsSig}|${notesSig}|${anchorsSig}`
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

      const isDiacritic = isDiacriticChar(ch)
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
    searchQuery: string | undefined,
  ): string {
    const globalKey = getGlobalKey(searchQuery)
    if (globalKey !== globalCacheKey) {
      renderCache.clear()
      perLineAnnotationKey.clear()
      globalCacheKey = globalKey
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
    result =
      diacriticsState.value === 0 ? result : diacriticsState.value === 2 ? cleanHebrewText(result) : applyDiacriticsFilter(result, diacriticsState.value)
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

    if (searchQuery?.trim()) result = highlightMatches(result, searchQuery)

    renderCache.set(flatIndex, result)
    return result
  }

  // Invalidate render cache when groups change (new line content loaded)
  watch(
    groups,
    () => {
      renderCache.clear()
      perLineAnnotationKey.clear()
      renderSource.clear()
      globalCacheKey = ''
    },
    { flush: 'sync' },
  )

  return {
    diacriticsState,
    commentaryFontPx,
    renderContent,
    setCurrentMark,
  }
}
