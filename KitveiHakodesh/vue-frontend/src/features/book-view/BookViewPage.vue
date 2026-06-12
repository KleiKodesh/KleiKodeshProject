<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { useMediaQuery } from '@vueuse/core'
import { useBookView } from './useBookView'
import { useBookViewStore } from '@/stores/bookViewStore'
import BookViewToolbar from './BookViewToolbar.vue'
import BookViewSplitPane from './BookViewSplitPane.vue'
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
  getActiveTocEntry, getTocPath,
} = useBookView(
  () => toolbarRef.value,
  () => linesContentRef.value,
  () => searchBarRef.value,
  () => commentaryViewRef.value,
)

// Keep commentaryMode and useBookView's commentaryVisible in sync.
// commentaryMode is the source of truth for the UI; commentaryVisible drives internal logic.
watch(commentaryMode, (mode) => { commentaryVisible.value = mode !== 'off' })
watch(commentaryVisible, (v) => { if (!v) commentaryMode.value = 'off' })
// Snap back to bottom layout when screen becomes too narrow for side-by-side.
watch(isWideScreen, (wide) => { if (!wide && commentaryMode.value === 'side') commentaryMode.value = 'bottom' })
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
      @toggle-toc="toggleTocPanel"
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
        @toggle-toc="toggleTocPanel"
    />
      <div class="content-area">
        <BookViewSplitPane
          :commentary-visible="commentaryVisible"
          :side-by-side="sideBySide"
          :commentary-fraction="commentaryFraction"
          @update:commentary-fraction="commentaryFraction = $event"
        >
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
              :current-match-line-index="
                searchMode === 'content' ? contentSearch.currentMatchLineIndex.value : undefined
              "
              :current-match-occurrence="
                searchMode === 'content' ? contentSearch.currentMatchOccurrence.value : undefined
              "
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
              :key="bookId"
              ref="commentaryViewRef"
              :selected-line-id="selectedLineId"
              :groups="groupsForDisplay"
              :loading="commentaryLoading"
              :visibility-list="commentaryTreeState.visibilityList"
              :pinned-group="pinnedCommentaryGroup"
              :filter-visible="commentaryTreeVisible"
              :search-query="searchMode === 'commentary' ? commentarySearch.query.value : ''"
              :current-match-flat-index="
                searchMode === 'commentary'
                  ? commentarySearch.currentMatchFlatIndex.value
                  : undefined
              "
              :current-match-occurrence="
                searchMode === 'commentary'
                  ? commentarySearch.currentMatchOccurrence.value
                  : undefined
              "
              @close="commentaryVisible = false"
              @navigate-section="onNavigateSection"
              @scroll="onCommentaryScroll"
              @toggle-filter-panel="toggleCommentaryTreePanel"
              @toggle-search="openCommentarySearch"
              @open-book="openBookInTab"
            />
          </template>
        </BookViewSplitPane>
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
      @toggle-toc="toggleTocPanel"
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
</style>
