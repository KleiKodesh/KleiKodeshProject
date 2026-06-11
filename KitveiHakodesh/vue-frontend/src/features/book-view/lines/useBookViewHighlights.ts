/**
 * Manages user highlights for the currently open book.
 *
 * Responsibilities:
 * - Load all highlights for the book on mount
 * - Provide per-line highlight lookup for the renderer
 * - Apply a highlight to a selection (handles overlaps: merge / recolor)
 * - Clear a highlight from a selection (handles partial overlap: trim / split)
 * - Persist all mutations to user_settings.db via userSettingsDb.ts
 *
 * Highlight overlap rules when APPLYING a color to range [newStart, newEnd]:
 *   - Existing highlight fully inside new range      → delete existing, new covers it
 *   - Existing highlight fully covers new range      → split into left stub + right stub, new fills gap
 *   - Existing highlight partially overlaps on left  → trim its end to newStart
 *   - Existing highlight partially overlaps on right → trim its start to newEnd
 *   - Existing highlight has SAME color              → merge (extend to cover both)
 *
 * Clear rules when ERASING range [newStart, newEnd]:
 *   - Existing highlight fully inside erased range   → delete
 *   - Existing highlight fully covers erased range   → split into left stub + right stub
 *   - Existing highlight partially overlaps on left  → trim its end to newStart
 *   - Existing highlight partially overlaps on right → trim its start to newEnd
 */
import { ref, watch } from 'vue'
import { queryUserSettings, executeUserSettings } from '@/webview-host/userSettingsDb'
import { USER_SETTINGS_SQL } from '@/webview-host/userSettingsDb.sql'

export interface Highlight {
  id: number
  bookId: number
  lineId: number
  startOffset: number
  endOffset: number
  colorArgb: number
  createdAt: number
}

// Map from lineId → highlights on that line, sorted by startOffset
type HighlightsByLine = Map<number, Highlight[]>

export function useBookViewHighlights(bookId: number) {
  const highlightsByLine = ref<HighlightsByLine>(new Map())
  const isLoaded = ref(false)

  // ── Load ─────────────────────────────────────────────────────────────────

  async function loadHighlights() {
    isLoaded.value = false
    try {
      const rows = await queryUserSettings<{
        id: number
        bookId: number
        lineId: number
        startOffset: number
        endOffset: number
        colorArgb: number
        createdAt: number
      }>(USER_SETTINGS_SQL.GET_HIGHLIGHTS_FOR_BOOK, [bookId])

      const map: HighlightsByLine = new Map()
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
        const existing = map.get(highlight.lineId) ?? []
        existing.push(highlight)
        map.set(highlight.lineId, existing)
      }
      highlightsByLine.value = map
    } catch {
      // DB not ready yet — silently skip, will retry when DB becomes available
    } finally {
      isLoaded.value = true
    }
  }

  loadHighlights()

  // ── Per-line lookup (called by renderer for each visible line) ────────────

  function getHighlightsForLine(lineId: number): Highlight[] {
    return highlightsByLine.value.get(lineId) ?? []
  }

  // ── Internal map mutation helpers ─────────────────────────────────────────

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

  // ── DB write helpers ──────────────────────────────────────────────────────

  async function _insertHighlight(
    lineId: number,
    startOffset: number,
    endOffset: number,
    colorArgb: number,
  ): Promise<Highlight> {
    const createdAt = Date.now()
    const insertedId = await executeUserSettings(USER_SETTINGS_SQL.INSERT_HIGHLIGHT, [
      bookId,
      lineId,
      startOffset,
      endOffset,
      colorArgb,
      createdAt,
    ])
    const highlight: Highlight = {
      id: insertedId,
      bookId,
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

  // ── Apply highlight ───────────────────────────────────────────────────────

  /**
   * Apply colorArgb to the range [startOffset, endOffset] on lineId.
   * Handles overlapping existing highlights: merges same-color, trims/splits
   * different-color highlights that overlap the new range.
   */
  async function applyHighlight(
    lineId: number,
    startOffset: number,
    endOffset: number,
    colorArgb: number,
  ): Promise<void> {
    if (startOffset >= endOffset) return

    console.log('[highlight] apply:', { lineId, startOffset, endOffset, colorArgb })

    try {
      const existing = [...(highlightsByLine.value.get(lineId) ?? [])]
      const overlapping = existing.filter(
        (h) => h.startOffset < endOffset && h.endOffset > startOffset,
      )

      // Compute the final merged range: start from the new range, then expand
      // to include any same-color adjacent/overlapping highlights
      let mergedStart = startOffset
      let mergedEnd = endOffset
      const toDelete: Highlight[] = []

      for (const h of overlapping) {
        if (h.colorArgb === colorArgb) {
          // Same color — absorb into merged range
          mergedStart = Math.min(mergedStart, h.startOffset)
          mergedEnd = Math.max(mergedEnd, h.endOffset)
          toDelete.push(h)
        } else {
          // Different color — trim or split the existing highlight

          if (h.startOffset >= startOffset && h.endOffset <= endOffset) {
            // Fully covered by new range → delete
            toDelete.push(h)
          } else if (h.startOffset < startOffset && h.endOffset > endOffset) {
            // Existing fully covers new range → split into left + right stubs
            toDelete.push(h)
            await _insertHighlight(lineId, h.startOffset, startOffset, h.colorArgb)
            await _insertHighlight(lineId, endOffset, h.endOffset, h.colorArgb)
          } else if (h.startOffset < startOffset) {
            // Overlaps on the left → trim its right end
            await _updateHighlight(h, h.startOffset, startOffset, h.colorArgb)
          } else {
            // Overlaps on the right → trim its left start
            await _updateHighlight(h, endOffset, h.endOffset, h.colorArgb)
          }
        }
      }

      // Delete absorbed same-color highlights
      for (const h of toDelete) {
        await _deleteHighlight(h)
      }

      // Insert the final merged highlight
      await _insertHighlight(lineId, mergedStart, mergedEnd, colorArgb)
      console.log('[highlight] applied:', { lineId, mergedStart, mergedEnd, colorArgb, total: highlightsByLine.value.get(lineId)?.length })
    } catch (err) {
      console.error('[highlight] error:', err)
      throw err
    }
  }

  // ── Clear highlight ───────────────────────────────────────────────────────

  /**
   * Remove all highlights (regardless of color) in [startOffset, endOffset] on lineId.
   * Highlights that partially overlap are trimmed; highlights fully covered are deleted;
   * highlights that fully span the erased range are split into two stubs.
   */
  async function clearHighlight(
    lineId: number,
    startOffset: number,
    endOffset: number,
  ): Promise<void> {
    if (startOffset >= endOffset) return

    const existing = [...(highlightsByLine.value.get(lineId) ?? [])]
    const overlapping = existing.filter(
      (h) => h.startOffset < endOffset && h.endOffset > startOffset,
    )

    for (const h of overlapping) {
      if (h.startOffset >= startOffset && h.endOffset <= endOffset) {
        // Fully inside erased range → delete
        await _deleteHighlight(h)
      } else if (h.startOffset < startOffset && h.endOffset > endOffset) {
        // Fully spans erased range → split
        await _deleteHighlight(h)
        await _insertHighlight(lineId, h.startOffset, startOffset, h.colorArgb)
        await _insertHighlight(lineId, endOffset, h.endOffset, h.colorArgb)
      } else if (h.startOffset < startOffset) {
        // Overlaps on the left → trim right
        await _updateHighlight(h, h.startOffset, startOffset, h.colorArgb)
      } else {
        // Overlaps on the right → trim left
        await _updateHighlight(h, endOffset, h.endOffset, h.colorArgb)
      }
    }
  }

  return {
    highlightsByLine,
    isLoaded,
    getHighlightsForLine,
    applyHighlight,
    clearHighlight,
    reloadHighlights: loadHighlights,
  }
}
