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
          <TocTreeView v-if="myTab?.bookState?.isTocOpen && myTab?.bookState?.bookId"
                       ref="tocTreeViewRef"
                       :toc-entries="filteredTocEntries"
                       :is-loading="isTocLoading"
                       :is-compact-mode="!myTab.bookState.isFirstTocOpen"
                       :current-toc-entry-id="currentTocEntryId"
                       class="toc-overlay"
                       @select-line="handleTocSelection" />
        </keep-alive>

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
            <CommentaryView :book-id="myTab.bookState.bookId"
                            :selected-line-index="myTab.bookState.selectedLineIndex"
                            :book="currentBook"
                            :flat-toc-entries="flatTocEntries"
                            @navigate-line="handleNavigateLine" />
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
import { ref } from 'vue'
import TocTreeView from '@/components/book/TocTreeView.vue'
import LineView from '@/components/book/LineView.vue'
import SplitPane from '@/components/shared/SplitPane.vue'
import CommentaryView from '@/components/commentary/CommentaryView.vue'
import LineViewToolbar from '@/components/book/LineViewToolbar.vue'
import { useBookViewPage } from '@/components/book/useBookViewPage'

const myTabId = ref<number | undefined>(undefined)
const lineViewerRef = ref<InstanceType<typeof LineView> | null>(null)
const tocTreeViewRef = ref<InstanceType<typeof TocTreeView> | null>(null)
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
  handleBackgroundClick
} = useBookViewPage(
  () => myTabId.value,
  () => lineViewerRef.value
)

// Initialize myTabId from active tab
import { useTabs } from '@/components/workspace/useTabs'
const { activeTab } = useTabs()
myTabId.value = activeTab.value?.id
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
