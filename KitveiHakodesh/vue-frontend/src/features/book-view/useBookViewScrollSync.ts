/**
 * Syncs the active TOC entry, the tab's tocPath (the breadcrumb), and commentary
 * auto-select to the book's scroll position.
 *
 * Reader scrolls sync immediately. Programmatic jumps (TOC click, search match,
 * restore) sync where they come to rest: the lines scroller's moveend.
 *
 * The auto-selected commentary (not the line highlight) loads once per scroll:
 * where a reader scroll stops (the lines scroller's moveend) or where a jump settles.
 *
 * Auto-select has two modes, chosen by what the commentary is anchored on when the
 * reader starts scrolling. There is no setting: the mode follows the reader's last selection.
 *
 *   SECTION mode - the anchor is a TOC entry's line (the reader selected a TOC line).
 *     The commentary stays on that TOC section while the text scrolls inside it, and
 *     moves to the next (or previous) TOC entry only when the text crosses into it.
 *     It never drops to a single line. See followedTocSectionAt.
 *
 *   LINE mode - the anchor is a plain line. The commentary follows the top line.
 *     TOC lines are skipped (the next non-TOC line is used): a TOC line as the anchor
 *     would switch to SECTION mode on its own.
 *
 * Only an explicit selection changes the mode: a TOC line -> SECTION, a plain line -> LINE.
 * Lines above the first TOC entry have no section, so they are LINE behaviour.
 */
import { onScopeDispose, ref, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { useBookViewStore } from '@/stores/bookViewStore'
import type { Ref } from 'vue'
import type { LinesTable } from './lines/linesTable'
import type { TocEntry } from '@/webview-host/queries.types'
export function useBookViewScrollSync(
  lines: () => LinesTable,
  activeTocEntryId: Ref<number | undefined>,
  activeAltTocEntryId: Ref<number | undefined>,
  selectedLineId: Ref<number | null>,
  commentaryLineId: Ref<number | null>,
  getActiveTocEntry: (lineIndex: number) => TocEntry | null,
  getTocEntryById: (id: number) => TocEntry | null,
  getActiveAltTocEntry: (lineIndex: number) => TocEntry | null,
  getTocPath: (entry: TocEntry) => string,
  /**
   * Fresh read of which line is at the top of the book view RIGHT NOW, straight
   * from the scroller — null while it has no rendered rows. The settle pass below
   * reads it rather than the indices recorded from the last event, so it syncs
   * where the jump came to rest (see readCurrentPosition in useBookViewLinesScroll).
   */
  readLivePosition: () => { lineIndex: number; fullLineIndex: number } | null,
  tocEntries: () => TocEntry[],
) {
  const paneNavigation = usePaneNavigation()
  const bookViewStore = useBookViewStore()
  const { autoSelectTopLine } = storeToRefs(bookViewStore)

  const currentScrollLineIndex = ref(0)
  const currentFullLineIndex = ref(0)
  /**
   * The line whose commentary auto-select owes, once the reader's scroll stops.
   * Loading on every scroll step reloaded the panel ~30 times for 30 wheel steps;
   * the reader only reads the commentary where the scroll comes to rest.
   */
  let pendingCommentaryLineId: number | null = null

  function commitPendingCommentary() {
    const lineId = pendingCommentaryLineId
    pendingCommentaryLineId = null
    if (lineId == null || !autoSelectTopLine.value) return
    commentaryLineId.value = lineId
  }

  function cancelPendingCommentary() {
    pendingCommentaryLineId = null
  }

  /**
   * The lines scroller stopped moving (its moveend). A jump syncs where it came to
   * rest, read fresh from the scroller; a reader scroll has synced as it went, and
   * auto-select now loads the commentary where they stopped.
   */
  function onLinesMoveEnd() {
    if (isJumpPending) {
      isJumpPending = false
      const live = readLivePosition()
      if (live) applyPositionSync(live.lineIndex, live.fullLineIndex, false)
    }
    commitPendingCommentary()
  }

  /**
   * Bumped every time the READER moves the view (not when the app jumps it).
   * The search panel watches this to re-anchor Enter/Shift+Enter to wherever the
   * reader has scrolled to — see useBookViewSearchPanel.
   */
  const userScrollTick = ref(0)

  /** A programmatic jump moved the view and its resting place is not synced yet. */
  let isJumpPending = false

  /**
   * A position-derived sync must never move the highlight BETWEEN entries that
   * start on the same line — a parent header and its first child often do (a
   * parasha and its first daf, say), and the position alone cannot tell them
   * apart: getActiveTocEntry always resolves to the last of them. Overwriting
   * here would flip every click on such a parent to its child once the jump rests. When
   * the reader's current entry shares the derived entry's line, their choice
   * stands.
   */
  function derivedEntrySupersedesActive(derived: TocEntry): boolean {
    if (activeTocEntryId.value == null) return true
    const active = getTocEntryById(activeTocEntryId.value)
    return !(active && active.lineIndex != null && active.lineIndex === derived.lineIndex)
  }

  /**
   * While the commentary is anchored on a TOC entry's line (a TOC line was selected,
   * or the previous scroll step followed one), the entry whose section holds
   * `lineIndex`: the last entry at the followed level or higher that starts at or
   * before it. Null when the commentary is on a plain line, or the line is above
   * every such entry (then the plain-line behaviour applies).
   *
   * The followed level is the level of the TOC line the READER selected, and it is
   * kept while the commentary moves along. Crossing into a higher-level heading (a
   * chapter, while following sections) lands on that heading, but the level stays:
   * the next step follows sections again instead of the whole chapter. The level is
   * remembered per landed entry (followedLevel) and forgotten when the reader selects
   * anything else.
   */
  function followedTocSectionAt(lineIndex: number): { entry: TocEntry; level: number } | null {
    const anchorId = pendingCommentaryLineId ?? commentaryLineId.value
    if (anchorId == null) return null
    const entries = tocEntries()
    const anchor = entries.find((e) => e.lineId === anchorId)
    if (!anchor) return null
    const level = followedLevel?.lineId === anchorId ? followedLevel.level : anchor.level
    let found: TocEntry | null = null
    for (const e of entries) {
      if (e.lineIndex == null || e.lineIndex > lineIndex) continue
      if (e.level <= level) found = e
    }
    return found ? { entry: found, level } : null
  }

  /** The level being followed, and the entry line the commentary landed on at that level. */
  let followedLevel: { lineId: number; level: number } | null = null

  // The ids of the lines that are TOC entries, rebuilt only when the TOC changes.
  let tocLineIdsFor: TocEntry[] | null = null
  let tocLineIdSet = new Set<number>()
  function tocLineIds(): Set<number> {
    const entries = tocEntries()
    if (entries !== tocLineIdsFor) {
      tocLineIdsFor = entries
      tocLineIdSet = new Set(entries.flatMap((e) => (e.lineId != null ? [e.lineId] : [])))
    }
    return tocLineIdSet
  }

  /** Sync everything that tracks the reading position to this line index. */
  function applyPositionSync(lineIndex: number, fullLineIndex: number, isUserScroll: boolean) {
    const entry = getActiveTocEntry(lineIndex)
    if (entry && entry.id !== activeTocEntryId.value && derivedEntrySupersedesActive(entry)) {
      activeTocEntryId.value = entry.id
      paneNavigation.updateActiveTab({ tocPath: getTocPath(entry) })
    } else if (!entry && activeTocEntryId.value != null) {
      // Above the first heading (Home on a book with an intro): no entry covers
      // this line, so the old highlight must go rather than linger.
      activeTocEntryId.value = undefined
      paneNavigation.updateActiveTab({ tocPath: undefined })
    }

    // The alt tree tracks the reader independently — it has its own entries, so
    // the main entry's id means nothing to it. The breadcrumb stays on the main TOC.
    const altEntry = getActiveAltTocEntry(lineIndex)
    if (altEntry?.id !== activeAltTocEntryId.value) {
      activeAltTocEntryId.value = altEntry?.id
    }

    if (!autoSelectTopLine.value) return

    // The reader is following a TOC section (a TOC line is the commentary's anchor):
    // the commentary stays on that section until the text crosses into another entry,
    // then moves to THAT entry — not to a single line.
    const section = followedTocSectionAt(lineIndex)
    const sectionEntry = section?.entry
    if (section && sectionEntry?.lineId != null) {
      followedLevel = { lineId: sectionEntry.lineId, level: section.level }
      selectedLineId.value = sectionEntry.lineId
      pendingCommentaryLineId = sectionEntry.lineId
      if (!isUserScroll) commitPendingCommentary()
      return
    }

    // Plain-line mode: while the READER scrolls, a TOC line is skipped. Passing over
    // one must not make it the commentary's anchor, and the TOC line IS what switches
    // to section mode. A jump that comes to rest ON a TOC line (a click in the TOC
    // panel) is the opposite: the reader chose that entry, so it becomes the anchor.
    const headingIds = tocLineIds()
    const skipHeadings = isUserScroll
    let line = lines().at(fullLineIndex)
    for (let i = fullLineIndex + 1; line && skipHeadings && headingIds.has(line.id) && i < lines().length; i++) {
      line = lines().at(i)
    }
    if (line && line.content !== null && !(skipHeadings && headingIds.has(line.id))) {
      selectedLineId.value = line.id
      pendingCommentaryLineId = line.id
      // A reader scroll loads where it stops (onLinesMoveEnd); a programmatic
      // jump reaches here only once it has settled, so it IS the stop.
      if (!isUserScroll) commitPendingCommentary()
    }
  }

  function onLinesScrolled(lineIndex: number, fullLineIndex: number, isUserScroll = false) {
    currentScrollLineIndex.value = lineIndex
    currentFullLineIndex.value = fullLineIndex

    // A reader scroll is a real position the moment it happens — sync now, and
    // drop any pending programmatic settle so a stale jump position cannot land
    // on top of where the reader just moved.
    if (isUserScroll) {
      userScrollTick.value++
      isJumpPending = false
      applyPositionSync(lineIndex, fullLineIndex, true)
      return
    }

    // A programmatic jump is only a real position where it comes to rest (a landing,
    // then perhaps the nudge that brings a search mark into view): synced at the
    // scroller's moveend, so the TOC never flicks through an intermediate place.
    isJumpPending = true
  }

  /**
   * Forces a one-time TOC path sync for a known line index without waiting for
   * a scroll event. Used by session restore so the breadcrumb is populated
   * immediately when a book reloads to its saved position.
   *
   * The saved index is the first row with ANY pixel showing, while the live
   * derivation (readCurrentPosition) skips a top sliver up to the landing gap.
   * Left at a section boundary with a sliver of the previous line above, the
   * saved index is that previous line — whose entry is the adjacent section (the
   * other amud). So when the view has already landed on the saved row, the fresh
   * read decides, exactly as the settle pass would; it is accepted only at the
   * saved row or the one after it (the sliver case), never a pre-landing read.
   */
  function syncTocPathForLineIndex(savedLineIndex: number) {
    const live = readLivePosition()
    const landed = live != null && live.lineIndex >= savedLineIndex && live.lineIndex <= savedLineIndex + 1
    const lineIndex = landed ? live.lineIndex : savedLineIndex
    const entry = getActiveTocEntry(lineIndex)
    if (entry) {
      activeTocEntryId.value = entry.id
      paneNavigation.updateActiveTab({ tocPath: getTocPath(entry) })
    }
    activeAltTocEntryId.value = getActiveAltTocEntry(lineIndex)?.id
  }

  // The reader selected something themselves (a click, a keyboard move, section
  // navigation) while a scroll's commentary was still owed. Their choice wins: the
  // owed load must not overwrite it at moveend, and must not decide the mode
  // (followedTocSectionAt reads the owed value first). Our own commit clears the
  // owed value BEFORE it assigns, so it never cancels itself. Sync flush: the next
  // scroll step must already see the cancelled value.
  watch(
    commentaryLineId,
    (id) => {
      if (pendingCommentaryLineId != null && id !== pendingCommentaryLineId) cancelPendingCommentary()
      // A selection of the reader's own sets the level to follow from scratch.
      if (followedLevel != null && id !== followedLevel.lineId) followedLevel = null
    },
    { flush: 'sync' },
  )

  /**
   * The reader selected a line themselves (a click in the text): the level to follow
   * starts from scratch, even when the commentary already sits on that very line and
   * nothing above fires.
   */
  function forgetFollowedLevel() {
    followedLevel = null
  }

  /**
   * The reader chose a TOC entry in the TOC panel. That click only scrolls — it never
   * selects a line — so the choice is recorded here as the owed anchor: the jump that
   * follows reads it, takes the entry's own level, and lands on its section.
   */
  function onTocEntryChosen(lineId: number | null) {
    followedLevel = null
    if (lineId != null && autoSelectTopLine.value) pendingCommentaryLineId = lineId
  }

  watch(autoSelectTopLine, (enabled) => {
    if (!enabled) cancelPendingCommentary()
  })

  // The owed load must not outlive the view: firing after teardown sets commentaryLineId
  // and starts a commentary fetch for a panel that no longer exists.
  onScopeDispose(cancelPendingCommentary)

  return {
    forgetFollowedLevel,
    onTocEntryChosen,
    currentScrollLineIndex,
    currentFullLineIndex,
    userScrollTick,
    onLinesScrolled,
    onLinesMoveEnd,
    syncTocPathForLineIndex,
  }
}
