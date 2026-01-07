<template>
  <div class="flex-column height-fill book-view-wrapper">

    <BookTocTreeView v-if="myTab?.bookState?.isTocOpen && myTab?.bookState?.bookId"
                     :book-id="myTab.bookState.bookId"
                     class="toc-overlay"
                     @select-line="handleTocSelection" />

    <SplitPane v-if="myTab?.bookState?.bookId"
               :show-bottom="myTab.bookState.showBottomPane || false">
      <template #top>
        <BookLineViewer ref="lineViewerRef"
                        :tab-id="myTabId"
                        class="flex-110" />
      </template>
      <template #bottom>
        <BookCommentaryView :book-id="myTab.bookState.bookId"
                            :selected-line-index="myTab.bookState.selectedLineIndex" />
      </template>
    </SplitPane>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import { useTabStore } from '../../stores/tabStore'

import BookTocTreeView from '../BookTocTreeView.vue' // This line is already correct
import BookLineViewer from '../BookLineViewer.vue'
import SplitPane from '../common/SplitPane.vue'
import BookCommentaryView from '../BookCommentaryView.vue'

const tabStore = useTabStore()
const myTabId = ref<number | undefined>(tabStore.activeTab?.id)
const myTab = computed(() => tabStore.tabs.find(t => t.id === myTabId.value))

const lineViewerRef = ref<InstanceType<typeof BookLineViewer> | null>(null)

function handleTocSelection(lineIndex: number) {
  lineViewerRef.value?.handleTocSelection(lineIndex)
}

</script>

<style scoped>
.book-view-wrapper {
  position: relative;
}

.toc-overlay {
  position: absolute;
  height: 100%;
  width: 100%;
  z-index: 100;
}
</style>
