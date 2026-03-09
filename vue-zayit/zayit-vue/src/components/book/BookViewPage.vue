<template>
  <div class="flex-column height-fill book-view-wrapper"
       @click="handleBackgroundClick">
    <!-- Top Toolbar -->
    <LineViewToolbar v-show="myTab?.bookState?.bookId && toolbarPosition === 'top'"
                     ref="toolbarRef"
                     position="top" />

    <!-- Floating Toolbars -->
    <LineViewToolbar v-show="myTab?.bookState?.bookId && toolbarPosition === 'float-horizontal'"
                     position="float-horizontal" />
    <LineViewToolbar v-show="myTab?.bookState?.bookId && toolbarPosition === 'float-vertical'"
                     position="float-vertical" />

    <!-- Content area with TOC overlay -->
    <div class="flex-110 content-area-wrapper">
      <!-- Right Toolbar (ימין - appears on right in RTL) -->
      <LineViewToolbar v-show="myTab?.bookState?.bookId && toolbarPosition === 'right'"
                       position="right" />

      <div class="flex-110 content-area">
        <!-- Virtualized viewer is now always enabled -->
        <keep-alive>
          <TocTreePanelSplit v-if="myTab?.bookState?.isTocOpen && myTab?.bookState?.bookId"
                        ref="tocTreeViewRef"
                        :toc-entries="filteredTocEntries"
                        :is-loading="isTocLoading"
                        :is-compact-mode="!myTab.bookState.isFirstTocOpen"
                        :current-toc-entry-id="currentTocEntryId"
                        :show-alt-toc="myTab.bookState.showAltToc"
                        class="toc-overlay"
                        @select-line="handleTocSelection" />
        </keep-alive>

        <!-- Search bar -->
        <BookSearch v-if="myTab?.bookState?.bookId"
                    :is-open="isSearchOpen"
                    :current-match-index="searchCurrentMatchIndex"
                    :total-matches="searchTotalMatches"
                    :search-scope="searchScope"
                    :is-commentary-visible="isCommentaryVisible"
                    top-offset="4px"
                    @close="handleSearchClose"
                    @search="handleSearch"
                    @next="handleSearchNext"
                    @previous="handleSearchPrevious"
                    @scope-change="handleSearchScopeChange" />

        <SplitPane v-if="myTab?.bookState?.bookId"
                   :show-bottom="myTab.bookState.showBottomPane || false"
                   class="height-fill">
          <template #top>
            <LineView ref="lineViewerRef"
                      :tab-id="myTabId"
                      :alt-toc-by-line-index="altTocByLineIndex"
                      :flat-toc-entries="flatTocEntries"
                      class="flex-110"
                      @center-line-changed="currentCenterLineIndex = $event"
                      @current-toc-entry-changed="currentTocEntryId = $event" />
          </template>
          <template #bottom>
            <CommentaryView ref="commentaryViewRef"
                            :book-id="myTab.bookState.bookId"
                            :selected-line-index="myTab.bookState.selectedLineIndex"
                            :book="currentBook"
                            :flat-toc-entries="flatTocEntries"
                            @navigate-line="handleNavigateLine"
                            @navigate-previous-line="(bookId) => handleNavigatePreviousLine(bookId)"
                            @navigate-next-line="(bookId) => handleNavigateNextLine(bookId)" />
          </template>
        </SplitPane>
      </div>

      <!-- Left Toolbar (שמאל - appears on left in RTL) -->
      <LineViewToolbar v-show="myTab?.bookState?.bookId && toolbarPosition === 'left'"
                       position="left" />
    </div>

    <!-- Bottom Toolbar -->
    <LineViewToolbar v-show="myTab?.bookState?.bookId && toolbarPosition === 'bottom'"
                     position="bottom" />
  </div>
</template>

<script setup lang="ts">
import { ref, computed, watch, nextTick } from 'vue'
import { useEventListener } from '@vueuse/core'
import TocTreePanelSplit from '@/components/book/TocTreePanelSplit.vue'
import LineView from '@/components/book/LineView.vue'
import SplitPane from '@/components/shared/SplitPane.vue'
import CommentaryView from '@/components/commentary/CommentaryView.vue'
import LineViewToolbar from '@/components/book/BookViewToolbar.vue'
import BookSearch from '@/components/book/BookSearchBar.vue'
import { useBookViewPage } from '@/components/book/useBookView'
import { useTabs } from '@/components/workspace/useTabs'

const myTabId = ref<number | undefined>(undefined)
const lineViewerRef = ref<InstanceType<typeof LineView> | null>(null)
const commentaryViewRef = ref<InstanceType<typeof CommentaryView> | null>(null)
const tocTreeViewRef = ref<InstanceType<typeof TocTreePanelSplit> | null>(null)
const toolbarRef = ref<InstanceType<typeof LineViewToolbar> | null>(null)

const {
  myTab,
  toolbarPosition,
  altTocByLineIndex,
  filteredTocEntries,
  flatTocEntries,
  isTocLoading,
  currentCenterLineIndex,
  currentTocEntryId,
  currentBook,
  handleTocSelection,
  handleNavigateLine,
  handleNavigatePreviousLine,
  handleNavigateNextLine,
  handleBackgroundClick
} = useBookViewPage(
  () => myTabId.value,
  () => lineViewerRef.value
)

// Initialize myTabId from active tab and keep it synced
const { activeTab } = useTabs()

watch(activeTab, (newTab) => {
  myTabId.value = newTab?.id
}, { immediate: true })

// Search
const isSearchOpen = computed({
  get: () => myTab.value?.bookState?.isSearchOpen || false,
  set: (value) => {
    if (myTab.value?.bookState) {
      myTab.value.bookState.isSearchOpen = value
    }
  }
})

const searchScope = ref<'lines' | 'commentary' | 'both'>('lines')

const isCommentaryVisible = computed(() => myTab.value?.bookState?.showBottomPane || false)

// Search state - use refs and update after each search operation
const searchCurrentMatchIndex = ref(0)
const searchTotalMatches = ref(0)

function updateSearchState() {
  const lineView = lineViewerRef.value as any
  const commentaryView = commentaryViewRef.value as any

  if (searchScope.value === 'lines' && lineView) {
    searchCurrentMatchIndex.value = lineView.currentMatchIndex ?? 0
    searchTotalMatches.value = lineView.totalMatches ?? 0
  } else if (searchScope.value === 'commentary' && commentaryView) {
    searchCurrentMatchIndex.value = commentaryView.currentMatchIndex ?? 0
    searchTotalMatches.value = commentaryView.totalMatches ?? 0
  } else if (searchScope.value === 'both') {
    // Combined: lines first, then commentary
    const lineMatches = lineView?.totalMatches ?? 0
    const commentaryMatches = commentaryView?.totalMatches ?? 0
    const lineCurrentIndex = lineView?.currentMatchIndex ?? -1
    const commentaryCurrentIndex = commentaryView?.currentMatchIndex ?? -1

    searchTotalMatches.value = lineMatches + commentaryMatches

    // Determine which is current
    if (lineCurrentIndex >= 0) {
      searchCurrentMatchIndex.value = lineCurrentIndex
    } else if (commentaryCurrentIndex >= 0) {
      searchCurrentMatchIndex.value = lineMatches + commentaryCurrentIndex
    } else {
      searchCurrentMatchIndex.value = 0
    }
  }
}

function handleSearch(query: string) {
  if (!lineViewerRef.value) return

  if (query.trim().length < 2) {
    // Clear search
    lineViewerRef.value.clearLineSearch()
    if (commentaryViewRef.value) {
      commentaryViewRef.value.clearCommentarySearch()
    }
    searchCurrentMatchIndex.value = 0
    searchTotalMatches.value = 0
    return
  }

  // Perform search based on scope
  if (searchScope.value === 'lines' || searchScope.value === 'both') {
    lineViewerRef.value.performLineSearch(query)
  } else {
    lineViewerRef.value.clearLineSearch()
  }

  if ((searchScope.value === 'commentary' || searchScope.value === 'both') && commentaryViewRef.value) {
    commentaryViewRef.value.performCommentarySearch(query)
  } else if (commentaryViewRef.value) {
    commentaryViewRef.value.clearCommentarySearch()
  }

  // Update state after search
  nextTick(() => updateSearchState())
}

function handleSearchNext() {
  const lineView = lineViewerRef.value as any
  const commentaryView = commentaryViewRef.value as any

  if (searchScope.value === 'lines' && lineView) {
    lineView.nextLineSearchMatch()
  } else if (searchScope.value === 'commentary' && commentaryView) {
    commentaryView.nextCommentarySearchMatch()
  } else if (searchScope.value === 'both') {
    // Navigate through lines first, then commentary
    const lineMatches = lineView?.totalMatches ?? 0
    const lineCurrentIndex = lineView?.currentMatchIndex ?? -1

    if (lineCurrentIndex >= 0 && lineCurrentIndex < lineMatches - 1) {
      // Still in lines
      lineView.nextLineSearchMatch()
    } else if (lineCurrentIndex === lineMatches - 1 && commentaryView) {
      // Move to first commentary match
      lineView.clearLineSearch()
      commentaryView.performCommentarySearch(lineView.searchQuery || '')
    } else if (commentaryView) {
      // In commentary
      commentaryView.nextCommentarySearchMatch()
    }
  }

  nextTick(() => updateSearchState())
}

function handleSearchPrevious() {
  const lineView = lineViewerRef.value as any
  const commentaryView = commentaryViewRef.value as any

  if (searchScope.value === 'lines' && lineView) {
    lineView.previousLineSearchMatch()
  } else if (searchScope.value === 'commentary' && commentaryView) {
    commentaryView.previousCommentarySearchMatch()
  } else if (searchScope.value === 'both') {
    // Navigate through commentary first (reverse), then lines
    const commentaryCurrentIndex = commentaryView?.currentMatchIndex ?? -1

    if (commentaryCurrentIndex > 0 && commentaryView) {
      // Still in commentary
      commentaryView.previousCommentarySearchMatch()
    } else if (commentaryCurrentIndex === 0 && lineView) {
      // Move to last line match
      commentaryView.clearCommentarySearch()
      lineView.performLineSearch(commentaryView.searchQuery || '')
      // Go to last match
      const lineMatches = lineView.totalMatches ?? 0
      if (lineMatches > 0) {
        lineView.currentMatchIndex = lineMatches - 1
        lineView.scrollToCurrentMatch()
      }
    } else if (lineView) {
      // In lines
      lineView.previousLineSearchMatch()
    }
  }

  nextTick(() => updateSearchState())
}

function handleSearchScopeChange(scope: 'lines' | 'commentary' | 'both') {
  searchScope.value = scope
  // Re-run search with new scope if there's a query
  const lineView = lineViewerRef.value as any
  if (lineView?.searchQuery) {
    handleSearch(lineView.searchQuery)
  }
}

// Reset search scope to 'lines' when commentary is closed
watch(isCommentaryVisible, (visible) => {
  if (!visible && (searchScope.value === 'commentary' || searchScope.value === 'both')) {
    searchScope.value = 'lines'
    // Re-run search if active
    const lineView = lineViewerRef.value as any
    if (lineView?.searchQuery) {
      handleSearch(lineView.searchQuery)
    }
  }
})

// When search is opened via toolbar (not keyboard), default to 'both' if commentary is visible
let isOpeningViaKeyboard = false
watch(isSearchOpen, (opened, wasOpened) => {
  // Only set scope when opening via toolbar (not when already open and not via keyboard)
  if (opened && !wasOpened && !isOpeningViaKeyboard) {
    // Search was just opened via toolbar button
    console.log('[Search Watch] Opened via toolbar')
    const isCommentaryVisible = myTab.value?.bookState?.showBottomPane || false
    console.log('[Search Watch] Commentary visible:', isCommentaryVisible)
    console.log('[Search Watch] Current scope before:', searchScope.value)
    if (isCommentaryVisible) {
      console.log('[Search Watch] Setting scope to both')
      searchScope.value = 'both'
      console.log('[Search Watch] Scope after setting:', searchScope.value)
    } else {
      console.log('[Search Watch] Setting scope to lines')
      searchScope.value = 'lines'
      console.log('[Search Watch] Scope after setting:', searchScope.value)
    }
  }
  // Reset flag
  isOpeningViaKeyboard = false
})

// Watch searchScope changes to debug
watch(searchScope, (newScope, oldScope) => {
  console.log('[Search Scope Changed]', oldScope, '->', newScope)
  console.trace('[Search Scope Stack Trace]')
})

function handleSearchClose() {
  if (lineViewerRef.value) {
    lineViewerRef.value.clearLineSearch()
  }
  if (commentaryViewRef.value) {
    commentaryViewRef.value.clearCommentarySearch()
  }
  isSearchOpen.value = false
}

// Keyboard shortcut for search
useEventListener('keydown', (event: KeyboardEvent) => {
  const hasCtrlOrMeta = event.ctrlKey || event.metaKey

  if (hasCtrlOrMeta && event.code === 'KeyF') {
    event.preventDefault()

    console.log('[Keyboard] Ctrl+F pressed, isSearchOpen:', isSearchOpen.value)

    // Always set scope based on focus, even if search is already open
    const isCommentaryVisible = myTab.value?.bookState?.showBottomPane || false

    if (!isCommentaryVisible) {
      // Commentary not visible, only lines available
      console.log('[Keyboard] Commentary not visible, setting to lines')
      searchScope.value = 'lines'
    } else {
      // Check if focus is in commentary or line view
      const activeElement = document.activeElement
      const commentaryElement = commentaryViewRef.value?.$el
      const lineViewElement = lineViewerRef.value?.$el

      let isFocusInCommentary = false
      let isFocusInLineView = false

      // Check commentary focus
      if (commentaryElement && activeElement) {
        isFocusInCommentary = commentaryElement.contains(activeElement)
      }

      if (!isFocusInCommentary && activeElement) {
        const element = activeElement as HTMLElement
        const classList = element.classList
        const hasCommentaryClass =
          classList.contains('commentary-scroll-container') ||
          classList.contains('commentary-link') ||
          classList.contains('commentary-group')

        const bottomPane = element.closest('.bottom-pane')

        if (hasCommentaryClass || bottomPane !== null) {
          isFocusInCommentary = true
        }
      }

      // Check line view focus
      if (!isFocusInCommentary && lineViewElement && activeElement) {
        isFocusInLineView = lineViewElement.contains(activeElement)
      }

      if (!isFocusInLineView && !isFocusInCommentary && activeElement) {
        const element = activeElement as HTMLElement
        const topPane = element.closest('.top-pane')
        if (topPane !== null) {
          isFocusInLineView = true
        }
      }

      // Set scope based on focus
      if (isFocusInCommentary) {
        console.log('[Keyboard] Focus in commentary, setting to commentary')
        searchScope.value = 'commentary'
      } else if (isFocusInLineView) {
        console.log('[Keyboard] Focus in line view, setting to lines')
        searchScope.value = 'lines'
      } else {
        // Neither focused, default to both
        console.log('[Keyboard] No specific focus, setting to both')
        searchScope.value = 'both'
      }
    }

    // Set flag to prevent watch from overriding when opening
    if (!isSearchOpen.value) {
      isOpeningViaKeyboard = true
    }

    isSearchOpen.value = true
  }
})
</script>

<style scoped>
.content-area-wrapper {
  display: flex;
}

.content-area {
  position: relative;
}

.toc-overlay {
  position: absolute;
  height: 100%;
  width: 100%;
  z-index: 100;
  top: 0;
}

/* For compact mode, the TOC is now fixed positioned, so no special overlay styling needed */
</style>
