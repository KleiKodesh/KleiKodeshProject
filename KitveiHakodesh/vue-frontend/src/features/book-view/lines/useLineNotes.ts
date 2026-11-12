/**
 * Lazy, viewport-driven loader for inline notes (line_note, Otzaria schema 7+).
 *
 * Same shape as useWordLinkAnchors: whenever the mounted lines change, a 100ms debounce
 * fires ONE batched query for the line ids not yet requested. On DBs without line_note
 * the seforimApi layer short-circuits to [] after the first probe, so this is a no-op there.
 */
import { onScopeDispose, shallowRef, triggerRef, watch, type Ref } from 'vue'
import { getLineNotesForLines } from '@/webview-host/seforimApi'
import type { LineNote } from '@/webview-host/queries.types'
import { cleanHebrewText } from '@/utils/hebrewTextCleaning'
import { useSettingsStore } from '@/stores/settingsStore'
import { useLinkPreviewTooltip } from './useWordLinkTooltip'
import { LINE_NOTE_SELECTOR, notesForMarker } from './lineNotes'
import { highlightSearchMatches } from '@/utils/searchMatchHighlight'

const EMPTY: LineNote[] = []

/**
 * Two wiring modes, like useWordLinkAnchors: pass `getVisibleLineIds` and loading is
 * watch-driven (lines view); omit it and call `scheduleLineNotesLoad` from the view's
 * virtualizer watcher (commentary view).
 */
export function useLineNotes(
  getVisibleLineIds?: () => number[],
  /** The transport; the lines view passes its decorations batch (one call with the anchors). */
  fetchNotes: (lineIds: number[]) => Promise<LineNote[]> = getLineNotesForLines,
) {
  // Shallow, mutated in place, one trigger per batch or eviction (no Map copy each time).
  const notesByLine = shallowRef(new Map<number, LineNote[]>())
  // Line ids already sent to (or in flight toward) the DB - each is queried once.
  const requested = new Set<number>()
  const inFlight = new Set<number>()
  let debounceTimer: ReturnType<typeof setTimeout> | null = null
  onScopeDispose(() => {
    if (debounceTimer !== null) clearTimeout(debounceTimer)
    debounceTimer = null
  })

  let latest: number[] = []
  function scheduleLoad(lineIds: number[]) {
    latest = lineIds
    if (!lineIds.some((id) => id > 0 && !requested.has(id))) return
    if (debounceTimer !== null) clearTimeout(debounceTimer)
    debounceTimer = setTimeout(() => {
      debounceTimer = null
      // The latest window: it may have moved during the debounce.
      const pending = latest.filter((id) => id > 0 && !requested.has(id))
      if (!pending.length) return
      for (const id of pending) {
        requested.add(id)
        inFlight.add(id)
      }
      void load(pending)
    }, 100)
  }

  async function load(lineIds: number[]) {
    try {
      const rows = await fetchNotes(lineIds)
      if (!rows.length) return
      const byLine = new Map<number, LineNote[]>()
      for (const row of rows) {
        let list = byLine.get(row.lineId)
        if (!list) byLine.set(row.lineId, (list = []))
        list.push(row)
      }
      // One reactive write per batch, so the lines re-render once.
      for (const [lineId, list] of byLine) notesByLine.value.set(lineId, list)
      triggerRef(notesByLine)
    } catch {
      // DB not ready - un-mark so the lines are retried on the next visible-set change.
      for (const id of lineIds) requested.delete(id)
    } finally {
      for (const id of lineIds) inFlight.delete(id)
    }
  }

  if (getVisibleLineIds) watch(getVisibleLineIds, (ids) => scheduleLoad(ids), { immediate: true })

  function getLineNotesForLine(lineId: number): LineNote[] {
    return notesByLine.value.get(lineId) ?? EMPTY
  }

  /** Drops the notes of lines whose text was evicted; they load again with the text. */
  function evictLineNotesForLines(lineIds: number[]) {
    let changed = false
    for (const id of lineIds) {
      if (inFlight.has(id)) continue
      requested.delete(id)
      if (notesByLine.value.delete(id)) changed = true
    }
    if (changed) triggerRef(notesByLine)
  }

  return { getLineNotesForLine, evictLineNotesForLines, scheduleLineNotesLoad: scheduleLoad }
}

/**
 * Hover preview of inline notes, shared by every view that renders seforim.db lines:
 * hovering a footnote marker of the text, or the end-of-line marker of notes without
 * one, shows the note in the link-preview panel (selectable, scrollable, stays open while
 * the pointer is inside). `rowOf` names the rendered line a marker sits in and its id; the
 * marker's ordinal in that line picks the note (lineNotes.ts). A click only previews.
 */
export function useLineNotesTooltip(
  scrollerEl: Ref<HTMLElement | null>,
  rowOf: (marker: Element) => { row: Element; lineId: number } | null,
  getLineNotesForLine: (lineId: number) => LineNote[],
  /**
   * The words a search matched in a line's notes, for the line a search result opened:
   * they are marked in the note's preview as they are in the line. A hit ON a footnote
   * opens its source line with the note popped open - and the matched words are in the
   * note, not in the line.
   */
  searchTermsForLine?: (lineId: number) => string[] | undefined,
) {
  const settingsStore = useSettingsStore()
  const {
    linkPreview: lineNoteTooltip,
    closeLinkPreview: closeLineNoteTooltip,
    keepOpen: keepLineNoteTooltipOpen,
    releaseOpen: releaseLineNoteTooltip,
    beginSelection: beginLineNoteTooltipSelection,
    openLinkPreview: openLineNoteTooltip,
  } = useLinkPreviewTooltip(scrollerEl, {
    selector: LINE_NOTE_SELECTOR,
    linkKey: (marker) => {
      const at = rowOf(marker)
      return at ? `${at.lineId}:${[...at.row.querySelectorAll(LINE_NOTE_SELECTOR)].indexOf(marker)}` : null
    },
    preview: async (marker) => {
      const at = rowOf(marker)
      const notes = at ? notesForMarker(marker, at.row, getLineNotesForLine(at.lineId)) : []
      if (!notes.length) return null
      const terms = at ? searchTermsForLine?.(at.lineId) ?? [] : []
      const marked = (body: string) => terms.reduce((html, term) => highlightSearchMatches(html, term), body)
      const html = notes
        .map((note) => `<div class="line-note-body">${marked(cleanHebrewText(note.body, settingsStore.cleanTextFlags))}</div>`)
        .join('')
      return { bookTitle: '', tocPath: '', html }
    },
    activate: () => {},
  })
  return { lineNoteTooltip, closeLineNoteTooltip, keepLineNoteTooltipOpen, releaseLineNoteTooltip, beginLineNoteTooltipSelection, openLineNoteTooltip }
}
