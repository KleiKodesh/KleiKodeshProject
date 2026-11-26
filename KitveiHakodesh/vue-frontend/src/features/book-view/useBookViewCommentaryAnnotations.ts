/**
 * Line-level commentary data shared by BOTH commentary panels: highlights, notes,
 * word-link anchors and TOC paths.
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
import { computed } from 'vue'
import { commentaryGroupKey } from './commentary/useCommentary'
import { useCommentaryHighlights } from './commentary/useCommentaryHighlights'
import { useCommentaryNotes } from './commentary/useCommentaryNotes'
import { useWordLinkAnchors } from './lines/useWordLinkAnchors'
import { useLineNotes } from './lines/useLineNotes'
import { createLineDecorationsBatch } from './lines/lineDecorationsBatch'
import { useCopyExportData } from './lines/useCopyExportData'

type GroupsForDisplay = Parameters<typeof useCommentaryHighlights>[0] extends () => infer T ? T : never

export function useBookViewCommentaryAnnotations(
  /**
   * Every group either panel currently displays. Pass the UNION of the panels'
   * groupsForDisplay: they agree on the real lines, but each injects a placeholder
   * for its own pinned book (a placeholder has no chapter path).
   */
  groupsForDisplay: () => GroupsForDisplay,
) {

  const { getHighlightsForLine, applyHighlight, clearHighlight } =
    useCommentaryHighlights(groupsForDisplay)

  const { getNotesForLine, scheduleNotesLoad, loadNotesForLines, createNote, updateNote, deleteNote } =
    useCommentaryNotes(groupsForDisplay)

  // Word-link anchors and inline notes are asked for together: the panel's window watcher
  // schedules both in the same turn, so ONE service call answers both (the same batch the
  // book's own lines view uses) instead of two calls and two connections per window.
  const lineDecorations = createLineDecorationsBatch()

  // Word-level link anchors for commentary lines (they are source lines of their own
  // links, e.g. a Mishnah Berurah line citing Chosen Mishpat). Schedule-driven from
  // CommentaryView's virtualizer watcher, same as notes.
  const { getWordLinkAnchorsForLine, getWordLinkTargetBookTitle, scheduleWordLinkAnchorsLoad, loadWordLinkAnchorsForLines } =
    useWordLinkAnchors(undefined, lineDecorations.anchorsFor)

  // Inline notes (line_note) of commentary lines - schedule-driven, same as word links.
  const { getLineNotesForLine, scheduleLineNotesLoad } = useLineNotes(undefined, lineDecorations.notesFor)

  // What copy-with-notes needs beyond the rendered markup — built here because this
  // is where the commentary's lazy note/citation stores live. CommentaryView receives
  // the two entry points as props and hands them to its copy menu.
  const { prepareForLines, prepareForRenderedHtml, resolveWordLinkTarget } = useCopyExportData({
    loadNotes: loadNotesForLines,
    loadWordLinkAnchors: loadWordLinkAnchorsForLines,
    getWordLinkAnchorsForLine,
    getBookTitle: getWordLinkTargetBookTitle,
  })

  // The chapter path of each group's header arrives WITH the group (Core resolves it in
  // getCommentaryList), so a header is complete on its first paint. This only keys it by group
  // identity (commentaryGroupKey), NOT by bookId: one book can yield several groups.
  const commentaryTocPaths = computed(() => {
    const paths = new Map<string, string>()
    for (const group of groupsForDisplay()) if (group.tocPath) paths.set(commentaryGroupKey(group), group.tocPath)
    return paths
  })

  return {
    getHighlightsForLine, applyHighlight, clearHighlight,
    getNotesForLine, scheduleNotesLoad, createNote, updateNote, deleteNote,
    getWordLinkAnchorsForLine, scheduleWordLinkAnchorsLoad,
    getLineNotesForLine, scheduleLineNotesLoad,
    prepareForLines, prepareForRenderedHtml, resolveWordLinkTarget,
    commentaryTocPaths,
  }
}
