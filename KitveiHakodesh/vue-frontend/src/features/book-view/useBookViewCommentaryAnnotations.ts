/**
 * Line-level commentary data shared by BOTH commentary panels: highlights, notes,
 * word-link anchors, TOC paths, and the export-only book line renderer.
 *
 * Shared, not per panel, because the two panels are anchored to the same line and
 * therefore need the same annotations - two instances would double every notes and
 * highlights query for identical results. Rendering is the exception and lives in
 * useCommentaryPanelSlot: its cache is keyed partly by the panel's own search
 * query, so one shared cache would thrash.
 *
 * These composables are also deliberately hoisted above the v-if on CommentaryView
 * so their watchers and caches survive a panel being unmounted and remounted.
 */
import { useCommentaryHighlights } from './commentary/useCommentaryHighlights'
import { useCommentaryNotes } from './commentary/useCommentaryNotes'
import { useCommentaryTocPaths } from './commentary/useCommentaryTocPaths'
import { useWordLinkAnchors } from './lines/useWordLinkAnchors'
import { useCopyExportData } from './lines/useCopyExportData'
import { useBooksDataStore } from '@/stores/booksDataStore'
import { useBookViewLineRenderer } from './lines/useBookViewLineRenderer'
import { buildBookExportHtml } from './lines/useBookViewLineCopyMenu'
import { computed } from 'vue'
import type { useSettingsStore } from '@/stores/settingsStore'

type Line = { id: number; lineIndex: number; content: string | null }
type GroupsForDisplay = Parameters<typeof useCommentaryHighlights>[0] extends () => infer T ? T : never

export function useBookViewCommentaryAnnotations(
  /**
   * Every group either panel currently displays. Pass the UNION of the panels'
   * groupsForDisplay: they agree on the real lines, but each injects a placeholder
   * for its own pinned book, and commentaryTocPaths must resolve a path for both.
   */
  groupsForDisplay: () => GroupsForDisplay,
  selectedSectionLineIds: () => number[] | null,
  lines: () => Line[],
  bookTitle: string | undefined,
  settings: ReturnType<typeof useSettingsStore>,
) {
  const diacriticsStateForExport = computed(() => settings.diacriticsState)

  const { getHighlightsForLine, applyHighlight, clearHighlight } =
    useCommentaryHighlights(groupsForDisplay)

  const { getNotesForLine, scheduleNotesLoad, loadNotesForLines, createNote, updateNote, deleteNote } =
    useCommentaryNotes(groupsForDisplay)

  // Word-level link anchors for commentary lines (they are source lines of their own
  // links, e.g. a Mishnah Berurah line citing Chosen Mishpat). Schedule-driven from
  // CommentaryView's virtualizer watcher, same as notes.
  const { getWordLinkAnchorsForLine, scheduleWordLinkAnchorsLoad, loadWordLinkAnchorsForLines } =
    useWordLinkAnchors()

  // What copy-with-notes needs beyond the rendered markup — built here because this
  // is where the commentary's lazy note/citation stores live. CommentaryView receives
  // the two entry points as props and hands them to its copy menu.
  const booksDataStore = useBooksDataStore()
  const { prepareForLines, prepareForRenderedHtml, resolveWordLinkTarget } = useCopyExportData({
    loadNotes: loadNotesForLines,
    loadWordLinkAnchors: loadWordLinkAnchorsForLines,
    getWordLinkAnchorsForLine,
    getBookTitle: (targetBookId) => booksDataStore.allBooksMap.get(targetBookId)?.title ?? '',
  })

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
    getWordLinkAnchorsForLine, scheduleWordLinkAnchorsLoad,
    prepareForLines, prepareForRenderedHtml, resolveWordLinkTarget,
    commentaryTocPaths,
    buildExportHtml,
  }
}
