<template>
  <div class="flex-column height-fill book-view-wrapper"
       @click="handleBackgroundClick">
    <!-- Virtualized viewer is now always enabled -->
    <keep-alive>
      <BookTocTreeView v-if="myTab?.bookState?.isTocOpen && myTab?.bookState?.bookId"
                       ref="tocTreeViewRef"
                       :toc-entries="tocEntries"
                       :is-loading="isTocLoading"
                       :is-compact-mode="!myTab.bookState.isFirstTocOpen"
                       class="toc-overlay"
                       @select-line="handleTocSelection" />
    </keep-alive>

    <SplitPane v-if="myTab?.bookState?.bookId"
               :show-bottom="myTab.bookState.showBottomPane || false">
      <template #top>
        <BookLineViewer ref="lineViewerRef"
                        :tab-id="myTabId"
                        :alt-toc-by-line-index="altTocByLineIndex"
                        :flat-toc-entries="flatTocEntries"
                        class="flex-110" />
      </template>
      <template #bottom>
        <BookCommentaryView :book-id="myTab.bookState.bookId"
                            :selected-line-index="myTab.bookState.selectedLineIndex"
                            :book="currentBook"
                            @navigate-line="handleNavigateLine" />
      </template>
    </SplitPane>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { useTabStore } from '../../stores/tabStore'
import { useCategoryTreeStore } from '../../stores/categoryTreeStore'

import BookTocTreeView from '../BookTocTreeView.vue'
import BookLineViewer from '../BookLineViewer.vue'
import SplitPane from '../common/SplitPane.vue'
import BookCommentaryView from '../BookCommentaryView.vue'
import { dbService } from '../../services/dbService'
import { buildTocFromFlat } from '../../services/bookTocService'
import type { AltTocLineEntry } from '../../services/bookTocService'
import type { TocEntry } from '../../types/BookToc'
import type { Book } from '../../types/Book'

const tabStore = useTabStore()
const categoryTreeStore = useCategoryTreeStore()
const myTabId = ref<number | undefined>(tabStore.activeTab?.id)
const myTab = computed(() => tabStore.tabs.find(t => t.id === myTabId.value))

const lineViewerRef = ref<InstanceType<typeof BookLineViewer> | null>(null)
const altTocByLineIndex = ref<Map<number, AltTocLineEntry[]>>(new Map())
const tocEntries = ref<TocEntry[]>([])
const flatTocEntries = ref<TocEntry[]>([])
const isTocLoading = ref(false)

// Get current book from the category tree store
const currentBook = computed(() => {
  const bookId = myTab.value?.bookState?.bookId
  if (!bookId) return undefined
  return categoryTreeStore.allBooks.find(book => book.id === bookId)
})

// Load TOC data when book changes
watch(() => myTab.value?.bookState?.bookId, async (bookId) => {
  if (bookId) {
    await loadTocData(bookId)

    // Check if we should highlight the initial line (from search or commentary navigation)
    if (myTab.value?.bookState?.shouldHighlight && myTab.value?.bookState?.initialLineIndex !== undefined) {
      // Wait for viewer to be ready, then trigger highlight
      setTimeout(() => {
        const viewer: any = lineViewerRef.value
        const highlightTerms = myTab.value?.searchState?.highlightTerms
        const highlightSnippet = myTab.value?.searchState?.highlightSnippet

        if (viewer?.scrollToLineWithFadeHighlight) {
          // Highlight line with optional search terms and snippet
          viewer.scrollToLineWithFadeHighlight(myTab.value!.bookState!.initialLineIndex!, highlightTerms, highlightSnippet)
        }

        // Clear the flags so they don't highlight again
        if (myTab.value?.bookState) {
          myTab.value.bookState.shouldHighlight = false
        }
        if (myTab.value?.searchState) {
          myTab.value.searchState.highlightTerms = undefined
          myTab.value.searchState.highlightSnippet = undefined
        }
      }, 500) // Wait for book to load
    }
  }
}, { immediate: true })

async function loadTocData(bookId: number) {
  isTocLoading.value = true
  try {
    const { tocEntriesFlat } = await dbService.getToc(bookId)
    const { tree, allTocs, altTocByLineIndex: altTocMap } = buildTocFromFlat(tocEntriesFlat)

    // Store both the tree and the alt TOC map
    tocEntries.value = tree
    altTocByLineIndex.value = altTocMap
    flatTocEntries.value = allTocs // Store flat TOC for line click detection
  } catch (error) {
    console.error('❌ Failed to load TOC data:', error)
    tocEntries.value = []
    altTocByLineIndex.value = new Map()
    flatTocEntries.value = []
  } finally {
    isTocLoading.value = false
  }
}

function handleTocSelection(lineIndex: number) {
  const viewer: any = lineViewerRef.value
  if (viewer?.handleTocSelection) {
    viewer.handleTocSelection(lineIndex)
  } else if (viewer?.scrollToLine) {
    // Fallback to direct scroll
    viewer.scrollToLine(lineIndex)
  }
}

function handleNavigateLine(newIndex: number) {
  // Scroll the line viewer explicitly for navigation requests coming from the commentary pane
  const viewer: any = lineViewerRef.value
  if (viewer?.scrollToLineIndex) {
    viewer.scrollToLineIndex(newIndex)
  } else if (viewer?.scrollToLine) {
    viewer.scrollToLine(newIndex)
  }
}

function handleBackgroundClick(event: MouseEvent) {
  // Close TOC if clicking outside of it (only in compact mode)
  if (myTab.value?.bookState?.isTocOpen && !myTab.value.bookState.isFirstTocOpen) {
    tabStore.closeToc()
  }
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

/* For compact mode, the TOC is now fixed positioned, so no special overlay styling needed */
</style>
