<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { useMediaQuery } from '@vueuse/core'
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
const bookViewStore = useBookViewStore()

type CommentaryMode = 'off' | 'bottom' | 'side'
const commentaryMode = ref<CommentaryMode>('off')
const sideBySide = computed(() => commentaryMode.value === 'side')
const isWideScreen = useMediaQuery('(min-width: 650px)')
const commentaryFraction = ref(0.4)

// Side-by-side divider drag state (inlined from BookViewSplitPane)
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

function cycleCommentaryMode() {
  if (commentaryMode.value === 'off') {
    commentaryMode.value = 'bottom'
  } else if (commentaryMode.value === 'bottom') {
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
  bookId, lines, prioritise, hasCommentaries, hasRelatedBooks, hasToc,
  bookHasTeamim,
  groups, groupsForDisplay, filterGroups, staticFilterGroups, commentaryLoading,
  tocEntries, tocSearchTree, selectedAltTocSection, tocLoading, tocError,
  altTocLabelMap, pinnedCommentaryGroup, selectedSectionLineIds,
  getHighlightsForLine, applyHighlight, clearHighlight,
  getNotesForLine, scheduleNotesLoad, createNote, updateNote, deleteNote,
  commentaryFontPx, renderContent, setCurrentMark, commentaryTocPaths,
  currentScrollLineIndex,
  scrollStateReady, idbResolved, initialLineIndex, initialScrollTop, initialScrollOffset,
  restoredCommentaryMode, restoredCommentaryFraction,
  activeMatchCount, activeMatchIdx, contentSearch, commentarySearch,
  onLinesScrolled, onTocSelect, onAltTocSelect,
  onLineSelected, onNavigateSection, onCommentaryScroll,
  onCommentaryTreeChanged, openBookInTab,
  openContentSearch, openCommentarySearch,
  onQueryChange, onSearchNext, onSearchPrev, onModeChange,
  toggleTocPanel, toggleCommentaryTreePanel, closeSidePanel,
  ensureStaticFilterGroupsLoaded, staticFilterGroupsLoaded,
  onCommentaryPanelMounted,
  getActiveTocEntry, getTocPath,
  buildExportHtml,
} = useBookView(
  () => toolbarRef.value,
  () => linesContentRef.value,
  () => searchBarRef.value,
  () => commentaryViewRef.value,
)

async function onExportToWord() {
  if (!isHosted) return
  const html = buildExportHtml()
  await bridgeExportToWord(html).catch(() => {})
}

// Keep commentaryMode and useBookView's commentaryVisible in sync.
// commentaryMode is the source of truth for the UI; commentaryVisible drives internal logic.
watch(commentaryMode, (mode) => { commentaryVisible.value = mode !== 'off' })
watch(commentaryVisible, (v) => { if (!v) commentaryMode.value = 'off' })
// Snap back to bottom layout when screen becomes too narrow for side-by-side.
watch(isWideScreen, (wide) => { if (!wide && commentaryMode.value === 'side') commentaryMode.value = 'bottom' })
// When switching between bottom and side layout, CommentaryView remounts — run the
// same open-side effects (commentaryLineId init, scroll-to-pinned) as a fresh open.
watch(commentaryMode, (mode, previous) => {
  if (mode !== 'off' && previous !== 'off' && mode !== previous) {
    // Use setTimeout to let the new CommentaryView mount before touching its state.
    setTimeout(() => onCommentaryPanelMounted(), 0)
  }
})
// Restore commentaryMode from IDB once session restore resolves.
watch(restoredCommentaryMode, (mode) => { if (mode) commentaryMode.value = mode }, { once: true })
// Restore commentaryFraction from IDB once session restore resolves.
watch(restoredCommentaryFraction, (fraction) => { if (fraction != null) commentaryFraction.value = fraction }, { once: true })
// Listen for Ctrl+F from AppTitleBar to open search
watch(() => bookViewStore.openSearchSignal, () => {
  openContentSearch()
})
// Listen for Ctrl+J from AppTitleBar to toggle commentary panel
watch(() => bookViewStore.toggleBottomPanelSignal, () => {
  cycleCommentaryMode()
})
// Listen for Ctrl+K from AppTitleBar to toggle TOC panel
watch(() => bookViewStore.toggleTocPanelSignal, () => {
  toggleTocPanel()
})
</script>

<template>
  <div class="book-view">
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
      :book-id="bookId"
      :book-has-teamim="bookHasTeamim"
      :filter-groups="staticFilterGroups"
      :related-books-loaded="staticFilterGroupsLoaded"
      :current-scroll-line-index="currentScrollLineIndex"
      :lines="lines"
      :on-related-books-open="ensureStaticFilterGroupsLoaded"
      :commentary-mode="commentaryMode"
      @cycle-commentary-mode="cycleCommentaryMode"
      @toggle-search="searchVisible = !searchVisible"
      @toggle-toc="toggleTocPanel"`n      @export-to-word="onExportToWord"
    />
    <!-- Middle row: right toolbar + content + left toolbar (RTL: first child = physical right) -->
    <div class="body-row">
      <!-- Right or left side toolbar — rendered once, CSS class drives orientation and border -->
      <BookViewToolbar
        v-if="toolbarVisible && (toolbarPosition === 'right' || toolbarPosition === 'left')"
        ref="toolbarRef"
        :commentary-visible="commentaryVisible"
        :search-visible="searchVisible"
        :toc-visible="tocVisible"
        :has-toc="hasToc"
        :has-commentaries="hasCommentaries"
        :has-related-books="hasRelatedBooks"
        :book-id="bookId"
        :book-has-teamim="bookHasTeamim"
        :filter-groups="staticFilterGroups"
        :related-books-loaded="staticFilterGroupsLoaded"
        :current-scroll-line-index="currentScrollLineIndex"
        :lines="lines"
        :class="toolbarPosition === 'left' ? 'toolbar-order-end' : ''"
        :on-related-books-open="ensureStaticFilterGroupsLoaded"
        :commentary-mode="commentaryMode"
        @cycle-commentary-mode="cycleCommentaryMode"
        @toggle-search="searchVisible = !searchVisible"
        @toggle-toc="toggleTocPanel"`n      @export-to-word="onExportToWord"
    />
      <div class="content-area">
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
              :visibility-list="commentaryTreeState.visibilityList"
              :pinned-group="pinnedCommentaryGroup"
              :filter-visible="commentaryTreeVisible"
              :get-highlights-for-line="getHighlightsForLine"
              :apply-highlight="applyHighlight"
              :clear-highlight="clearHighlight"
              :get-notes-for-line="getNotesForLine"
              :schedule-notes-load="scheduleNotesLoad"
              :create-note="createNote"
              :update-note="updateNote"
              :delete-note="deleteNote"
              :commentary-font-px="commentaryFontPx"
              :render-content="renderContent"
              :set-current-mark="setCurrentMark"
              :commentary-toc-paths="commentaryTocPaths"
              :search-query="searchMode === 'commentary' ? commentarySearch.query.value : ''"
              :current-match-flat-index="searchMode === 'commentary' ? commentarySearch.currentMatchFlatIndex.value : undefined"
              :current-match-occurrence="searchMode === 'commentary' ? commentarySearch.currentMatchOccurrence.value : undefined"
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
              :idb-resolved="idbResolved"
              :search-highlight-line-index="searchHighlightLineIndex"
              :search-highlight-query="searchHighlightQuery"
              :search-highlight-snippet="searchHighlightSnippet"
              :search-highlight-terms="searchHighlightTerms"
              :search-bar-visible="searchVisible"
              :commentary-scroll-index="commentaryScrollIndex"
              :commentary-scroll-offset="commentaryScrollOffset"
              :commentary-filter-state="commentaryTreeState"
              :search-query="searchMode === 'content' ? contentSearch.query.value : ''"
              :current-match-line-index="searchMode === 'content' ? contentSearch.currentMatchLineIndex.value : undefined"
              :current-match-occurrence="searchMode === 'content' ? contentSearch.currentMatchOccurrence.value : undefined"
              :get-active-toc-entry="getActiveTocEntry"
              :get-toc-path="getTocPath"
              :pinned-commentary-group="pinnedCommentaryGroup"
              :selected-section-line-ids="selectedSectionLineIds"
              @scrolled="onLinesScrolled"
              @line-selected="onLineSelected"
              @ctrl-f="openContentSearch"
            />
          </div>
        </div>
        <SplitPane v-else :bottom-visible="commentaryVisible">
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
              :initial-line-index="initialLineIndex"
              :initial-scroll-index="initialScrollTop"
              :initial-scroll-offset="initialScrollOffset"
              :idb-resolved="idbResolved"
              :search-highlight-line-index="searchHighlightLineIndex"
              :search-highlight-query="searchHighlightQuery"
              :search-highlight-snippet="searchHighlightSnippet"
              :search-highlight-terms="searchHighlightTerms"
              :search-bar-visible="searchVisible"
              :commentary-scroll-index="commentaryScrollIndex"
              :commentary-scroll-offset="commentaryScrollOffset"
              :commentary-filter-state="commentaryTreeState"
              :search-query="searchMode === 'content' ? contentSearch.query.value : ''"
              :current-match-line-index="searchMode === 'content' ? contentSearch.currentMatchLineIndex.value : undefined"
              :current-match-occurrence="searchMode === 'content' ? contentSearch.currentMatchOccurrence.value : undefined"
              :get-active-toc-entry="getActiveTocEntry"
              :get-toc-path="getTocPath"
              :pinned-commentary-group="pinnedCommentaryGroup"
              :selected-section-line-ids="selectedSectionLineIds"
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
              :visibility-list="commentaryTreeState.visibilityList"
              :pinned-group="pinnedCommentaryGroup"
              :filter-visible="commentaryTreeVisible"
              :get-highlights-for-line="getHighlightsForLine"
              :apply-highlight="applyHighlight"
              :clear-highlight="clearHighlight"
              :get-notes-for-line="getNotesForLine"
              :schedule-notes-load="scheduleNotesLoad"
              :create-note="createNote"
              :update-note="updateNote"
              :delete-note="deleteNote"
              :commentary-font-px="commentaryFontPx"
              :render-content="renderContent"
              :set-current-mark="setCurrentMark"
              :commentary-toc-paths="commentaryTocPaths"
              :search-query="searchMode === 'commentary' ? commentarySearch.query.value : ''"
              :current-match-flat-index="searchMode === 'commentary' ? commentarySearch.currentMatchFlatIndex.value : undefined"
              :current-match-occurrence="searchMode === 'commentary' ? commentarySearch.currentMatchOccurrence.value : undefined"
              @close="commentaryVisible = false"
              @navigate-section="onNavigateSection"
              @scroll="onCommentaryScroll"
              @toggle-filter-panel="toggleCommentaryTreePanel"
              @toggle-search="openCommentarySearch"
              @open-book="openBookInTab"
            />
          </template>
        </SplitPane>
        <BookViewSearchBar
          ref="searchBarRef"
          :visible="searchVisible"
          :toolbar-visible="toolbarVisible"
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
        <BookViewSidePanel
          v-if="sidePanelVisible"
          :toggle-button-el="sidePanelToggleButtonEl"
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
      </div>
    </div>
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
      :book-id="bookId"
      :book-has-teamim="bookHasTeamim"
      :filter-groups="staticFilterGroups"
      :related-books-loaded="staticFilterGroupsLoaded"
      :current-scroll-line-index="currentScrollLineIndex"
      :lines="lines"
      :on-related-books-open="ensureStaticFilterGroupsLoaded"
      :commentary-mode="commentaryMode"
      @cycle-commentary-mode="cycleCommentaryMode"
      @toggle-search="searchVisible = !searchVisible"
      @toggle-toc="toggleTocPanel"`n      @export-to-word="onExportToWord"
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
.content-area {
  position: relative;
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}
/* Pushes the left-position toolbar to the physical left end of body-row */
.toolbar-order-end {
  order: 1;
}
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
