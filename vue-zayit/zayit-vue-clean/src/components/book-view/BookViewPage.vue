<script setup lang="ts">
import { ref, watch, onMounted, onBeforeUnmount, computed, nextTick } from 'vue'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useTabStore } from '@/stores/tabStore'
import { useToc } from './useToc'
import { useLines } from './useLines'
import { useCommentary } from './useCommentary'
import { useBookViewSearch } from './useBookViewSearch'
import { useCommentarySearch } from './useCommentarySearch'
import BookViewToolbar from './BookViewToolbar.vue'
import BookViewSplitPane from './BookViewSplitPane.vue'
import BookViewLinesContent from './BookViewLinesContent.vue'
import BookViewBottomPanel from './BookViewBottomPanel.vue'
import BookViewSearchBar from './BookViewSearchBar.vue'
import BookViewTocTree from './BookViewTocTree.vue'
import CommentaryView from './CommentaryView.vue'
import type { TocEntry } from './useToc'
import type { SearchMode } from './BookViewSearchBar.vue'

const bookViewStore = useBookViewStore()
const tabStore = useTabStore()
const tabId = tabStore.activeTabId
const bookId = tabStore.activeTab.bookId
const bookTitle = tabStore.activeTab.title
const openToc = tabStore.activeTab.openToc ?? false
if (openToc) tabStore.updateActiveTab({ openToc: false })

const bottomVisible = ref(false)
const searchVisible = ref(false)
const tocVisible = ref(openToc)
const selectedLineId = ref<number | null>(null)
const searchMode = ref<SearchMode>('content')

const linesContentRef = ref<InstanceType<typeof BookViewLinesContent> | null>(null)
const commentaryViewRef = ref<InstanceType<typeof CommentaryView> | null>(null)

const { getActiveTocEntry, getTocPath, altTocSections } = useToc(() => bookId, () => bookTitle)
const { lines } = useLines(() => bookId)
const { groups, loading: commentaryLoading } = useCommentary(() => selectedLineId.value)
const contentSearch = useBookViewSearch(() => lines.value)
const commentarySearch = useCommentarySearch(() => groups.value)

const activeSearch = computed(() => searchMode.value === 'content' ? contentSearch : commentarySearch)
const activeMatchCount = computed(() => activeSearch.value.matchCount.value)
const activeMatchIdx = computed(() => activeSearch.value.currentMatchIdx.value)

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

const activeTocEntryId = ref<number | undefined>(undefined)

function onLinesScrolled(lineIndex: number) {
  if (tocScrolling) return
  const entry = getActiveTocEntry(lineIndex)
  if (entry && entry.id !== activeTocEntryId.value) {
    activeTocEntryId.value = entry.id
    tabStore.updateActiveTab({ tocPath: getTocPath(entry) })
  }
}

onMounted(async () => {
  const saved = await tabStore.getTabViewState(tabId)
  if (saved) {
    bottomVisible.value = saved.bottomVisible
    if (!openToc) tocVisible.value = saved.tocVisible
  }
  if (bookId != null) {
    const bookSaved = await tabStore.getBookViewState(tabId, bookId)
    if (bookSaved?.selectedLineId != null) {
      selectedLineId.value = bookSaved.selectedLineId
      bottomVisible.value = true
    }
  }
})

onBeforeUnmount(() => {
  tabStore.updateActiveTab({ tocPath: undefined })
})

watch(bottomVisible, (val) => tabStore.setTabViewState(tabId, { bottomVisible: val, tocVisible: tocVisible.value }))
watch(tocVisible, (val) => tabStore.setTabViewState(tabId, { bottomVisible: bottomVisible.value, tocVisible: val }))
watch(searchVisible, (v) => { if (!v) { contentSearch.clear(); commentarySearch.clear() } })

let tocScrolling = false
let tocScrollTimer: ReturnType<typeof setTimeout> | null = null

function onTocSelect(entry: TocEntry) {
  if (entry.lineId == null) return
  tocScrolling = true
  if (tocScrollTimer) clearTimeout(tocScrollTimer)
  activeTocEntryId.value = entry.id
  tabStore.updateActiveTab({ tocPath: getTocPath(entry) })
  linesContentRef.value?.scrollToLineId(entry.lineId)
  tocScrollTimer = setTimeout(() => { tocScrolling = false }, 500)
}

function onAltTocSelect(entry: TocEntry) {
  if (entry.lineId == null) return
  linesContentRef.value?.scrollToLineId(entry.lineId)
  const lineIndex = entry.lineIndex
  if (lineIndex != null) {
    const mainEntry = getActiveTocEntry(lineIndex)
    if (mainEntry) {
      activeTocEntryId.value = mainEntry.id
      tabStore.updateActiveTab({ tocPath: getTocPath(mainEntry) })
    }
  }
}

function onModeChange(mode: SearchMode) {
  const currentQuery = activeSearch.value.query.value
  contentSearch.clear()
  commentarySearch.clear()
  searchMode.value = mode
  if (!currentQuery) return
  const target = mode === 'content' ? contentSearch : commentarySearch
  target.query.value = currentQuery
  nextTick(() => {
    if (mode === 'content' && contentSearch.matchLineIndices.value.length) {
      linesContentRef.value?.scrollToLineIndex(contentSearch.matchLineIndices.value[0]!)
    } else if (mode === 'commentary' && commentarySearch.matchFlatIndices.value.length) {
      commentaryViewRef.value?.scrollToFlatIndex(commentarySearch.matchFlatIndices.value[0]!)
    }
  })
}

function onQueryChange(q: string) {
  activeSearch.value.query.value = q
  if (searchMode.value === 'content' && contentSearch.matchLineIndices.value.length) {
    linesContentRef.value?.scrollToLineIndex(contentSearch.matchLineIndices.value[0]!)
  } else if (searchMode.value === 'commentary' && commentarySearch.matchFlatIndices.value.length) {
    commentaryViewRef.value?.scrollToFlatIndex(commentarySearch.matchFlatIndices.value[0]!)
  }
}

function onSearchNext() {
  activeSearch.value.next()
  if (searchMode.value === 'content') {
    linesContentRef.value?.scrollToLineIndex(contentSearch.currentMatchLineIndex.value)
  } else {
    commentaryViewRef.value?.scrollToFlatIndex(commentarySearch.currentMatchFlatIndex.value)
  }
}

function onSearchPrev() {
  activeSearch.value.prev()
  if (searchMode.value === 'content') {
    linesContentRef.value?.scrollToLineIndex(contentSearch.currentMatchLineIndex.value)
  } else {
    commentaryViewRef.value?.scrollToFlatIndex(commentarySearch.currentMatchFlatIndex.value)
  }
}

function onLineSelected(lineId: number) {
  selectedLineId.value = lineId
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
        :match-count="activeMatchCount"
        :current-match="activeMatchIdx"
        :commentary-visible="bottomVisible"
        @close="searchVisible = false"
        @query-change="onQueryChange"
        @next="onSearchNext"
        @prev="onSearchPrev"
        @mode-change="onModeChange"
      />
      <BookViewSplitPane :bottom-visible="bottomVisible">
        <template #top>
          <BookViewLinesContent
            ref="linesContentRef"
            :alt-toc-label-map="altTocLabelMap"
            :selected-line-id="selectedLineId"
            :bottom-visible="bottomVisible"
            :search-query="searchMode === 'content' ? contentSearch.query.value : ''"
            :current-match-line-index="searchMode === 'content' ? contentSearch.currentMatchLineIndex.value : undefined"
            @scrolled="onLinesScrolled"
            @line-selected="onLineSelected"
          />
        </template>
        <template #bottom>
          <CommentaryView
            ref="commentaryViewRef"
            :selected-line-id="selectedLineId"
            :groups="groups"
            :loading="commentaryLoading"
            :search-query="searchMode === 'commentary' ? commentarySearch.query.value : ''"
            :current-match-flat-index="searchMode === 'commentary' ? commentarySearch.currentMatchFlatIndex.value : undefined"
            @close="bottomVisible = false"
          />
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
        @alt-select="onAltTocSelect"
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
