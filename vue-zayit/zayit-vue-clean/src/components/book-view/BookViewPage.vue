<script setup lang="ts">
import { ref, watch, onMounted, onUnmounted, computed } from 'vue'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useTabStore } from '@/stores/tabStore'
import { useToc } from './useToc'
import BookViewToolbar from './BookViewToolbar.vue'
import BookViewSplitPane from './BookViewSplitPane.vue'
import BookViewLinesContent from './BookViewLinesContent.vue'
import BookViewBottomPanel from './BookViewBottomPanel.vue'
import BookViewSearchBar from './BookViewSearchBar.vue'
import BookViewTocTree from './BookViewTocTree.vue'
import type { TocEntry } from './useToc'

const bookViewStore = useBookViewStore()
const tabStore = useTabStore()
const tabId = tabStore.activeTabId.value
const bookId = tabStore.activeTab.bookId
const bookTitle = tabStore.activeTab.title
const openToc = tabStore.activeTab.openToc ?? false
if (openToc) tabStore.updateActiveTab({ openToc: false })

const bottomVisible = ref(false)
const searchVisible = ref(false)
const tocVisible = ref(openToc)
const linesContentRef = ref<InstanceType<typeof BookViewLinesContent> | null>(null)
const activeTocEntryId = ref<number | undefined>(undefined)

const { getActiveTocEntry, getTocPath, altTocSections } = useToc(() => bookId, () => bookTitle)

// Map of lineIndex → alt toc label for rendering pseudo-labels in the content view
const altTocLabelMap = computed(() => {
  const map = new Map<number, string>()
  for (const section of altTocSections.value) {
    for (const entry of section.entries) {
      if (entry.lineIndex == null) continue
      const existing = map.get(entry.lineIndex)
      map.set(entry.lineIndex, existing ? `${existing} / ${entry.text}` : entry.text)
    }
  }
  return map
})

function onLinesScrolled(lineIndex: number) {
  const entry = getActiveTocEntry(lineIndex)
  if (entry && entry.id !== activeTocEntryId.value) {
    activeTocEntryId.value = entry.id
    bookViewStore.currentTocPath = getTocPath(entry)
  }
}

onMounted(async () => {
  const saved = await tabStore.getTabViewState(tabId)
  if (saved) {
    bottomVisible.value = saved.bottomVisible
    if (!openToc) tocVisible.value = saved.tocVisible
  }
})

watch(bottomVisible, (val) => tabStore.setTabViewState(tabId, { bottomVisible: val, tocVisible: tocVisible.value }))
watch(tocVisible, (val) => tabStore.setTabViewState(tabId, { bottomVisible: bottomVisible.value, tocVisible: val }))

function onTocSelect(entry: TocEntry) {
  if (entry.lineId != null) linesContentRef.value?.scrollToLineId(entry.lineId)
}

onUnmounted(() => { bookViewStore.currentTocPath = null })
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
          <BookViewLinesContent ref="linesContentRef" :alt-toc-label-map="altTocLabelMap" @scrolled="onLinesScrolled" />
        </template>
        <template #bottom>
          <BookViewBottomPanel />
        </template>
      </BookViewSplitPane>
      <BookViewTocTree
        v-show="tocVisible"
        :book-id="bookId"
        :book-title="bookTitle"
        :active-toc-entry-id="activeTocEntryId"
        :visible="tocVisible"
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
