<script setup lang="ts">
import { ref, watch } from 'vue'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useTabStore } from '@/stores/tabStore'
import BookViewToolbar from './BookViewToolbar.vue'
import BookViewSplitPane from './BookViewSplitPane.vue'
import BookViewLinesContent from './BookViewLinesContent.vue'
import BookViewBottomPanel from './BookViewBottomPanel.vue'
import BookViewSearchBar from './BookViewSearchBar.vue'
import BookViewTocTree from './BookViewTocTree.vue'

const bookViewStore = useBookViewStore()
const tabStore = useTabStore()
const tabId = tabStore.activeTabId

const saved = bookViewStore.getTabState(tabId)
const bottomVisible = ref(saved.bottomVisible)
const searchVisible = ref(false)

watch(bottomVisible, (val) => bookViewStore.setTabState(tabId, { bottomVisible: val }))
</script>

<template>
  <div class="book-view">
    <BookViewToolbar
      v-if="bookViewStore.toolbarVisible"
      :bottom-visible="bottomVisible"
      :search-visible="searchVisible"
      @toggle-bottom="bottomVisible = !bottomVisible"
      @toggle-search="searchVisible = !searchVisible"
    />
    <div class="content-area">
      <BookViewSearchBar
        :visible="searchVisible"
        :toolbar-visible="bookViewStore.toolbarVisible"
        @close="searchVisible = false"
      />
      <BookViewSplitPane :bottom-visible="bottomVisible">
        <template #top>
          <BookViewLinesContent />
        </template>
        <template #bottom>
          <BookViewBottomPanel />
        </template>
      </BookViewSplitPane>
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
