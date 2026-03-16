<script setup lang="ts">
import { ref, watch, computed } from 'vue'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useTabStore } from '@/stores/tabStore'
import BookViewToolbar from './BookViewToolbar.vue'
import BookViewSplitPane from './BookViewSplitPane.vue'
import BookViewLinesContent from './BookViewLinesContent.vue'
import BookViewBottomPanel from './BookViewBottomPanel.vue'
import BookViewSearchBar from './BookViewSearchBar.vue'
import BookViewTocTree from './BookViewTocTree.vue'
import type { TocEntry } from './useToc'

const bookViewStore = useBookViewStore()
const tabStore = useTabStore()
const tabId = tabStore.activeTabId
const bookId = computed(() => tabStore.activeTab.bookId)

const saved = bookViewStore.getTabState(tabId)
const bottomVisible = ref(saved.bottomVisible)
const searchVisible = ref(false)
const tocVisible = ref(saved.tocVisible ?? true)
const linesContentRef = ref<InstanceType<typeof BookViewLinesContent> | null>(null)

watch(bottomVisible, (val) => bookViewStore.setTabState(tabId, { bottomVisible: val }))
watch(tocVisible, (val) => bookViewStore.setTabState(tabId, { tocVisible: val }))

function onTocSelect(entry: TocEntry) {
  if (entry.lineId != null) linesContentRef.value?.scrollToLineId(entry.lineId)
}
</script>

<template>
  <div class="book-view">
    <BookViewToolbar
      v-if="bookViewStore.toolbarVisible"
      :bottom-visible="bottomVisible"
      :search-visible="searchVisible"
      :toc-visible="tocVisible"
      @toggle-bottom="bottomVisible = !bottomVisible"
      @toggle-search="searchVisible = !searchVisible"
      @toggle-toc="tocVisible = !tocVisible"
    />
    <div class="content-area">
      <BookViewSearchBar
        :visible="searchVisible"
        :toolbar-visible="bookViewStore.toolbarVisible"
        @close="searchVisible = false"
      />
      <BookViewSplitPane :bottom-visible="bottomVisible">
        <template #top>
          <BookViewLinesContent ref="linesContentRef" />
        </template>
        <template #bottom>
          <BookViewBottomPanel />
        </template>
      </BookViewSplitPane>
      <BookViewTocTree
        v-if="tocVisible"
        :book-id="bookId"
        :book-title="tabStore.activeTab.title"
        @close="tocVisible = false"
        @select="onTocSelect"
      />
    </div>
  </div>
</template>

<style scoped>
.book-view {
  display: flex;
  flex-direction: column;
  height: 100%;
  background: var(--bg-primary);
}
.content-area {
  position: relative;
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}
</style>
