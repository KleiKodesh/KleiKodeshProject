/**
 * Commentary annotation and rendering for the book view.
 *
 * Owns highlights, notes, content rendering, TOC paths for commentary entries,
 * and the export-only book line renderer. These composables are intentionally
 * hoisted here (above the v-if toggle on CommentaryView) so their watchers
 * and caches survive the panel being unmounted and remounted.
 */
import { computed } from 'vue'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useCommentaryHighlights } from './commentary/useCommentaryHighlights'
import { useCommentaryNotes } from './commentary/useCommentaryNotes'
import { useCommentaryRender } from './commentary/useCommentaryRender'
import { useCommentaryTocPaths } from './commentary/useCommentaryTocPaths'
import { useWordLinkAnchors } from './lines/useWordLinkAnchors'
import { useBookViewLineRenderer } from './lines/useBookViewLineRenderer'
import { buildBookExportHtml } from './lines/useBookViewLineCopyMenu'
import type { useSettingsStore } from '@/stores/settingsStore'

type Line = { id: number; lineIndex: number; content: string | null }
type GroupsForDisplay = Parameters<typeof useCommentaryHighlights>[0] extends () => infer T ? T : never

export function useBookViewCommentaryAnnotations(
  groupsForDisplay: () => GroupsForDisplay,
  selectedSectionLineIds: () => number[] | null,
  lines: () => Line[],
  bookTitle: string | undefined,
  settings: ReturnType<typeof useSettingsStore>,
  tabId: string,
  bookId: number | undefined,
) {
  const bookViewStore = useBookViewStore()
  const diacriticsStateForExport = computed(() => settings.diacriticsState)

  const { getHighlightsForLine, applyHighlight, clearHighlight } =
    useCommentaryHighlights(groupsForDisplay)

  const { getNotesForLine, scheduleNotesLoad, createNote, updateNote, deleteNote } =
    useCommentaryNotes(groupsForDisplay)

  // Use a pane-scoped zoom getter so split view pane 2 reads its own zoom
  // rather than the store-level computed (which always resolves to the active tab).
  const getCommentaryZoom = () =>
    bookId != null ? bookViewStore.getCommentaryZoom(tabId, bookId) : 100

  // Word-level link anchors for commentary lines (they are source lines of their own
  // links, e.g. a Mishnah Berurah line citing Chosen Mishpat). Schedule-driven from
  // CommentaryView's virtualizer watcher, same as notes.
  const { getWordLinkAnchorsForLine, scheduleWordLinkAnchorsLoad } = useWordLinkAnchors()

  const { commentaryFontPx, renderContent, setCurrentMark } = useCommentaryRender(
    groupsForDisplay,
    getCommentaryZoom,
    getHighlightsForLine,
    getNotesForLine,
    getWordLinkAnchorsForLine,
  )

  const { commentaryTocPaths } = useCommentaryTocPaths(
    groupsForDisplay,
    () => {
      const ids = selectedSectionLineIds()
      return ids != null && ids.length > 1
    },
  )

  const { lineContent: renderLineForExport } = useBookViewLineRenderer(
    settings,
    diacriticsStateForExport,
    () => ({ getHighlightsForLine: undefined, getNotesForLine }),
  )

  function buildExportHtml(): string {
    return buildBookExportHtml(lines(), bookTitle ?? '', renderLineForExport, getNotesForLine)
  }

  return {
    getHighlightsForLine, applyHighlight, clearHighlight,
    getNotesForLine, scheduleNotesLoad, createNote, updateNote, deleteNote,
    scheduleWordLinkAnchorsLoad,
    commentaryFontPx, renderContent, setCurrentMark,
    commentaryTocPaths,
    buildExportHtml,
  }
}
