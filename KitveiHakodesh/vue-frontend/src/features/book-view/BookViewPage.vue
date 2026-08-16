<script setup lang="ts">
import { ref, computed, watch, nextTick, inject } from 'vue'
import { useResizeObserver } from '@vueuse/core'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { useBookView } from './useBookView'
import { useBookViewStore } from '@/stores/bookViewStore'
import { exportToWord as bridgeExportToWord } from '@/webview-host/bridge'
import BookViewToolbar from './BookViewToolbar.vue'
import SplitPane from '@/components/SplitPane.vue'
import BookViewLinesContent from './lines/BookViewLinesContent.vue'
import BookViewSearchBar from './BookViewSearchBar.vue'
import BookViewSidePanel from './BookViewSidePanel.vue'
import BookViewTocTree from './toc/BookViewTocTree.vue'
import CommentaryTreePanel from './commentary/CommentaryTreePanel.vue'
import CommentaryPanelHost from './commentary/CommentaryPanelHost.vue'
import { slotForSearchMode, isSideCommentarySlot, SIDE_COMMENTARY_SLOTS } from './bookViewTypes'
import type { CommentarySlot } from './bookViewTypes'

const toolbarRef = ref<InstanceType<typeof BookViewToolbar> | null>(null)
const linesContentRef = ref<InstanceType<typeof BookViewLinesContent> | null>(null)
const searchBarRef = ref<InstanceType<typeof BookViewSearchBar> | null>(null)
const bottomHostRef = ref<InstanceType<typeof CommentaryPanelHost> | null>(null)
const sideHostRef = ref<InstanceType<typeof CommentaryPanelHost> | null>(null)
const sideLeftHostRef = ref<InstanceType<typeof CommentaryPanelHost> | null>(null)
const bookViewRoot = ref<HTMLElement | null>(null)
const bookViewStore = useBookViewStore()
const paneId = inject<1 | 2>('paneId', 1)

// Track the shell's own width instead of the viewport width so that in split
// view each pane responds to its own size, not the full window size.
const shellWidth = ref(window.innerWidth)
useResizeObserver(bookViewRoot, ([entry]) => { shellWidth.value = entry!.contentRect.width })
const isWideScreen = computed(() => shellWidth.value >= 650)
/** Share of the split row the book text keeps when both side columns are open. */
const MIN_LINES_FRACTION = 0.2
const isSidePanelWideScreen = computed(() => shellWidth.value >= 520)
const sidePanelIsOverlay = computed(() => !isSidePanelWideScreen.value)

const {
  toolbarPosition, toolbarVisible,
  searchHighlightLineIndex, searchHighlightQuery, searchHighlightSnippet, searchHighlightTerms,
  searchVisible,
  selectedLineId, searchMode,
  activeTocEntryId, activeAltTocEntryId,
  tocVisible,
  sidePanelVisible, sidePanelToggleButtonEl,
  panels, anyCommentaryVisible, openCommentarySlots, commentaryPersistState,
  tabId, bookId, lines, prioritise, hasCommentaries, hasRelatedBooks, hasToc,
  bookHasTeamim,
  filterGroups, staticFilterGroups, commentaryLoading, commentaryLoadError, requestContentPriority,
  tocEntries, tocSearchTree, selectedAltTocSection, tocLoading, tocError,
  altTocLabelMap, selectedSectionLineIds, manualSelectionLineIds,
  getHighlightsForLine, applyHighlight, clearHighlight,
  getNotesForLine, scheduleNotesLoad, createNote, updateNote, deleteNote,
  scheduleWordLinkAnchorsLoad,
  commentaryTocPaths,
  currentScrollLineIndex,
  scrollStateReady, idbResolved, initialLineIndex, initialScrollTop, initialScrollOffset,
  flashOpenLine,
  activeMatchCount, activeMatchIdx, contentSearch,
  onLinesScrolled, onTocSelect, onAltTocSelect,
  onLineSelected, onNavigateSection, navigateToAdjacentTocSection,
  openBookTarget,
  openContentSearch, openCommentarySearch, toggleSearch,
  onQueryChange, onSearchNext, onSearchPrev, onModeChange,
  toggleTocPanel, closeSidePanel,
  ensureStaticFilterGroupsLoaded, staticFilterGroupsLoaded,
  getActiveTocEntry, getTocPath,
  buildExportHtml,
  bookTitle,
} = useBookView(
  () => toolbarRef.value,
  () => linesContentRef.value,
  () => searchBarRef.value,
  {
    bottom: () => bottomHostRef.value?.view ?? null,
    side: () => sideHostRef.value?.view ?? null,
    'side-left': () => sideLeftHostRef.value?.view ?? null,
  },
)

// The side panels need a pane wide enough to sit beside the text. Rendering is
// gated on both flags so a pane that narrows never shows a cramped column, and
// the watcher below closes them so their toggles and the search bar agree.
const sideCommentaryOpen = computed(() => panels.side.visible.value && isWideScreen.value)
const sideLeftCommentaryOpen = computed(() => panels['side-left'].visible.value && isWideScreen.value)

// Re-keys the lines scroller: opening or closing EITHER side column re-wraps the
// text, so both belong in the key (see the capture watcher below).
const sideColumnsKey = computed(() => `${sideCommentaryOpen.value}:${sideLeftCommentaryOpen.value}`)

// Everything a commentary panel needs that is NOT per panel. Bound with v-bind on
// every host so the shared list is written once.
const commentarySharedProps = computed(() => ({
  bookId,
  selectedLineId: selectedLineId.value,
  // The filter tree's rows. Shared: every panel filters the same book list, and
  // which of those rows are checked is per panel (treeState + scopeKey).
  filterGroups: filterGroups.value,
  loading: commentaryLoading.value,
  loadError: commentaryLoadError.value,
  getHighlightsForLine,
  applyHighlight,
  clearHighlight,
  getNotesForLine,
  scheduleNotesLoad,
  scheduleWordLinkAnchorsLoad,
  requestContentPriority,
  createNote,
  updateNote,
  deleteNote,
  commentaryTocPaths: commentaryTocPaths.value,
}))

// All toolbar props that do not depend on its position, so the three placements
// (top / side / bottom) each spread one object instead of repeating the list.
const toolbarProps = computed(() => ({
  searchVisible: searchVisible.value,
  tocVisible: tocVisible.value,
  hasToc: hasToc.value,
  hasCommentaries: hasCommentaries.value,
  hasRelatedBooks: hasRelatedBooks.value,
  tabId,
  bookId,
  bookHasTeamim: bookHasTeamim.value,
  filterGroups: staticFilterGroups.value,
  relatedBooksLoaded: staticFilterGroupsLoaded.value,
  currentScrollLineIndex: currentScrollLineIndex.value,
  lines: lines.value,
  onRelatedBooksOpen: ensureStaticFilterGroupsLoaded,
  bottomCommentaryVisible: panels.bottom.visible.value,
  sideCommentaryVisible: sideCommentaryOpen.value,
  sideLeftCommentaryVisible: sideLeftCommentaryOpen.value,
  canUseSidePanel: isWideScreen.value,
}))

// The search bar shows one query at a time: the content query, or the query of
// whichever commentary panel it is currently aimed at.
const activeSearchQuery = computed(() => {
  const slot = slotForSearchMode(searchMode.value)
  return slot ? panels[slot].search.query.value : contentSearch.query.value
})

// ── The bottom panel's filter tree ──────────────────────────────────────────
// A dropdown, exactly like the side panels' (see CommentaryPanelHost) - it floats
// over the content and never reflows the layout. The one difference is its height:
// it fills the whole book-view body rather than stopping at the bottom panel. That
// is why it is rendered here and not in the host: SplitPane and .side-lines both
// clip, so a dropdown mounted inside the bottom panel could only be as tall as
// that panel.
const bottomFilterButtonEl = computed(
  () => bottomHostRef.value?.view?.getFilterButtonEl?.() ?? null,
)

/** Clicking a book in the bottom tree scrolls the bottom panel to it. */
function scrollBottomTreeSelectionIntoView(targetBookId: number) {
  bottomHostRef.value?.view?.scrollToGroup(targetBookId)
}

const bottomFilterRef = ref<HTMLElement | null>(null)
const { justClosed: bottomFilterJustClosed } = useDropdownClose(
  bottomFilterRef,
  () => panels.bottom.closeFilter(),
  {
    toggleButton: bottomFilterButtonEl,
    // The tree holds a search input, and focus moving into a WebView iframe must
    // not shut it mid-typing (same reasoning as the side dropdowns).
    closeOnBlur: false,
  },
)

function onToggleBottomFilter() {
  if (bottomFilterJustClosed.value) return
  panels.bottom.toggleFilter()
}

// Horizontal placement: aligned to the start edge of its own PANEL, like every
// other panel's dropdown. A side panel's needs no measuring - its column is the
// panel, so `inset-inline-start: 0` already means the panel's edge. This one is
// positioned against .content-area, which spans the whole body, so it has to
// measure where the bottom panel's own start edge falls (an open side column
// pushes it inward).
const contentAreaRef = ref<HTMLElement | null>(null)
const bottomFilterInlineStart = ref(0)

function measureBottomFilterOffset() {
  const panel = bottomHostRef.value?.$el as HTMLElement | undefined
  const container = contentAreaRef.value
  if (!panel?.getBoundingClientRect || !container) return
  const panelRect = panel.getBoundingClientRect()
  const containerRect = container.getBoundingClientRect()
  // RTL: inline-start is the physical RIGHT edge, so the offset is measured from
  // the container's right edge to the panel's right edge.
  // +6px so the dropdown is inset from the panel edge rather than flush to it,
  // leaving all four corners free to round (see .bottom-filter-dropdown).
  bottomFilterInlineStart.value = Math.max(0, containerRect.right - panelRect.right) + 6
}

// Measured when it opens, and again whenever anything moves the button sideways: a
// side column opening or closing, one of their dividers being dragged, or the pane
// resizing. The bottom panel's own divider only moves it vertically, which no
// longer matters now that the dropdown spans the full height.
watch(
  [
    () => panels.bottom.filterOpen.value,
    sideColumnsKey,
    () => panels.side.fraction.value,
    () => panels['side-left'].fraction.value,
    shellWidth,
  ],
  async ([open]) => {
    if (!open) return
    await nextTick()
    measureBottomFilterOffset()
  },
  { immediate: true },
)

// ── Divider drag: the side commentary columns ────────────────────────────────
// One handler pair for both columns; the dragging slot decides which edge the
// fraction is measured from (RTL: 'side' hugs the right edge, 'side-left' the left).
const splitContainer = ref<HTMLElement | null>(null)
const draggingSideSlot = ref<CommentarySlot | null>(null)

function onSplitDividerPointerDown(e: PointerEvent, slot: CommentarySlot) {
  draggingSideSlot.value = slot
  ;(e.target as HTMLElement).setPointerCapture(e.pointerId)
}
function onSplitPointerMove(e: PointerEvent) {
  const slot = draggingSideSlot.value
  if (!slot || !splitContainer.value) return
  const rect = splitContainer.value.getBoundingClientRect()
  const distance = slot === 'side' ? rect.right - e.clientX : e.clientX - rect.left
  // With both columns open the text keeps at least MIN_LINES_FRACTION of the row,
  // so dragging one column wide can never squeeze the text out between them.
  const otherSlot: CommentarySlot = slot === 'side' ? 'side-left' : 'side'
  const otherOpen = otherSlot === 'side' ? sideCommentaryOpen.value : sideLeftCommentaryOpen.value
  const max = otherOpen ? 1 - MIN_LINES_FRACTION - panels[otherSlot].fraction.value : 0.9
  panels[slot].fraction.value = Math.min(max, Math.max(0.1, distance / rect.width))
}
function onSplitPointerUp() {
  draggingSideSlot.value = null
}

// ── Divider drag: TOC / filter side panel ────────────────────────────────────
// sidePanelResizeArea wraps the side panel + divider + content-area so we can
// measure the total available width for fraction calculations.
const sidePanelResizeArea = ref<HTMLElement | null>(null)
const isSidePanelDragging = ref(false)
const sidePanelFraction = ref<number | null>(null)

function onSidePanelDividerPointerDown(e: PointerEvent) {
  isSidePanelDragging.value = true
  ;(e.target as HTMLElement).setPointerCapture(e.pointerId)
}
function onSidePanelResizePointerMove(e: PointerEvent) {
  if (!isSidePanelDragging.value || !sidePanelResizeArea.value) return
  const rect = sidePanelResizeArea.value.getBoundingClientRect()
  // Panel is on the physical right (RTL: inline-start = right), measure from right edge
  sidePanelFraction.value = Math.min(0.5, Math.max(0.1, (rect.right - e.clientX) / rect.width))
}
function onSidePanelResizePointerUp() {
  isSidePanelDragging.value = false
}

// ── Panel toggles ────────────────────────────────────────────────────────────

function toggleCommentaryPanel(slot: CommentarySlot) {
  if (isSideCommentarySlot(slot) && !isWideScreen.value) return
  panels[slot].visible.value = !panels[slot].visible.value
}

// Works in both modes — hosted drives Word through the Office PIA, dev through the
// service. There is deliberately no isHosted guard: isHosted is TRUE in dev, so it
// never blocked anything here, and the bridge call it let through could only reject
// (silently, via the catch) until dev got a real export path.
async function onExportToWord() {
  const html = buildExportHtml()
  await bridgeExportToWord(html, bookTitle ?? '').catch(() => {})
}

// Opening or closing EITHER side commentary column re-wraps the text, so the lines
// scroller is re-keyed and restored from the position captured just before the
// swap. Without the capture the remounted instance would re-apply
// initialLineIndex/initialScrollTop — values frozen at session-restore time — and
// jump back to a stale position (or to the top when nothing was saved).
watch(sideColumnsKey, () => {
  // Session restore opens the side panel BEFORE idbResolved flips (the visible
  // watchers flush first), so this fires against a lines instance that has not
  // applied its restore yet - capturing would read {0,0} and overwrite the seeded
  // initialScrollTop, landing the whole view at the top. Skip: the seeded values
  // are exactly what the remounted instance should restore from. (The old
  // single-panel code dodged this by deferring the layout flip until after
  // restoreSession, which let the pre-flip instance finish its stage-1 scroll.)
  if (!idbResolved.value) return
  const pos = linesContentRef.value?.captureScrollPos?.()
  if (!pos) return
  initialLineIndex.value = undefined
  initialScrollTop.value = pos.scrollIndex
  initialScrollOffset.value = pos.scrollOffset
})

// A pane too narrow for the side columns closes them rather than leaving one open
// but unrendered, so their toggle buttons and the search bar's mode cycle stay
// truthful. Watching visible TOO (not just width changes) matters for session
// restore on an already-narrow shell: isWideScreen never changes there, but restore
// flips visible true - without the clamp that panel stays logically open while never
// rendering (phantom search mode, held backfill gate, no way to close it).
watch(
  [() => SIDE_COMMENTARY_SLOTS.map((slot) => panels[slot].visible.value), isWideScreen],
  ([, wide]) => {
    if (wide) return
    for (const slot of SIDE_COMMENTARY_SLOTS) panels[slot].visible.value = false
  },
)

// Both columns opened at their saved fractions can together leave the text too
// little room. Shrink them proportionally the moment that happens - the drag
// clamp only covers the column being dragged.
watch([sideCommentaryOpen, sideLeftCommentaryOpen], ([rightOpen, leftOpen]) => {
  if (!rightOpen || !leftOpen) return
  const total = panels.side.fraction.value + panels['side-left'].fraction.value
  const max = 1 - MIN_LINES_FRACTION
  if (total <= max) return
  const scale = max / total
  panels.side.fraction.value *= scale
  panels['side-left'].fraction.value *= scale
})

watch(() => bookViewStore.openSearchSignal, (signal) => { if (signal.paneId === paneId) openContentSearch() })
watch(() => bookViewStore.toggleCommentaryPanelSignal, (signal) => {
  if (signal.paneId === paneId) toggleCommentaryPanel(signal.slot)
})
watch(() => bookViewStore.toggleTocPanelSignal, (signal) => { if (signal.paneId === paneId) toggleTocPanel() })
</script>

<template>
  <div class="book-view" ref="bookViewRoot">
    <!-- Top toolbar -->
    <BookViewToolbar
      v-if="toolbarVisible && toolbarPosition === 'top'"
      ref="toolbarRef"
      v-bind="toolbarProps"
      @toggle-bottom-commentary="toggleCommentaryPanel('bottom')"
      @toggle-side-commentary="toggleCommentaryPanel('side')"
      @toggle-side-left-commentary="toggleCommentaryPanel('side-left')"
      @toggle-search="toggleSearch"
      @toggle-toc="toggleTocPanel"
      @export-to-word="onExportToWord"
      @navigate-to-next-section="navigateToAdjacentTocSection('next')"
      @navigate-to-previous-section="navigateToAdjacentTocSection('previous')"
    />
    <!-- Middle row: side toolbar + main area (RTL: first child = physical right) -->
    <div class="body-row">
      <BookViewToolbar
        v-if="toolbarVisible && (toolbarPosition === 'right' || toolbarPosition === 'left')"
        ref="toolbarRef"
        v-bind="toolbarProps"
        :class="toolbarPosition === 'left' ? 'toolbar-order-end' : ''"
        @toggle-bottom-commentary="toggleCommentaryPanel('bottom')"
        @toggle-side-commentary="toggleCommentaryPanel('side')"
        @toggle-side-left-commentary="toggleCommentaryPanel('side-left')"
        @toggle-search="toggleSearch"
        @toggle-toc="toggleTocPanel"
        @export-to-word="onExportToWord"
        @navigate-to-next-section="navigateToAdjacentTocSection('next')"
        @navigate-to-previous-section="navigateToAdjacentTocSection('previous')"
      />

      <!--
        main-area: flex row containing the inline side panel (when wide) + content-area.
        When the side panel is in inline mode this wrapper also captures pointer events
        for the divider drag — its bounding rect is used to compute sidePanelFraction.
      -->
      <div
        ref="sidePanelResizeArea"
        class="main-area"
        @pointermove="onSidePanelResizePointerMove"
        @pointerup="onSidePanelResizePointerUp"
      >
        <!--
          Inline side panel — rendered as a sibling of content-area inside main-area.
          RTL: first child in a flex row = physical right, so the panel appears on the right.
        -->
        <template v-if="sidePanelVisible && !sidePanelIsOverlay">
          <BookViewSidePanel
            :toggle-button-el="sidePanelToggleButtonEl"
            :is-overlay="false"
            :style="sidePanelFraction !== null ? { width: `${sidePanelFraction * 100}%` } : undefined"
            @close="closeSidePanel"
          >
            <BookViewTocTree
              :active-toc-entry-id="activeTocEntryId"
              :active-alt-toc-entry-id="activeAltTocEntryId"
              :toc-entries="tocEntries"
              :toc-search-tree="tocSearchTree"
              :selected-alt-toc-section="selectedAltTocSection"
              :loading="tocLoading"
              :error="tocError"
              @select="onTocSelect"
              @alt-select="onAltTocSelect"
            />
          </BookViewSidePanel>
          <div class="sash sash-v" @pointerdown="onSidePanelDividerPointerDown" />
        </template>

        <!-- content-area: always fills remaining horizontal space -->
        <div ref="contentAreaRef" class="content-area">
          <!--
            One nested layout, always: each side commentary is a column beside the
            text, the bottom commentary a row beneath it. All three are independent
            panels, so any combination can be open and no branch excludes another.
            RTL: first child is physically right, last child physically left.
          -->
          <div
            ref="splitContainer"
            class="side-by-side"
            @pointermove="onSplitPointerMove"
            @pointerup="onSplitPointerUp"
          >
            <template v-if="sideCommentaryOpen">
              <div class="side-commentary" :style="{ width: `${panels.side.fraction.value * 100}%` }">
                <CommentaryPanelHost
                  ref="sideHostRef"
                  :panel="panels.side"
                  v-bind="commentarySharedProps"
                  :search-active="searchVisible && searchMode === 'commentary-side'"
                  @close="panels.side.visible.value = false"
                  @navigate-section="(direction, id) => onNavigateSection('side', direction, id)"
                  @toggle-filter-panel="panels.side.toggleFilter()"
                  @toggle-search="openCommentarySearch('side')"
                  @open-book="openBookTarget"
                />
              </div>
              <div class="sash sash-v" @pointerdown="onSplitDividerPointerDown($event, 'side')" />
            </template>

            <div class="side-lines">
              <SplitPane v-model="panels.bottom.fraction.value" :bottom-visible="panels.bottom.visible.value">
                <template #top>
                  <BookViewLinesContent
                    v-if="scrollStateReady"
                    :key="sideColumnsKey"
                    ref="linesContentRef"
                    :lines="lines"
                    :prioritise="prioritise"
                    :alt-toc-label-map="altTocLabelMap"
                    :selected-line-id="selectedLineId"
                    :commentary-visible="anyCommentaryVisible"
                    :commentary-persist-state="commentaryPersistState"
                    :initial-line-index="initialLineIndex"
                    :initial-scroll-index="initialScrollTop"
                    :initial-scroll-offset="initialScrollOffset"
                    :flash-line-on-open="flashOpenLine"
                    :idb-resolved="idbResolved"
                    :search-highlight-line-index="searchHighlightLineIndex"
                    :search-highlight-query="searchHighlightQuery"
                    :search-highlight-snippet="searchHighlightSnippet"
                    :search-highlight-terms="searchHighlightTerms"
                    :search-bar-visible="searchVisible"
                    :search-query="searchVisible && searchMode === 'content' ? contentSearch.query.value : ''"
                    :current-match-line-index="searchVisible && searchMode === 'content' ? contentSearch.currentMatchLineIndex.value : undefined"
                    :current-match-occurrence="searchVisible && searchMode === 'content' ? contentSearch.currentMatchOccurrence.value : undefined"
                    :get-active-toc-entry="getActiveTocEntry"
                    :get-toc-path="getTocPath"
                    :selected-section-line-ids="selectedSectionLineIds"
                    :multi-select-line-ids="manualSelectionLineIds"
                    @scrolled="onLinesScrolled"
                    @line-selected="onLineSelected"
                    @ctrl-f="openContentSearch"
                  />
                </template>
                <template #bottom>
                  <CommentaryPanelHost
                    ref="bottomHostRef"
                    :panel="panels.bottom"
                    v-bind="commentarySharedProps"
                    :search-active="searchVisible && searchMode === 'commentary-bottom'"
                    @close="panels.bottom.visible.value = false"
                    @navigate-section="(direction, id) => onNavigateSection('bottom', direction, id)"
                    @toggle-filter-panel="onToggleBottomFilter()"
                    @toggle-search="openCommentarySearch('bottom')"
                    @open-book="openBookTarget"
                  />
                </template>
              </SplitPane>
            </div>

            <!-- RTL: last child sits physically left, opposite the 'side' column. -->
            <template v-if="sideLeftCommentaryOpen">
              <div
                class="sash sash-v"
                @pointerdown="onSplitDividerPointerDown($event, 'side-left')"
              />
              <div
                class="side-commentary"
                :style="{ width: `${panels['side-left'].fraction.value * 100}%` }"
              >
                <CommentaryPanelHost
                  ref="sideLeftHostRef"
                  :panel="panels['side-left']"
                  v-bind="commentarySharedProps"
                  :search-active="searchVisible && searchMode === 'commentary-side-left'"
                  @close="panels['side-left'].visible.value = false"
                  @navigate-section="(direction, id) => onNavigateSection('side-left', direction, id)"
                  @toggle-filter-panel="panels['side-left'].toggleFilter()"
                  @toggle-search="openCommentarySearch('side-left')"
                  @open-book="openBookTarget"
                />
              </div>
            </template>
          </div>

          <!-- Search bar (floats inside content-area) -->
          <BookViewSearchBar
            ref="searchBarRef"
            :visible="searchVisible"
            :toolbar-visible="toolbarVisible"
            :toolbar-position="toolbarPosition"
            :match-count="activeMatchCount"
            :current-match="activeMatchIdx"
            :commentary-visible="anyCommentaryVisible"
            :open-commentary-slots="openCommentarySlots"
            :mode="searchMode"
            :query="activeSearchQuery"
            @close="searchVisible = false"
            @query-change="onQueryChange"
            @next="onSearchNext"
            @prev="onSearchPrev"
            @mode-change="onModeChange"
          />

          <!-- Overlay side panel: only rendered when screen is too narrow for inline mode -->
          <BookViewSidePanel
            v-if="sidePanelVisible && sidePanelIsOverlay"
            :toggle-button-el="sidePanelToggleButtonEl"
            :is-overlay="true"
            @close="closeSidePanel"
          >
            <BookViewTocTree
              :active-toc-entry-id="activeTocEntryId"
              :active-alt-toc-entry-id="activeAltTocEntryId"
              :toc-entries="tocEntries"
              :toc-search-tree="tocSearchTree"
              :selected-alt-toc-section="selectedAltTocSection"
              :loading="tocLoading"
              :error="tocError"
              @select="onTocSelect"
              @alt-select="onAltTocSelect"
            />
          </BookViewSidePanel>

          <!--
            The bottom panel's filter dropdown. Same behaviour as a side panel's,
            but anchored here so it can run the full height of the body instead of
            being clipped to the bottom panel.
          -->
          <div
            v-if="panels.bottom.filterOpen.value"
            ref="bottomFilterRef"
            class="bottom-filter-dropdown"
            :style="{
              insetInlineStart: `${bottomFilterInlineStart}px`,
              '--bottom-filter-offset': `${bottomFilterInlineStart}px`,
            }"
          >
            <CommentaryTreePanel
              :groups="filterGroups"
              :tree-state="panels.bottom.treeState"
              :scope-key="panels.bottom.scopeKey"
              :scroll-to-book="scrollBottomTreeSelectionIntoView"
              @close="panels.bottom.closeFilter()"
            />
          </div>
        </div><!-- end .content-area -->
      </div><!-- end .main-area -->
    </div><!-- end .body-row -->

    <!-- Bottom toolbar -->
    <BookViewToolbar
      v-if="toolbarVisible && toolbarPosition === 'bottom'"
      ref="toolbarRef"
      v-bind="toolbarProps"
      @toggle-bottom-commentary="toggleCommentaryPanel('bottom')"
      @toggle-side-commentary="toggleCommentaryPanel('side')"
      @toggle-side-left-commentary="toggleCommentaryPanel('side-left')"
      @toggle-search="toggleSearch"
      @toggle-toc="toggleTocPanel"
      @export-to-word="onExportToWord"
      @navigate-to-next-section="navigateToAdjacentTocSection('next')"
      @navigate-to-previous-section="navigateToAdjacentTocSection('previous')"
    />
  </div>
</template>

<style scoped>
.book-view {
  display: flex;
  flex-direction: column;
  height: 100%;
  background: var(--bg-primary);
}

.body-row {
  display: flex;
  flex-direction: row;
  flex: 1;
  min-height: 0;
}

/* Pushes the left-position toolbar to the physical left end of body-row */
.toolbar-order-end {
  order: 1;
}

/* main-area: flex row containing inline side panel (when visible) + content-area.
   Also serves as the pointer-capture container for side panel drag resizing. */
.main-area {
  flex: 1;
  display: flex;
  flex-direction: row;
  min-height: 0;
  min-width: 0;
}

.content-area {
  position: relative;
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
  min-width: 0;
}

/* ── Side panel resize divider (inline mode) ───────────────────────────────── */
/* ── The bottom panel's filter dropdown ───────────────────────────────────── */
/* Floats over .content-area (which is position:relative), so it reflows nothing.
   Unlike a side panel's dropdown it is anchored to the body, not to its own panel,
   which is the whole point: it runs the full height of the book view instead of
   being trapped in the bottom panel's height. inset-inline-start is set inline,
   measured from the filter button.
   Width comes from the tree itself (CommentaryTreePanel is width: fit-content). */
.bottom-filter-dropdown {
  position: absolute;
  /* Inset from the body's edges so all four corners are free to round. */
  top: 6px;
  bottom: 6px;
  z-index: 60;
  display: flex;
  /* Whatever is left of the pane past the offset, so an inward-pushed button
     cannot make the dropdown overflow the far edge. */
  max-width: calc(100% - var(--bottom-filter-offset, 0px) - 12px);
  overflow: hidden;
  background: var(--bg-secondary);
  /* The app's floating-panel chrome (FullTextSearchAdvancedPanel, BookViewNoteBubble):
     1px border, 8px radius on all four corners, soft shadow. */
  border: 1px solid var(--border-color);
  border-radius: 8px;
  box-shadow: 0 4px 16px rgb(0 0 0 / 24%);
  --tree-bg: var(--bg-secondary);
}

/* ── Side commentary column + text column ─────────────────────────────────── */
.side-by-side {
  display: flex;
  flex-direction: row;
  flex: 1;
  overflow: hidden;
  min-height: 0;
}

.side-commentary {
  flex-shrink: 0;
  overflow: hidden;
  display: flex;
  flex-direction: column;
  min-width: 0;
}

.side-lines {
  flex: 1;
  overflow: hidden;
  display: flex;
  flex-direction: column;
  min-width: 0;
}

/* VS Code's sash model, as in SplitPane: a hairline at rest, a wide invisible grab
   band (::before), and a visible line (::after) that thickens and takes the accent
   colour only while hovered or dragged. The element is 1px and transparent so the
   ::after can grow without shifting the layout. */
</style>
