/**
 * Restores per-book view state from IDB on mount: scroll position, selected line,
 * zoom, auto-sync setting, and - for EACH commentary panel - whether it was open,
 * its scroll position, filter and pinned book.
 *
 * Two sources feed this, and the per-panel state comes from both:
 *   - `BookState`    per tab + book. Always used.
 *   - `LastReadState` per book, tab-independent. Used when the reader turned on
 *     "remember my last place in a book", or when this tab has been in this book
 *     before (so reopening the same book in a NEW tab starts clean when the
 *     setting is off).
 *
 * Also exposes the initial scroll refs that BookViewLinesContent needs before the
 * IDB read completes.
 */
import { nextTick, ref } from 'vue'
import { useTabStore } from '@/stores/tabStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useSettingsStore } from '@/stores/settingsStore'
import { COMMENTARY_SLOTS } from './bookViewTypes'
import type { Ref } from 'vue'
import type {
  CommentaryPanelPersistState,
  CommentaryPanelPersistStates,
  CommentarySlot,
  CommentaryTreeState,
  CommentaryTreeStatePersist,
  TocPersistState,
  PinnedCommentaryGroup,
} from './bookViewTypes'
import {
  hasCommentaryCheckState,
  hydrateCommentaryCheckState,
  isCommentaryBookUnchecked,
} from './commentary/uncheckedCommentaryBooks'

/** The slice of a commentary panel that session restore writes into. */
export interface RestorableCommentaryPanel {
  scopeKey: string
  visible: Ref<boolean>
  scrollIndex: Ref<number | null>
  scrollOffset: Ref<number | null>
  fraction: Ref<number>
  treeState: CommentaryTreeState
  restorePin: (group: PinnedCommentaryGroup) => void
}

/** What session restore needs in order to bring the TOC panel back. */
export interface RestorableToc {
  visible: Ref<boolean>
  /** Applies the saved inner state to the tree component, once it exists. */
  apply: (saved: TocPersistState) => void
  restoreAltStructure: (id: number | null | undefined) => void
}

export function useBookViewSessionRestore(
  tabId: string,
  bookId: number | undefined,
  openTocLineIndex: number | undefined,
  panels: Record<CommentarySlot, RestorableCommentaryPanel>,
  selectedLineId: Ref<number | null>,
  commentaryLineId: Ref<number | null>,
  toc?: RestorableToc,
) {
  const tabStore = useTabStore()
  const bookViewStore = useBookViewStore()
  const settingsStore = useSettingsStore()

  const initialLineIndex = ref<number | undefined>(openTocLineIndex)
  const initialScrollTop = ref<number | undefined>()
  const initialScrollOffset = ref<number>(0)
  const scrollStateReady = ref(true)
  const idbResolved = ref(false)

  /** Resolved per-panel saved state, filled by _applyRestoreData. */
  let _restoredPanels: CommentaryPanelPersistStates = {}

  const _idbPromise: Promise<void> = bookId == null
    ? Promise.resolve()
    : (() => {
        return Promise.all([
          tabStore.getBookViewState(tabId, bookId),
          tabStore.getLastReadPos(bookId),
        ]).then(([bookSaved, lastRead]) => {
          _restoredPanels = _applyRestoreData(bookSaved ?? null, lastRead ?? null)
        })
      })()

  _idbPromise.then(() => { idbResolved.value = true })

  function _applyRestoreData(
    bookSaved: Awaited<ReturnType<typeof tabStore.getBookViewState>>,
    lastRead: Awaited<ReturnType<typeof tabStore.getLastReadPos>>,
  ): CommentaryPanelPersistStates {
    // When resumeLastRead is off, only use lastRead as a fallback if there is
    // already a per-tab bookSaved entry — i.e. the user has visited this book
    // in this tab before. Opening the same book in a brand-new tab should start
    // from scratch when the setting is disabled.
    const useLastRead = settingsStore.resumeLastRead || bookSaved != null
    const restoredLineId = bookSaved?.selectedLineId ?? (useLastRead ? lastRead?.selectedLineId : undefined)

    // Text zoom falls back to last-read like everything else — the commentary
    // panels' zoom already travels inside commentaryPanels, so without the
    // fallback reopening a book restored their zoom but reset the text's.
    const restoredZoom = bookSaved?.zoom ?? (useLastRead ? lastRead?.zoom : undefined)
    if (restoredZoom != null) bookViewStore.setLinesZoom(tabId, bookId!, restoredZoom)
    if (bookSaved?.autoSelectTopLine != null) {
      bookViewStore.autoSelectTopLine = bookSaved.autoSelectTopLine
    }

    // Per-panel state: the tab's own save wins, then the book's last-read save.
    // Merged per slot rather than per field so a panel never restores half of one
    // save and half of the other.
    const resolved: CommentaryPanelPersistStates = {}
    for (const slot of COMMENTARY_SLOTS) {
      const saved =
        bookSaved?.commentaryPanels?.[slot] ??
        (useLastRead ? lastRead?.commentaryPanels?.[slot] : undefined)
      if (saved) resolved[slot] = saved
    }

    for (const slot of COMMENTARY_SLOTS) {
      const saved = resolved[slot]
      if (!saved) continue
      const panel = panels[slot]

      // Check-tree first: _applyFilterState derives every isChecked from it.
      // Skipped when the scope already has live state — the reader has been in
      // this panel during this session (a tab switch remounts the view), and
      // their current ticks outrank the last save.
      if (!hasCommentaryCheckState(panel.scopeKey)) {
        hydrateCommentaryCheckState(panel.scopeKey, saved.checkState)
      }

      if (saved.filterState) _applyFilterState(panel, saved.filterState)

      // Reopen a panel that was open, whether or not a line came back with it.
      // It used to require a restored line, which silently dropped every panel
      // for a reader who had opened one while scrolling without ever clicking a
      // line. A panel with no line simply shows its empty state, and the
      // hasCommentaries watcher in useBookViewCommentaryPanel closes it outright
      // when the book has no commentary at all.
      if (saved.visible) panel.visible.value = true
    }

    // The TOC panel. Unlike the commentary panels this does NOT wait on a restored
    // line — the TOC is how a reader finds a line in the first place, so a book
    // opened without one should still come back with the panel they left open.
    const savedToc = bookSaved?.toc ?? (useLastRead ? lastRead?.toc : undefined)
    if (savedToc && toc) {
      toc.restoreAltStructure(savedToc.altStructureId)
      if (savedToc.visible) {
        toc.visible.value = true
        // The tree only mounts once the panel is visible, so its inner state has
        // to wait for that render.
        nextTick(() => toc.apply(savedToc))
      }
    }

    if (restoredLineId != null) {
      selectedLineId.value = restoredLineId
      // Deliberately NOT commentaryLineId: that would trigger a booksDataStore load
      // (GET_ALL_CATEGORIES + GET_ALL_BOOKS) before line chunks have finished
      // loading. Each panel sets it when it first becomes visible.
    }

    if (openTocLineIndex == null) {
      const scrollIndex = bookSaved?.scrollIndex ?? (useLastRead ? lastRead?.scrollIndex : undefined)
      const scrollOffset = bookSaved?.scrollOffset ?? (useLastRead ? lastRead?.scrollOffset : undefined)
      if (scrollIndex != null) {
        initialScrollTop.value = scrollIndex
        initialScrollOffset.value = scrollOffset ?? 0
      }
    }

    return resolved
  }

  function _applyFilterState(panel: RestorableCommentaryPanel, saved: CommentaryTreeStatePersist) {
    panel.treeState.searchQuery = saved.searchQuery
    panel.treeState.tokens = saved.tokens ?? []
    // isChecked is a cache derived from the check-tree, not a source of truth, so
    // it is re-derived rather than read back from the saved list. The tree itself
    // was just hydrated from `checkState` above, so this now reproduces the ticks
    // the reader left — and it also covers books this line has that the saved list
    // never did.
    panel.treeState.visibilityList = saved.visibilityList.map((item) => ({
      ...item,
      isChecked: !isCommentaryBookUnchecked(
        panel.scopeKey,
        item.sectionLabel,
        item.subSectionLabel,
        item.bookId,
      ),
    }))
  }

  function _seedPanel(slot: CommentarySlot, saved: CommentaryPanelPersistState) {
    const panel = panels[slot]

    if (saved.zoom != null) bookViewStore.setCommentaryZoom(tabId, bookId!, slot, saved.zoom)
    if (saved.fraction != null) panel.fraction.value = saved.fraction
    if (saved.pinnedGroup != null) panel.restorePin(saved.pinnedGroup)

    // Publish the persisted scroll position into the live panel refs. This is the
    // ONLY thing session restore does for commentary scroll — the actual restore is
    // owned entirely by onCommentaryPanelMounted (useBookViewCommentaryPanel), which
    // fires when the panel becomes visible (set by _applyRestoreData above), reads
    // these seeded refs, claims restore intent, and restores.
    // Seeding also makes hasSavedScrollPos true before the first groups load, so
    // setupGroupReloadScroll defers instead of jumping to the pinned group.
    // (Seeding happens in this microtask, before the panel path's setTimeout(0), so
    // the ordering is guaranteed.)
    if (saved.scrollIndex == null || saved.scrollOffset == null) return
    // A user line-click before the IDB read resolves sets commentaryLineId and
    // invalidates any saved position — don't overwrite that with stale values.
    if (commentaryLineId.value != null) return
    panel.scrollIndex.value = saved.scrollIndex
    panel.scrollOffset.value = saved.scrollOffset
  }

  async function restore(): Promise<void> {
    if (bookId == null) return
    await _idbPromise
    for (const slot of COMMENTARY_SLOTS) {
      const saved = _restoredPanels[slot]
      if (saved) _seedPanel(slot, saved)
    }
  }

  return { initialLineIndex, initialScrollTop, initialScrollOffset, scrollStateReady, idbResolved, restore }
}
