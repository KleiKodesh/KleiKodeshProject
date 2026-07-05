/**
 * Commentary annotation and rendering for the book view.
 *
 * Owns highlights, notes, content rendering, TOC paths for commentary entries,
 * and the export-only book line renderer. These composables are intentionally
 * hoisted here (above the v-if toggle on CommentaryView) so their watchers
 * and caches survive the panel being unmounted and remounted.
 */
import { computed } from 'vue'
import { useCommentaryHighlights } from './commentary/useCommentaryHighlights'
import { useCommentaryNotes } from './commentary/useCommentaryNotes'
import { useCommentaryRender } from './commentary/useCommentaryRender'
import { useCommentaryTocPaths } from './commentary/useCommentaryTocPaths'
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
) {
  const diacriticsStateForExport = computed(() => settings.diacriticsState)

  const { getHighlightsForLine, applyHighlight, clearHighlight } =
    useCommentaryHighlights(groupsForDisplay)

  const { getNotesForLine, scheduleNotesLoad, createNote, updateNote, deleteNote } =
    useCommentaryNotes(groupsForDisplay)

  const { commentaryFontPx, renderContent, setCurrentMark } = useCommentaryRender(
    groupsForDisplay,
    getHighlightsForLine,
    getNotesForLine,
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
    commentaryFontPx, renderContent, setCurrentMark,
    commentaryTocPaths,
    buildExportHtml,
  }
}
