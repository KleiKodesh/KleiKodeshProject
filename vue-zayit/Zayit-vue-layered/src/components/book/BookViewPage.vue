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
import { ref, computed, watch } from 'vue'
import { useTabs } from '@/components/workspace/useTabs'
import { useCategoryTreeStore } from '@/data/stores/categoryTreeStore'

import TocTreeView from '@/components/book/TocTreeView.vue'
import LineView from '@/components/book/LineView.vue'
import SplitPane from '@/components/shared/SplitPane.vue'
import CommentaryView from '@/components/commentary/CommentaryView.vue'
import LineViewToolbar from '@/components/book/LineViewToolbar.vue'
import { dbService } from '@/data/services/dbService'
import { buildTocFromFlat } from '@/data/services/bookTocService'
import type { AltTocLineEntry } from '@/data/services/bookTocService'
import type { TocEntry } from '@/data/types/BookToc'
import type { Book } from '@/data/types/Book'

const { tabs, activeTab } = useTabs()
const categoryTreeStore = useCategoryTreeStore()
const myTabId = ref<number | undefined>(activeTab.value?.id)
const myTab = computed(() => tabs.value.find(t => t.id === myTabId.value))

const toolbarPosition = computed(() => myTab.value?.bookState?.toolbarPosition || 'top')
const toolbarPositionClass = computed(() => `toolbar-position-${toolbarPosition.value}`)
const contentAreaClass = computed(() => `content-with-toolbar-${toolbarPosition.value}`)

const lineViewerRef = ref<InstanceType<typeof LineView> | null>(null)
const tocTreeViewRef = ref<InstanceType<typeof TocTreeView> | null>(null)
const toolbarRef = ref<InstanceType<typeof LineViewToolbar> | null>(null)
const altTocByLineIndex = ref<Map<number, AltTocLineEntry[]>>(new Map())
const tocEntries = ref<TocEntry[]>([])
const flatTocEntries = ref<TocEntry[]>([])
const isTocLoading = ref(false)
const currentCenterLineIndex = ref<number | null>(null)
const currentTocEntryId = ref<number | undefined>(undefined)

// Filtered TOC entries based on showAltToc setting and book title
const filteredTocEntries = computed(() => {
  let entries = tocEntries.value

  // If showAltToc is false, filter out the alt TOC root node (חלוקה נוספת)
  if (myTab.value?.bookState?.showAltToc === false) {
    entries = entries.filter(entry => !entry.isAltToc)
  }

  // If root entry matches book title, promote its children to root level
  const bookTitle = currentBook.value?.title
  if (bookTitle && entries.length === 1) {
    const rootEntry = entries[0]
    // Check if single root entry matches book title (case-insensitive, trimmed)
    if (rootEntry && (rootEntry.level === 0 || !rootEntry.parentId) &&
      rootEntry.text.trim().toLowerCase() === bookTitle.trim().toLowerCase() &&
      rootEntry.children && rootEntry.children.length > 0) {
      // Return the children as the new root level
      return rootEntry.children
    }
  }

  return entries
})

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
      console.log('[BookViewPage] Should highlight line:', myTab.value.bookState.initialLineIndex, 'with terms:', myTab.value?.searchState?.highlightTerms)

      // Wait for viewer to be ready, then trigger highlight
      setTimeout(() => {
        const viewer: any = lineViewerRef.value
        const highlightTerms = myTab.value?.searchState?.highlightTerms
        const highlightSnippet = myTab.value?.searchState?.highlightSnippet

        console.log('[BookViewPage] Calling scrollToLineWithFadeHighlight with:', {
          lineIndex: myTab.value!.bookState!.initialLineIndex,
          highlightTerms,
          highlightSnippet,
          viewerExists: !!viewer,
          methodExists: !!viewer?.scrollToLineWithFadeHighlight
        })

        if (viewer?.scrollToLineWithFadeHighlight) {
          // Highlight line with optional search terms and snippet
          viewer.scrollToLineWithFadeHighlight(myTab.value!.bookState!.initialLineIndex!, highlightTerms, highlightSnippet)
        } else {
          console.error('[BookViewPage] scrollToLineWithFadeHighlight not available on viewer')
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
    } else {
      console.log('[BookViewPage] Not highlighting - shouldHighlight:', myTab.value?.bookState?.shouldHighlight, 'initialLineIndex:', myTab.value?.bookState?.initialLineIndex)
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

function handleNavigateLine(newIndex: number, tocEntryId?: number) {
  // Update the selected line index in tab state
  if (myTab.value?.bookState) {
    myTab.value.bookState.selectedLineIndex = newIndex

    // Update TOC entry ID if provided (for TOC mode navigation)
    if (tocEntryId !== undefined) {
      myTab.value.bookState.selectedTocEntryId = tocEntryId
    }
  }

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
    const { closeToc } = useTabs()
    closeToc()
  }
}

</script>

<style scoped>
.book-view-wrapper {
  position: relative;
}

.content-area-wrapper {
  display: flex;
  flex-direction: row;
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
