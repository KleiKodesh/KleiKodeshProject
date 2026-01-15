<template>
  <div class="flex-column height-fill book-view-wrapper">
    <keep-alive>
      <BookTocTreeView v-if="myTab?.bookState?.isTocOpen && myTab?.bookState?.bookId"
                       ref="tocTreeViewRef"
                       :toc-entries="tocEntries"
                       :is-loading="isTocLoading"
                       class="toc-overlay"
                       @select-line="handleTocSelection" />
    </keep-alive>

    <SplitPane v-if="myTab?.bookState?.bookId"
               :show-bottom="myTab.bookState.showBottomPane || false">
      <template #top>
        <BookLineViewer ref="lineViewerRef"
                        :tab-id="myTabId"
                        :alt-toc-by-line-index="altTocByLineIndex"
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
import { ref, computed, watch } from 'vue'
import { useTabStore } from '../../stores/tabStore'

import BookTocTreeView from '../BookTocTreeView.vue'
import BookLineViewer from '../BookLineViewer.vue'
import SplitPane from '../common/SplitPane.vue'
import BookCommentaryView from '../BookCommentaryView.vue'
import { dbManager } from '../../data/dbManager'
import { buildTocFromFlat } from '../../data/tocBuilder'
import type { AltTocLineEntry } from '../../data/tocBuilder'
import type { TocEntry } from '../../types/BookToc'

const tabStore = useTabStore()
const myTabId = ref<number | undefined>(tabStore.activeTab?.id)
const myTab = computed(() => tabStore.tabs.find(t => t.id === myTabId.value))

const lineViewerRef = ref<InstanceType<typeof BookLineViewer> | null>(null)
const altTocByLineIndex = ref<Map<number, AltTocLineEntry[]>>(new Map())
const tocEntries = ref<TocEntry[]>([])
const isTocLoading = ref(false)

// Load TOC data when book changes
watch(() => myTab.value?.bookState?.bookId, async (bookId) => {
    if (bookId) {
        await loadTocData(bookId)
    }
}, { immediate: true })

async function loadTocData(bookId: number) {
    isTocLoading.value = true
    try {
        const { tocEntriesFlat } = await dbManager.getToc(bookId)
        const { tree, altTocByLineIndex: altTocMap } = buildTocFromFlat(tocEntriesFlat)
        
        // Store both the tree and the alt TOC map
        tocEntries.value = tree
        altTocByLineIndex.value = altTocMap
    } catch (error) {
        console.error('❌ Failed to load TOC data:', error)
        tocEntries.value = []
        altTocByLineIndex.value = new Map()
    } finally {
        isTocLoading.value = false
    }
}

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
