<script setup lang="ts">
/**
 * Renders one commentary panel: the per-panel state from its CommentaryPanel slot,
 * plus the line-level data every panel shares.
 *
 * It exists so BookViewPage does not carry one near-identical thirty-prop
 * CommentaryView block per panel - the panels differ only in which slot they are
 * handed. The inner instance is re-exposed as `view` because useBookView drives
 * each panel through its CommentaryView methods (scrollToGroup, restore, ...).
 *
 * A SIDE panel's filter dropdown is rendered here, clipped to its own column.
 * The bottom panel's is not: it runs the full book-view body height, which a
 * dropdown clipped to the bottom pane cannot do, so BookViewPage renders that one.
 */
import { computed, ref } from 'vue'
import CommentaryView from './CommentaryView.vue'
import CommentaryTreePanel from './CommentaryTreePanel.vue'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { isSideCommentarySlot } from '../bookViewTypes'
import type { CommentaryPanel } from './useCommentaryPanelSlot'
import type { CommentaryGroup } from './useCommentary'
import type { PinnedCommentaryGroup } from '../bookViewTypes'
import type { Highlight } from '../lines/useBookViewHighlights'
import type { Note } from '../lines/useBookViewNotes'
import type { WordLinkTarget } from '../lines/wordLinkAnchors'
import type { WordLinkTargetContent } from '../lines/wordLinkExport'

const props = defineProps<{
  panel: CommentaryPanel
  /** Re-key the inner view when the book changes, as the single panel used to. */
  bookId: number | undefined
  /** True when the search bar is open and targeting THIS panel. */
  searchActive: boolean
  /** Every commentary book of this book — the filter tree's rows. */
  filterGroups: CommentaryGroup[]

  // ── Shared across every panel (see useBookViewCommentaryAnnotations) ───────
  selectedLineId: number | null
  loading: boolean
  loadError?: boolean
  getHighlightsForLine: (lineId: number) => Highlight[]
  applyHighlight: (lineId: number, startOffset: number, endOffset: number, colorArgb: number) => void
  clearHighlight: (lineId: number, startOffset: number, endOffset: number) => void
  getNotesForLine: (lineId: number) => Note[]
  scheduleNotesLoad: (lineIds: number[]) => void
  scheduleWordLinkAnchorsLoad?: (lineIds: number[]) => void
  // Copy-with-notes needs the notes and citations of lines never scrolled into view,
  // plus the target lines the citations point at — see useCopyExportData.
  prepareExportData?: (lineIds: number[]) => Promise<void>
  prepareExportTargets?: (html: string) => Promise<void>
  resolveWordLinkTarget?: (target: WordLinkTarget) => WordLinkTargetContent | undefined
  requestContentPriority?: (lineIds: number[]) => void
  createNote: (lineId: number, startOffset: number, endOffset: number, quote: string) => Promise<Note>
  updateNote: (note: Note, newText: string) => Promise<void>
  deleteNote: (note: Note) => Promise<void>
  commentaryTocPaths: Map<string, string>
}>()

const emit = defineEmits<{
  close: []
  'navigate-section': [direction: 'next' | 'prev', bookId: number]
  'open-book': [bookId: number, lineIndex: number]
  'toggle-search': []
}>()

const view = ref<InstanceType<typeof CommentaryView> | null>(null)

// ── This panel's filter tree ────────────────────────────────────────────────
// A side column hosts its own tree as a popup over itself: it must not borrow the
// book view's side panel (which belongs to the TOC) and must not resize the text.
// The bottom panel's tree is a full-height column instead, so BookViewPage renders
// that one - a popup clipped to the bottom panel's height would be unusable.
const showFilterPopup = computed(
  () => isSideCommentarySlot(props.panel.slot) && props.panel.filterOpen.value,
)

const popupRef = ref<HTMLElement | null>(null)

const { justClosed } = useDropdownClose(popupRef, () => props.panel.closeFilter(), {
  // getFilterButtonEl is a call, not a ref, so it needs the computed wrapper.
  toggleButton: computed(() => view.value?.getFilterButtonEl?.() ?? null),
  // The tree holds a text input the user types into, and this app runs in a
  // WebView where focus routinely moves into an iframe. Blur-closing it would
  // shut the tree mid-search; click-outside is the only close we want.
  closeOnBlur: false,
})

// Without this the pointerdown that closes the popup is followed by a click on
// the filter button that reopens it, and the button reads as dead.
function onToggleFilter() {
  if (justClosed.value) return
  props.panel.toggleFilter()
}

/** Clicking a book in this panel's tree scrolls THIS panel to it. */
function scrollToBook(targetBookId: number) {
  view.value?.scrollToGroup(targetBookId)
}

// Search results are only shown while the bar is open AND aimed at this panel;
// the query itself survives so reopening the bar restores it.
const searchQuery = computed(() => (props.searchActive ? props.panel.search.query.value : ''))
const currentMatchFlatIndex = computed(() =>
  props.searchActive ? props.panel.search.currentMatchFlatIndex.value : undefined,
)
const currentMatchOccurrence = computed(() =>
  props.searchActive ? props.panel.search.currentMatchOccurrence.value : undefined,
)

const pinnedGroup = computed<PinnedCommentaryGroup | null>(
  () => props.panel.pinnedCommentaryGroup.value,
)

defineExpose({ view })
</script>

<template>
  <div class="panel-host">
    <CommentaryView
      ref="view"
      :key="bookId"
      :slot-name="panel.slot"
      :selected-line-id="selectedLineId"
      :groups="panel.visibleGroups.value"
      :loading="loading"
      :load-error="loadError"
      :pinned-group="pinnedGroup"
      :filter-visible="panel.filterOpen.value"
      :get-highlights-for-line="getHighlightsForLine"
      :apply-highlight="applyHighlight"
      :clear-highlight="clearHighlight"
      :get-notes-for-line="getNotesForLine"
      :schedule-notes-load="scheduleNotesLoad"
      :schedule-word-link-anchors-load="scheduleWordLinkAnchorsLoad"
      :prepare-export-data="prepareExportData"
      :prepare-export-targets="prepareExportTargets"
      :resolve-word-link-target="resolveWordLinkTarget"
      :request-content-priority="requestContentPriority"
      :has-saved-scroll-pos="panel.scrollIndex.value != null"
      :create-note="createNote"
      :update-note="updateNote"
      :delete-note="deleteNote"
      :commentary-font-px="panel.commentaryFontPx.value"
      :render-content="panel.renderContent"
      :set-current-mark="panel.setCurrentMark"
      :commentary-toc-paths="commentaryTocPaths"
      :search-query="searchQuery"
      :current-match-flat-index="currentMatchFlatIndex"
      :current-match-occurrence="currentMatchOccurrence"
      @close="emit('close')"
      @navigate-section="(direction, id) => emit('navigate-section', direction, id)"
      @scroll="panel.onScroll"
      @toggle-filter-panel="onToggleFilter"
      @toggle-search="emit('toggle-search')"
      @open-book="(id, lineIndex) => emit('open-book', id, lineIndex)"
    />
    <div v-if="showFilterPopup" ref="popupRef" class="filter-popup">
      <CommentaryTreePanel
        :groups="filterGroups"
        :tree-state="panel.treeState"
        :scope-key="panel.scopeKey"
        :scroll-to-book="scrollToBook"
        @close="panel.closeFilter()"
      />
    </div>
  </div>
</template>

<style scoped>
/*
  The containing block for the filter popup, and what clips it: a side panel's
  tree stays over its OWN column, never spilling onto the text or resizing it.
*/
.panel-host {
  position: relative;
  height: 100%;
  min-height: 0;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

/* CommentaryView sizes itself with height:100%, which resolves against this
   host; the flex rule keeps it filling the column if that ever changes. */
.panel-host > :deep(.commentary-view) {
  flex: 1;
  min-height: 0;
}

/* Hangs below the sticky nav (32px), on the RTL start edge under the filter
   button that opens it.

   Chrome follows the app's floating-panel convention (FullTextSearchAdvancedPanel,
   BookViewNoteBubble): 1px border, 8px radius on all four corners, soft shadow.
   Inset from the column's edge and foot so every corner is free to round. */
.filter-popup {
  position: absolute;
  top: 32px;
  bottom: 6px;
  inset-inline-start: 6px;
  z-index: 60;
  display: flex;
  max-width: calc(100% - 12px);
  overflow: hidden;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  box-shadow: 0 4px 16px rgb(0 0 0 / 24%);
  --tree-bg: var(--bg-secondary);
}
</style>
