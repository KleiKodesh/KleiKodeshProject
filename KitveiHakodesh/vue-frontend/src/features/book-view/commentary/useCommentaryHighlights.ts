/**
 * Manages user highlights for all commentary books visible in the commentary panel.
 *
 * Unlike useBookViewHighlights (which loads highlights for a single book by id),
 * the commentary panel shows lines from many different books simultaneously — each
 * CommentaryGroup has its own bookId and its own set of lineIds. Highlights must
 * be stored and looked up using the commentary book's bookId, not the parent book's id.
 *
 * This composable:
 * - Loads highlights lazily per commentary bookId as groups become visible
 * - Maintains a single combined Map<lineId, Highlight[]> across all loaded books
 * - Exposes the same getHighlightsForLine / applyHighlight / clearHighlight API
 *   as useBookViewHighlights so the renderer and copy menu can use it identically
 * - Routes applyHighlight and clearHighlight to the correct commentary bookId by
 *   looking up which book owns the given lineId in the current groups
 */
import { ref, watch } from 'vue'
import { queryUserSettings, executeUserSettings } from '@/webview-host/userSettingsDb'
import { USER_SETTINGS_SQL } from '@/webview-host/userSettingsDb.sql'
import type { Highlight } from '../lines/useBookViewHighlights'
import type { CommentaryGroup } from './useCommentary'

// Map from lineId → highlights on that line, sorted by startOffset
type HighlightsByLine = Map<number, Highlight[]>

export function useCommentaryHighlights(getGroups: () => CommentaryGroup[]) {
  const highlightsByLine = ref<HighlightsByLine>(new Map())

  // Track which bookIds have already been loaded so we don't re-fetch
  const loadedBookIds = new Set<number>()

  // Map from lineId → commentary bookId — needed so applyHighlight / clearHighlight
  // know which bookId to pass to the DB when writing a new row
  const lineIdToBookId = new Map<number, number>()

  // ── Load ──────────────────────────────────────────────────────────────────

  async function loadHighlightsForBook(commentaryBookId: number): Promise<void> {
    if (loadedBookIds.has(commentaryBookId)) return
    loadedBookIds.add(commentaryBookId)
    try {
      const rows = await queryUserSettings<{
        id: number
        bookId: number
        lineId: number
        startOffset: number
        endOffset: number
        colorArgb: number
        createdAt: number
      }>(USER_SETTINGS_SQL.GET_HIGHLIGHTS_FOR_BOOK, [commentaryBookId])

      for (const row of rows) {
        const highlight: Highlight = {
          id: row.id,
          bookId: row.bookId,
          lineId: row.lineId,
          startOffset: Number(row.startOffset),
          endOffset: Number(row.endOffset),
          colorArgb: Number(row.colorArgb),
          createdAt: Number(row.createdAt),
        }
        _addToMap(highlight)
      }
    } catch {
      // DB not ready — silently skip; the watch below will re-run when groups change
      loadedBookIds.delete(commentaryBookId)
    }
  }

  // When the visible groups change, load highlights for any newly seen commentary books
  watch(
    getGroups,
    (groups) => {
      for (const group of groups) {
        if (group.bookId > 0 && !loadedBookIds.has(group.bookId)) {
          // Register all lineIds for this group before the async load so
          // applyHighlight calls that arrive before the load finishes still
          // know which bookId owns each line
          for (const line of group.lines) {
            if (line.lineId > 0) lineIdToBookId.set(line.lineId, group.bookId)
          }
          void loadHighlightsForBook(group.bookId)
        }
      }
      // Keep lineIdToBookId up to date for lines already registered
      for (const group of groups) {
        for (const line of group.lines) {
          if (line.lineId > 0) lineIdToBookId.set(line.lineId, group.bookId)
        }
      }
    },
    { immediate: true },
  )

  // ── Per-line lookup ────────────────────────────────────────────────────────

  function getHighlightsForLine(lineId: number): Highlight[] {
    return highlightsByLine.value.get(lineId) ?? []
  }

  // ── Internal map mutation helpers ──────────────────────────────────────────

  function _removeFromMap(highlight: Highlight) {
    const list = highlightsByLine.value.get(highlight.lineId)
    if (!list) return
    const index = list.findIndex((h) => h.id === highlight.id)
    if (index !== -1) list.splice(index, 1)
    if (list.length === 0) highlightsByLine.value.delete(highlight.lineId)
  }

  function _addToMap(highlight: Highlight) {
    const list = highlightsByLine.value.get(highlight.lineId) ?? []
    list.push(highlight)
    list.sort((a, b) => a.startOffset - b.startOffset)
    highlightsByLine.value.set(highlight.lineId, list)
  }

  function _updateInMap(highlight: Highlight) {
    const list = highlightsByLine.value.get(highlight.lineId)
    if (!list) return
    const index = list.findIndex((h) => h.id === highlight.id)
    if (index !== -1) {
      list[index] = { ...highlight }
      list.sort((a, b) => a.startOffset - b.startOffset)
    }
  }

  // ── DB write helpers (same logic as useBookViewHighlights) ─────────────────

  async function _insertHighlight(
    commentaryBookId: number,
    lineId: number,
    startOffset: number,
    endOffset: number,
    colorArgb: number,
  ): Promise<Highlight> {
    const createdAt = Date.now()
    const insertedId = await executeUserSettings(USER_SETTINGS_SQL.INSERT_HIGHLIGHT, [
      commentaryBookId,
      lineId,
      startOffset,
      endOffset,
      colorArgb,
      createdAt,
    ])
    const highlight: Highlight = {
      id: insertedId,
      bookId: commentaryBookId,
      lineId,
      startOffset,
      endOffset,
      colorArgb,
      createdAt,
    }
    _addToMap(highlight)
    return highlight
  }

  async function _updateHighlight(
    highlight: Highlight,
    startOffset: number,
    endOffset: number,
    colorArgb: number,
  ): Promise<void> {
    await executeUserSettings(USER_SETTINGS_SQL.UPDATE_HIGHLIGHT, [
      startOffset,
      endOffset,
      colorArgb,
      highlight.id,
    ])
    _updateInMap({ ...highlight, startOffset, endOffset, colorArgb })
  }

  async function _deleteHighlight(highlight: Highlight): Promise<void> {
    await executeUserSettings(USER_SETTINGS_SQL.DELETE_HIGHLIGHT, [highlight.id])
    _removeFromMap(highlight)
  }

  // ── Apply highlight ────────────────────────────────────────────────────────

  async function applyHighlight(
    lineId: number,
    startOffset: number,
    endOffset: number,
    colorArgb: number,
  ): Promise<void> {
    if (startOffset >= endOffset) return

    const commentaryBookId = lineIdToBookId.get(lineId)
    if (commentaryBookId == null) return

    try {
      const existing = [...(highlightsByLine.value.get(lineId) ?? [])]
      const overlapping = existing.filter(
        (h) => h.startOffset < endOffset && h.endOffset > startOffset,
      )

      let mergedStart = startOffset
      let mergedEnd = endOffset
      const toDelete: Highlight[] = []

      for (const h of overlapping) {
        if (h.colorArgb === colorArgb) {
          mergedStart = Math.min(mergedStart, h.startOffset)
          mergedEnd = Math.max(mergedEnd, h.endOffset)
          toDelete.push(h)
        } else {
          if (h.startOffset >= startOffset && h.endOffset <= endOffset) {
            toDelete.push(h)
          } else if (h.startOffset < startOffset && h.endOffset > endOffset) {
            toDelete.push(h)
            await _insertHighlight(commentaryBookId, lineId, h.startOffset, startOffset, h.colorArgb)
            await _insertHighlight(commentaryBookId, lineId, endOffset, h.endOffset, h.colorArgb)
          } else if (h.startOffset < startOffset) {
            await _updateHighlight(h, h.startOffset, startOffset, h.colorArgb)
          } else {
            await _updateHighlight(h, endOffset, h.endOffset, h.colorArgb)
          }
        }
      }

      for (const h of toDelete) {
        await _deleteHighlight(h)
      }

      await _insertHighlight(commentaryBookId, lineId, mergedStart, mergedEnd, colorArgb)
    } catch (err) {
      console.error('[commentary highlight] error:', err)
      throw err
    }
  }

  // ── Clear highlight ────────────────────────────────────────────────────────

  async function clearHighlight(
    lineId: number,
    startOffset: number,
    endOffset: number,
  ): Promise<void> {
    if (startOffset >= endOffset) return

    const commentaryBookId = lineIdToBookId.get(lineId)
    if (commentaryBookId == null) return

    const existing = [...(highlightsByLine.value.get(lineId) ?? [])]
    const overlapping = existing.filter(
      (h) => h.startOffset < endOffset && h.endOffset > startOffset,
    )

    for (const h of overlapping) {
      if (h.startOffset >= startOffset && h.endOffset <= endOffset) {
        await _deleteHighlight(h)
      } else if (h.startOffset < startOffset && h.endOffset > endOffset) {
        await _deleteHighlight(h)
        await _insertHighlight(commentaryBookId, lineId, h.startOffset, startOffset, h.colorArgb)
        await _insertHighlight(commentaryBookId, lineId, endOffset, h.endOffset, h.colorArgb)
      } else if (h.startOffset < startOffset) {
        await _updateHighlight(h, h.startOffset, startOffset, h.colorArgb)
      } else {
        await _updateHighlight(h, endOffset, h.endOffset, h.colorArgb)
      }
    }
  }

  return {
    getHighlightsForLine,
    applyHighlight,
    clearHighlight,
  }
}
