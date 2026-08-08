<script setup lang="ts">
/**
 * Renders one commentary panel: the per-panel state from its CommentaryPanel slot,
 * plus the line-level data both panels share.
 *
 * It exists so BookViewPage does not carry two near-identical thirty-prop
 * CommentaryView blocks - the bottom and side panels differ only in which slot they
 * are handed. The inner instance is re-exposed as `view` because useBookView drives
 * each panel through its CommentaryView methods (scrollToGroup, restore, ...).
 */
import { computed, ref } from 'vue'
import CommentaryView from './CommentaryView.vue'
import type { CommentaryPanel } from './useCommentaryPanelSlot'
import type { PinnedCommentaryGroup } from '../bookViewTypes'
import type { Highlight } from '../lines/useBookViewHighlights'
import type { Note } from '../lines/useBookViewNotes'

const props = defineProps<{
  panel: CommentaryPanel
  /** Re-key the inner view when the book changes, as the single panel used to. */
  bookId: number | undefined
  /** True when the side filter tree is currently bound to THIS panel. */
  filterVisible: boolean
  /** True when the search bar is open and targeting THIS panel. */
  searchActive: boolean

  // ── Shared across both panels (see useBookViewCommentaryAnnotations) ───────
  selectedLineId: number | null
  loading: boolean
  loadError?: boolean
  getHighlightsForLine: (lineId: number) => Highlight[]
  applyHighlight: (lineId: number, startOffset: number, endOffset: number, colorArgb: number) => void
  clearHighlight: (lineId: number, startOffset: number, endOffset: number) => void
  getNotesForLine: (lineId: number) => Note[]
  scheduleNotesLoad: (lineIds: number[]) => void
  scheduleWordLinkAnchorsLoad?: (lineIds: number[]) => void
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
  'toggle-filter-panel': []
  'toggle-search': []
}>()

const view = ref<InstanceType<typeof CommentaryView> | null>(null)

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
  <CommentaryView
    ref="view"
    :key="bookId"
    :slot-name="panel.slot"
    :selected-line-id="selectedLineId"
    :groups="panel.visibleGroups.value"
    :loading="loading"
    :load-error="loadError"
    :pinned-group="pinnedGroup"
    :filter-visible="filterVisible"
    :get-highlights-for-line="getHighlightsForLine"
    :apply-highlight="applyHighlight"
    :clear-highlight="clearHighlight"
    :get-notes-for-line="getNotesForLine"
    :schedule-notes-load="scheduleNotesLoad"
    :schedule-word-link-anchors-load="scheduleWordLinkAnchorsLoad"
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
    @toggle-filter-panel="emit('toggle-filter-panel')"
    @toggle-search="emit('toggle-search')"
    @open-book="(id, lineIndex) => emit('open-book', id, lineIndex)"
  />
</template>
