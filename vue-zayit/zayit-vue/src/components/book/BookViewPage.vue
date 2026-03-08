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
          <TocTreePanel v-if="myTab?.bookState?.isTocOpen && myTab?.bookState?.bookId"
                        ref="tocTreeViewRef"
                        :toc-entries="filteredTocEntries"
                        :is-loading="isTocLoading"
                        :is-compact-mode="!myTab.bookState.isFirstTocOpen"
                        :current-toc-entry-id="currentTocEntryId"
                        class="toc-overlay"
                        @select-line="handleTocSelection" />
        </keep-alive>

        <!-- Search bar -->
        <BookSearch v-if="myTab?.bookState?.bookId"
                    :is-open="isSearchOpen"
                    :current-match-index="searchCurrentMatchIndex"
                    :total-matches="searchTotalMatches"
                    top-offset="4px"
                    @close="handleSearchClose"
                    @search="handleSearch"
                    @next="handleSearchNext"
                    @previous="handleSearchPrevious" />

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
import TocTreePanel from '@/components/book/TocTreePanel.vue'
import LineView from '@/components/book/LineView.vue'
import SplitPane from '@/components/shared/SplitPane.vue'
import CommentaryView from '@/components/commentary/CommentaryView.vue'
import LineViewToolbar from '@/components/book/BookViewToolbar.vue'
import BookSearch from '@/components/book/BookSearch.vue'
import { useBookViewPage } from '@/components/book/useBookView'
import { useTabs } from '@/components/workspace/useTabs'

const myTabId = ref<number | undefined>(undefined)
const lineViewerRef = ref<InstanceType<typeof LineView> | null>(null)
const commentaryViewRef = ref<InstanceType<typeof CommentaryView> | null>(null)
const tocTreeViewRef = ref<InstanceType<typeof TocTreePanel> | null>(null)
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

// Search state - use refs and update after each search operation
const searchCurrentMatchIndex = ref(0)
const searchTotalMatches = ref(0)

function updateSearchState() {
  const lineView = lineViewerRef.value as any
  if (lineView) {
    searchCurrentMatchIndex.value = lineView.currentMatchIndex ?? 0
    searchTotalMatches.value = lineView.totalMatches ?? 0
  }
}

function handleSearch(query: string) {
  if (!lineViewerRef.value) return

  if (query.trim().length < 2) {
    // Clear search
    lineViewerRef.value.clearLineSearch()
    searchCurrentMatchIndex.value = 0
    searchTotalMatches.value = 0
    return
  }

  // Perform search
  lineViewerRef.value.performLineSearch(query)
  // Update state after search
  nextTick(() => updateSearchState())
}

function handleSearchNext() {
  if (!lineViewerRef.value) return
  lineViewerRef.value.nextLineSearchMatch()
  // Update state after navigation
  nextTick(() => updateSearchState())
}

function handleSearchPrevious() {
  if (!lineViewerRef.value) return
  lineViewerRef.value.previousLineSearchMatch()
  // Update state after navigation
  nextTick(() => updateSearchState())
}

function handleSearchClose() {
  if (lineViewerRef.value) {
    lineViewerRef.value.clearLineSearch()
  }
  isSearchOpen.value = false
}

// Keyboard shortcut for search
useEventListener('keydown', (event: KeyboardEvent) => {
  const hasCtrlOrMeta = event.ctrlKey || event.metaKey

  if (hasCtrlOrMeta && event.code === 'KeyF') {
    event.preventDefault()
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
