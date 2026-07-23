<script setup lang="ts">
import { ref, computed, watch, inject } from 'vue'
import { useResizeObserver } from '@vueuse/core'
import { useBookView } from './useBookView'
import { useBookViewStore } from '@/stores/bookViewStore'
import { isHosted } from '@/webview-host/seforimDb'
import { exportToWord as bridgeExportToWord } from '@/webview-host/bridge'
import BookViewToolbar from './BookViewToolbar.vue'
import SplitPane from '@/components/SplitPane.vue'
import BookViewLinesContent from './lines/BookViewLinesContent.vue'
import BookViewSearchBar from './BookViewSearchBar.vue'
import BookViewSidePanel from './BookViewSidePanel.vue'
import BookViewTocTree from './toc/BookViewTocTree.vue'
import CommentaryTreePanel from './commentary/CommentaryTreePanel.vue'
import CommentaryView from './commentary/CommentaryView.vue'

const toolbarRef = ref<InstanceType<typeof BookViewToolbar> | null>(null)
const linesContentRef = ref<InstanceType<typeof BookViewLinesContent> | null>(null)
const searchBarRef = ref<InstanceType<typeof BookViewSearchBar> | null>(null)
const commentaryViewRef = ref<InstanceType<typeof CommentaryView> | null>(null)
const bookViewRoot = ref<HTMLElement | null>(null)
const bookViewStore = useBookViewStore()
const paneId = inject<1 | 2>('paneId', 1)

type CommentaryMode = 'off' | 'bottom' | 'side'
const commentaryMode = ref<CommentaryMode>('off')
const sideBySide = computed(() => commentaryMode.value === 'side')

// Track the shell's own width instead of the viewport width so that in split
// view each pane responds to its own size, not the full window size.
const shellWidth = ref(window.innerWidth)
useResizeObserver(bookViewRoot, ([entry]) => { shellWidth.value = entry!.contentRect.width })
const isWideScreen = computed(() => shellWidth.value >= 650)
const isSidePanelWideScreen = computed(() => shellWidth.value >= 520)
const sidePanelIsOverlay = computed(() => !isSidePanelWideScreen.value)
const commentaryFraction = ref(0.4)
const stackedCommentaryFraction = ref(0.5)

// Commentary side-by-side divider drag state
const splitContainer = ref<HTMLElement | null>(null)
const isSplitDragging = ref(false)

function onSplitDividerPointerDown(e: PointerEvent) {
  isSplitDragging.value = true
  ;(e.target as HTMLElement).setPointerCapture(e.pointerId)
}
function onSplitPointerMove(e: PointerEvent) {
  if (!isSplitDragging.value || !splitContainer.value) return
  const rect = splitContainer.value.getBoundingClientRect()
  commentaryFraction.value = Math.min(0.9, Math.max(0.1, (rect.right - e.clientX) / rect.width))
}
function onSplitPointerUp() {
  isSplitDragging.value = false
}

// Side panel inline divider drag state.
// sidePanelResizeArea ref wraps the side panel + divider + content-area so we
// can measure the total available width for fraction calculations.
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

function cycleCommentaryMode() {
  if (commentaryMode.value === 'off') {
    commentaryMode.value = 'bottom'
  } else if (commentaryMode.value === 'bottom') {
    // Side-by-side only fits a wide pane; on a narrow pane skip it and close,
    // so we don't flash into a mode a watcher would immediately bounce back.
    commentaryMode.value = isWideScreen.value ? 'side' : 'off'
  } else {
    commentaryMode.value = 'off'
  }
}

const {
  toolbarPosition, toolbarVisible,
  searchHighlightLineIndex, searchHighlightQuery, searchHighlightSnippet, searchHighlightTerms,
  commentaryVisible, searchVisible, sidePanelMode,
  selectedLineId, commentaryTreeState, searchMode,
  activeTocEntryId, commentaryScrollIndex, commentaryScrollOffset,
  tocVisible, commentaryTreeVisible, sidePanelVisible, sidePanelToggleButtonEl,
  tabId, bookId, lines, prioritise, hasCommentaries, hasRelatedBooks, hasToc,
  bookHasTeamim,
  groups, groupsForDisplay, filterGroups, staticFilterGroups, commentaryLoading, commentaryLoadError, requestContentPriority,
  tocEntries, tocSearchTree, selectedAltTocSection, tocLoading, tocError,
  altTocLabelMap, pinnedCommentaryGroup, selectedSectionLineIds, manualSelectionLineIds,
  getHighlightsForLine, applyHighlight, clearHighlight,
  getNotesForLine, scheduleNotesLoad, createNote, updateNote, deleteNote,
  commentaryFontPx, renderContent, setCurrentMark, commentaryTocPaths,
  currentScrollLineIndex,
  scrollStateReady, idbResolved, initialLineIndex, initialScrollTop, initialScrollOffset,
  flashOpenLine,
  restoredCommentaryMode, restoredCommentaryFraction, restoredStackedCommentaryFraction,
  activeMatchCount, activeMatchIdx, contentSearch, commentarySearch,
  onLinesScrolled, onTocSelect, onAltTocSelect,
  onLineSelected, onNavigateSection, navigateToAdjacentTocSection, onCommentaryScroll,
  onCommentaryTreeChanged, openBookInTab,
  openContentSearch, openCommentarySearch, toggleSearch,
  onQueryChange, onSearchNext, onSearchPrev, onModeChange,
  toggleTocPanel, toggleCommentaryTreePanel, closeSidePanel,
  ensureStaticFilterGroupsLoaded, staticFilterGroupsLoaded,
  onCommentaryPanelMounted,
  getActiveTocEntry, getTocPath,
  buildExportHtml,
  bookTitle,
} = useBookView(
  () => toolbarRef.value,
  () => linesContentRef.value,
  () => searchBarRef.value,
  () => commentaryViewRef.value,
)

async function onExportToWord() {
  if (!isHosted) return
  const html = buildExportHtml()
  await bridgeExportToWord(html, bookTitle ?? '').catch(() => {})
}

// Switching between the stacked (SplitPane) and side-by-side layouts swaps template
// branches, which unmounts and remounts BookViewLinesContent. The remounted instance
// re-runs its initial-scroll restore using initialLineIndex/initialScrollTop — values
// frozen at session-restore time — so it would jump back to the stale position (or to
// the top when nothing was saved). Capture the live position before the swap (pre-flush,
// old instance still mounted) and feed it through the same initial-scroll props.
watch(sideBySide, () => {
  const pos = linesContentRef.value?.captureScrollPos?.()
  if (!pos) return
  initialLineIndex.value = undefined
  initialScrollTop.value = pos.scrollIndex
  initialScrollOffset.value = pos.scrollOffset
})

watch(commentaryMode, (mode) => { commentaryVisible.value = mode !== 'off' })
watch(commentaryVisible, (v) => { if (!v) commentaryMode.value = 'off' })
watch(isWideScreen, (wide) => { if (!wide && commentaryMode.value === 'side') commentaryMode.value = 'bottom' })
watch(commentaryMode, (mode) => { if (mode === 'side' && !isWideScreen.value) commentaryMode.value = 'off' })
watch(commentaryMode, (mode, previous) => {
  if (mode !== 'off' && previous !== 'off' && mode !== previous) {
    setTimeout(() => onCommentaryPanelMounted(), 0)
  }
})
watch(restoredCommentaryMode, (mode) => { if (mode) commentaryMode.value = mode }, { once: true })
watch(restoredCommentaryFraction, (fraction) => { if (fraction != null) commentaryFraction.value = fraction }, { once: true })
watch(restoredStackedCommentaryFraction, (fraction) => { if (fraction != null) stackedCommentaryFraction.value = fraction }, { once: true })
watch(() => bookViewStore.openSearchSignal, (signal) => { if (signal.paneId === paneId) openContentSearch() })
watch(() => bookViewStore.toggleBottomPanelSignal, (signal) => { if (signal.paneId === paneId) cycleCommentaryMode() })
watch(() => bookViewStore.toggleTocPanelSignal, (signal) => { if (signal.paneId === paneId) toggleTocPanel() })
</script>

<template>
  <div class="book-view" ref="bookViewRoot">
    <!-- Top toolbar -->
    <BookViewToolbar
      v-if="toolbarVisible && toolbarPosition === 'top'"
      ref="toolbarRef"
      :commentary-visible="commentaryVisible"
      :search-visible="searchVisible"
      :toc-visible="tocVisible"
      :has-toc="hasToc"
      :has-commentaries="hasCommentaries"
      :has-related-books="hasRelatedBooks"
      :tab-id="tabId"
      :book-id="bookId"
      :book-has-teamim="bookHasTeamim"
      :filter-groups="staticFilterGroups"
      :related-books-loaded="staticFilterGroupsLoaded"
      :current-scroll-line-index="currentScrollLineIndex"
      :lines="lines"
      :on-related-books-open="ensureStaticFilterGroupsLoaded"
      :commentary-mode="commentaryMode"
      :can-use-side-by-side="isWideScreen"
      @cycle-commentary-mode="cycleCommentaryMode"
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
        :commentary-visible="commentaryVisible"
        :search-visible="searchVisible"
        :toc-visible="tocVisible"
        :has-toc="hasToc"
        :has-commentaries="hasCommentaries"
        :has-related-books="hasRelatedBooks"
        :tab-id="tabId"
        :book-id="bookId"
        :book-has-teamim="bookHasTeamim"
        :filter-groups="staticFilterGroups"
        :related-books-loaded="staticFilterGroupsLoaded"
        :current-scroll-line-index="currentScrollLineIndex"
        :lines="lines"
        :class="toolbarPosition === 'left' ? 'toolbar-order-end' : ''"
        :on-related-books-open="ensureStaticFilterGroupsLoaded"
        :commentary-mode="commentaryMode"
        :can-use-side-by-side="isWideScreen"
        @cycle-commentary-mode="cycleCommentaryMode"
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
              v-if="sidePanelMode === 'toc'"
              :active-toc-entry-id="activeTocEntryId"
              :toc-entries="tocEntries"
              :toc-search-tree="tocSearchTree"
              :selected-alt-toc-section="selectedAltTocSection"
              :loading="tocLoading"
              :error="tocError"
              @select="onTocSelect"
              @alt-select="onAltTocSelect"
            />
            <CommentaryTreePanel
              v-else-if="sidePanelMode === 'commentary-tree'"
              :groups="filterGroups"
              :tree-state="commentaryTreeState"
              :scroll-to-book="(bookId: number) => commentaryViewRef?.scrollToGroup(bookId)"
            />
          </BookViewSidePanel>
          <div class="side-panel-divider" @pointerdown="onSidePanelDividerPointerDown" />
        </template>

        <!-- content-area: always fills remaining horizontal space -->
        <div class="content-area">
          <!-- Commentary side-by-side layout -->
          <div
            v-if="sideBySide && commentaryVisible"
            ref="splitContainer"
            class="side-by-side"
            @pointermove="onSplitPointerMove"
            @pointerup="onSplitPointerUp"
          >
            <div class="side-commentary" :style="{ width: `${commentaryFraction * 100}%` }">
              <CommentaryView
                v-if="commentaryVisible"
                :key="bookId"
                ref="commentaryViewRef"
                :selected-line-id="selectedLineId"
                :groups="groupsForDisplay"
                :loading="commentaryLoading"
                :load-error="commentaryLoadError"
                :visibility-list="commentaryTreeState.visibilityList"
                :pinned-group="pinnedCommentaryGroup"
                :filter-visible="commentaryTreeVisible"
                :get-highlights-for-line="getHighlightsForLine"
                :apply-highlight="applyHighlight"
                :clear-highlight="clearHighlight"
                :get-notes-for-line="getNotesForLine"
                :schedule-notes-load="scheduleNotesLoad"
                :request-content-priority="requestContentPriority"
                :has-saved-scroll-pos="commentaryScrollIndex != null"
                :create-note="createNote"
                :update-note="updateNote"
                :delete-note="deleteNote"
                :commentary-font-px="commentaryFontPx"
                :render-content="renderContent"
                :set-current-mark="setCurrentMark"
                :commentary-toc-paths="commentaryTocPaths"
                :search-query="searchVisible && searchMode === 'commentary' ? commentarySearch.query.value : ''"
                :current-match-flat-index="searchVisible && searchMode === 'commentary' ? commentarySearch.currentMatchFlatIndex.value : undefined"
                :current-match-occurrence="searchVisible && searchMode === 'commentary' ? commentarySearch.currentMatchOccurrence.value : undefined"
                @close="commentaryVisible = false"
                @navigate-section="onNavigateSection"
                @scroll="onCommentaryScroll"
                @toggle-filter-panel="toggleCommentaryTreePanel"
                @toggle-search="openCommentarySearch"
                @open-book="openBookInTab"
              />
            </div>
            <div class="side-divider" @pointerdown="onSplitDividerPointerDown" />
            <div class="side-lines">
              <BookViewLinesContent
                v-if="scrollStateReady"
                ref="linesContentRef"
                :lines="lines"
                :prioritise="prioritise"
                :alt-toc-label-map="altTocLabelMap"
                :selected-line-id="selectedLineId"
                :commentary-visible="commentaryVisible"
                :commentary-mode="commentaryMode"
                :commentary-fraction="commentaryFraction"
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
                :commentary-scroll-index="commentaryScrollIndex"
                :commentary-scroll-offset="commentaryScrollOffset"
                :commentary-filter-state="commentaryTreeState"
                :search-query="searchVisible && searchMode === 'content' ? contentSearch.query.value : ''"
                :current-match-line-index="searchVisible && searchMode === 'content' ? contentSearch.currentMatchLineIndex.value : undefined"
                :current-match-occurrence="searchVisible && searchMode === 'content' ? contentSearch.currentMatchOccurrence.value : undefined"
                :get-active-toc-entry="getActiveTocEntry"
                :get-toc-path="getTocPath"
                :pinned-commentary-group="pinnedCommentaryGroup"
                :selected-section-line-ids="selectedSectionLineIds"
                :multi-select-line-ids="manualSelectionLineIds"
                @scrolled="onLinesScrolled"
                @line-selected="onLineSelected"
                @ctrl-f="openContentSearch"
              />
            </div>
          </div>

          <!-- Commentary stacked (bottom) layout -->
          <SplitPane v-else v-model="stackedCommentaryFraction" :bottom-visible="commentaryVisible">
            <template #top>
              <BookViewLinesContent
                v-if="scrollStateReady"
                ref="linesContentRef"
                :lines="lines"
                :prioritise="prioritise"
                :alt-toc-label-map="altTocLabelMap"
                :selected-line-id="selectedLineId"
                :commentary-visible="commentaryVisible"
                :commentary-mode="commentaryMode"
                :commentary-fraction="commentaryFraction"
                :stacked-commentary-fraction="stackedCommentaryFraction"
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
                :commentary-scroll-index="commentaryScrollIndex"
                :commentary-scroll-offset="commentaryScrollOffset"
                :commentary-filter-state="commentaryTreeState"
                :search-query="searchVisible && searchMode === 'content' ? contentSearch.query.value : ''"
                :current-match-line-index="searchVisible && searchMode === 'content' ? contentSearch.currentMatchLineIndex.value : undefined"
                :current-match-occurrence="searchVisible && searchMode === 'content' ? contentSearch.currentMatchOccurrence.value : undefined"
                :get-active-toc-entry="getActiveTocEntry"
                :get-toc-path="getTocPath"
                :pinned-commentary-group="pinnedCommentaryGroup"
                :selected-section-line-ids="selectedSectionLineIds"
                :multi-select-line-ids="manualSelectionLineIds"
                @scrolled="onLinesScrolled"
                @line-selected="onLineSelected"
                @ctrl-f="openContentSearch"
              />
            </template>
            <template #bottom>
              <CommentaryView
                v-if="commentaryVisible"
                :key="bookId"
                ref="commentaryViewRef"
                :selected-line-id="selectedLineId"
                :groups="groupsForDisplay"
                :loading="commentaryLoading"
                :load-error="commentaryLoadError"
                :visibility-list="commentaryTreeState.visibilityList"
                :pinned-group="pinnedCommentaryGroup"
                :filter-visible="commentaryTreeVisible"
                :get-highlights-for-line="getHighlightsForLine"
                :apply-highlight="applyHighlight"
                :clear-highlight="clearHighlight"
                :get-notes-for-line="getNotesForLine"
                :schedule-notes-load="scheduleNotesLoad"
                :request-content-priority="requestContentPriority"
                :has-saved-scroll-pos="commentaryScrollIndex != null"
                :create-note="createNote"
                :update-note="updateNote"
                :delete-note="deleteNote"
                :commentary-font-px="commentaryFontPx"
                :render-content="renderContent"
                :set-current-mark="setCurrentMark"
                :commentary-toc-paths="commentaryTocPaths"
                :search-query="searchVisible && searchMode === 'commentary' ? commentarySearch.query.value : ''"
                :current-match-flat-index="searchVisible && searchMode === 'commentary' ? commentarySearch.currentMatchFlatIndex.value : undefined"
                :current-match-occurrence="searchVisible && searchMode === 'commentary' ? commentarySearch.currentMatchOccurrence.value : undefined"
                @close="commentaryVisible = false"
                @navigate-section="onNavigateSection"
                @scroll="onCommentaryScroll"
                @toggle-filter-panel="toggleCommentaryTreePanel"
                @toggle-search="openCommentarySearch"
                @open-book="openBookInTab"
              />
            </template>
          </SplitPane>

          <!-- Search bar (floats inside content-area) -->
          <BookViewSearchBar
            ref="searchBarRef"
            :visible="searchVisible"
            :toolbar-visible="toolbarVisible"
            :toolbar-position="toolbarPosition"
            :match-count="activeMatchCount"
            :current-match="activeMatchIdx"
            :commentary-visible="commentaryVisible"
            :mode="searchMode"
            :query="searchMode === 'content' ? contentSearch.query.value : commentarySearch.query.value"
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
              v-if="sidePanelMode === 'toc'"
              :active-toc-entry-id="activeTocEntryId"
              :toc-entries="tocEntries"
              :toc-search-tree="tocSearchTree"
              :selected-alt-toc-section="selectedAltTocSection"
              :loading="tocLoading"
              :error="tocError"
              @select="onTocSelect"
              @alt-select="onAltTocSelect"
            />
            <CommentaryTreePanel
              v-else-if="sidePanelMode === 'commentary-tree'"
              :groups="filterGroups"
              :tree-state="commentaryTreeState"
              :scroll-to-book="(bookId: number) => commentaryViewRef?.scrollToGroup(bookId)"
            />
          </BookViewSidePanel>
        </div><!-- end .content-area -->
      </div><!-- end .main-area -->
    </div><!-- end .body-row -->

    <!-- Bottom toolbar -->
    <BookViewToolbar
      v-if="toolbarVisible && toolbarPosition === 'bottom'"
      ref="toolbarRef"
      :commentary-visible="commentaryVisible"
      :search-visible="searchVisible"
      :toc-visible="tocVisible"
      :has-toc="hasToc"
      :has-commentaries="hasCommentaries"
      :has-related-books="hasRelatedBooks"
      :tab-id="tabId"
      :book-id="bookId"
      :book-has-teamim="bookHasTeamim"
      :filter-groups="staticFilterGroups"
      :related-books-loaded="staticFilterGroupsLoaded"
      :current-scroll-line-index="currentScrollLineIndex"
      :lines="lines"
      :on-related-books-open="ensureStaticFilterGroupsLoaded"
      :commentary-mode="commentaryMode"
      :can-use-side-by-side="isWideScreen"
      @cycle-commentary-mode="cycleCommentaryMode"
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
.side-panel-divider {
  width: 2px;
  flex-shrink: 0;
  background: var(--border-color);
  touch-action: none;
  position: relative;
  cursor:
    url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='24' height='24' viewBox='0 0 24 24'%3E%3Cpath d='M3 12 L7 8 L7 10 L11 10 L11 14 L7 14 L7 16 Z' fill='%23ffffff' stroke='%23000000' stroke-width='0.5'/%3E%3Cpath d='M21 12 L17 8 L17 10 L13 10 L13 14 L17 14 L17 16 Z' fill='%23ffffff' stroke='%23000000' stroke-width='0.5'/%3E%3C/svg%3E")
      12 12,
    col-resize;
}

.side-panel-divider::before {
  content: '';
  position: absolute;
  top: 0;
  bottom: 0;
  left: 50%;
  transform: translateX(-50%);
  width: 20px;
}

.side-panel-divider::after {
  content: '';
  position: absolute;
  top: 0;
  bottom: 0;
  left: 50%;
  transform: translateX(-50%);
  width: 2px;
  background: var(--border-color);
  transition: width 120ms;
}

.side-panel-divider:hover::after,
.side-panel-divider:active::after {
  width: 4px;
  background: color-mix(in srgb, var(--accent-color) 50%, transparent);
}

/* ── Commentary side-by-side layout ───────────────────────────────────────── */
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

.side-divider {
  width: 2px;
  flex-shrink: 0;
  background: var(--border-color);
  touch-action: none;
  position: relative;
  cursor:
    url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='24' height='24' viewBox='0 0 24 24'%3E%3Cpath d='M3 12 L7 8 L7 10 L11 10 L11 14 L7 14 L7 16 Z' fill='%23ffffff' stroke='%23000000' stroke-width='0.5'/%3E%3Cpath d='M21 12 L17 8 L17 10 L13 10 L13 14 L17 14 L17 16 Z' fill='%23ffffff' stroke='%23000000' stroke-width='0.5'/%3E%3C/svg%3E")
      12 12,
    col-resize;
}

.side-divider::before {
  content: '';
  position: absolute;
  top: 0;
  bottom: 0;
  left: 50%;
  transform: translateX(-50%);
  width: 20px;
}

.side-divider::after {
  content: '';
  position: absolute;
  top: 0;
  bottom: 0;
  left: 50%;
  transform: translateX(-50%);
  width: 2px;
  background: var(--border-color);
  transition: width 120ms;
}

.side-divider:hover::after {
  width: 6px;
  background: color-mix(in srgb, var(--text-secondary) 25%, transparent);
}
</style>
