<script setup lang="ts">
import { ref, watch, onMounted, onBeforeUnmount, computed, nextTick } from 'vue'
import { storeToRefs } from 'pinia'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useTabStore } from '@/stores/tabStore'
import { useZoomHandler } from '@/composables/useZoom'
import { useToc } from './useToc'
import { useLines } from './useLinesTable'
import { useCommentary } from './useCommentary'
import { useBookViewSearch } from './useBookViewSearch'
import { useCommentarySearch } from './useCommentarySearch'
import {
  findNextCommentarySection,
  findPrevCommentarySection,
  findNextTocCommentarySection,
  findPrevTocCommentarySection,
} from '@/utils/commentaryNav'
import { query } from '@/host/db'
import { SQL } from '@/host/queries.sql'
import BookViewToolbar from './BookViewToolbar.vue'
import BookViewSplitPane from './BookViewSplitPane.vue'
import BookViewLinesContent from './BookViewLinesContent.vue'
import BookViewSearchBar from './BookViewSearchBar.vue'
import BookViewTocTree from './BookViewTocTree.vue'
import CommentaryView from './CommentaryView.vue'
import type { TocEntry } from './useToc'
import type { SearchMode } from './BookViewSearchBar.vue'

const bookViewStore = useBookViewStore()
const tabStore = useTabStore()
const { zoom, isBookViewActive } = storeToRefs(bookViewStore)

useZoomHandler({ zoom, enabled: isBookViewActive })
const tabId = tabStore.activeTabId
const bookId = tabStore.activeTab.bookId
const bookTitle = tabStore.activeTab.title
const openTocEntryId = tabStore.activeTab.openTocEntryId
const openTocLineIndex = tabStore.activeTab.openTocLineIndex
const searchHighlightLineIndex = tabStore.activeTab.searchHighlightLineIndex
const searchHighlightQuery = tabStore.activeTab.searchHighlightQuery ?? ''
if (openTocEntryId != null)
  tabStore.updateActiveTab({
    openTocEntryId: undefined,
    openTocLineIndex: undefined,
    searchHighlightLineIndex: undefined,
    searchHighlightQuery: undefined,
  })

const bottomVisible = ref(false)
const searchVisible = ref(false)
const tocVisible = ref(false)
const selectedLineId = ref<number | null>(null)
const searchMode = ref<SearchMode>('content')
const activeTocEntryId = ref<number | undefined>(undefined)
const initialLineIndex = ref<number | undefined>(openTocLineIndex)
const initialScrollTop = ref<number | undefined>()
const initialScrollOffset = ref<number>(0)
const scrollStateReady = ref(openTocLineIndex != null) // if TOC nav, no need to wait for IDB

const linesContentRef = ref<InstanceType<typeof BookViewLinesContent> | null>(null)
const commentaryViewRef = ref<InstanceType<typeof CommentaryView> | null>(null)
const searchBarRef = ref<InstanceType<typeof BookViewSearchBar> | null>(null)

const {
  getActiveTocEntry,
  getTocPath,
  altTocSections,
  tocEntries,
  loading: tocLoading,
  error: tocError,
} = useToc(
  () => bookId,
  () => bookTitle,
)
const { lines, prioritise } = useLines(() => bookId)

// When selected line is a toc entry line, collect all line IDs in that section
const selectedSectionLineIds = computed<number[] | null>(() => {
  if (selectedLineId.value == null || !tocEntries.value.length || !lines.value.length) return null
  const tocEntry = tocEntries.value.find((e) => e.lineId === selectedLineId.value)
  if (!tocEntry || tocEntry.lineIndex == null) return null
  // find next toc entry at same or higher level to determine section end
  const idx = tocEntries.value.indexOf(tocEntry)
  const nextEntry = tocEntries.value
    .slice(idx + 1)
    .find((e) => e.lineIndex != null && e.level <= tocEntry.level)
  const fromIndex = tocEntry.lineIndex
  const toIndex = nextEntry?.lineIndex ?? lines.value.length
  return lines.value
    .filter((l) => l.lineIndex >= fromIndex && l.lineIndex < toIndex)
    .map((l) => l.id)
})

const { groups, loading: commentaryLoading } = useCommentary(
  () => selectedLineId.value,
  () => selectedSectionLineIds.value,
)
const contentSearch = useBookViewSearch(
  () => lines.value,
  () => currentScrollLineIndex.value,
)
const commentarySearch = useCommentarySearch(
  () => groups.value,
  () => commentaryViewRef.value?.topVisibleFlatIndex ?? 0,
)

const activeSearch = computed(() =>
  searchMode.value === 'content' ? contentSearch : commentarySearch,
)
const activeMatchCount = computed(() => activeSearch.value.matchCount.value)
const activeMatchIdx = computed(() => activeSearch.value.currentMatchIdx.value)

const altTocLabelMap = computed(() => {
  const map = new Map<number, string>()
  for (const section of altTocSections.value)
    for (const entry of section.entries) {
      if (entry.lineIndex == null) continue
      const existing = map.get(entry.lineIndex)
      map.set(entry.lineIndex, existing ? `${existing} / ${entry.text}` : entry.text)
    }
  return map
})

if (openTocEntryId != null) {
  const stop = watch(tocEntries, (entries) => {
    if (!entries.length) return
    const entry = entries.find((e) => e.id === openTocEntryId)
    if (entry != null) activeTocEntryId.value = entry.id
    stop()
  })
}

let tocScrolling = false
let tocScrollTimer: ReturnType<typeof setTimeout> | null = null

const currentScrollLineIndex = ref(0)

function onLinesScrolled(lineIndex: number) {
  currentScrollLineIndex.value = lineIndex
  if (tocScrolling) return
  const entry = getActiveTocEntry(lineIndex)
  if (entry && entry.id !== activeTocEntryId.value) {
    activeTocEntryId.value = entry.id
    tabStore.updateActiveTab({ tocPath: getTocPath(entry) })
  }
}

function onTocSelect(entry: TocEntry) {
  if (entry.lineId == null) return
  tocScrolling = true
  if (tocScrollTimer) clearTimeout(tocScrollTimer)
  activeTocEntryId.value = entry.id
  tabStore.updateActiveTab({ tocPath: getTocPath(entry) })
  linesContentRef.value?.scrollToLineId(entry.lineId)
  tocScrollTimer = setTimeout(() => {
    tocScrolling = false
  }, 500)
}

function onAltTocSelect(entry: TocEntry) {
  if (entry.lineId == null) return
  linesContentRef.value?.scrollToLineId(entry.lineId)
  if (entry.lineIndex != null) {
    const mainEntry = getActiveTocEntry(entry.lineIndex)
    if (mainEntry) {
      activeTocEntryId.value = mainEntry.id
      tabStore.updateActiveTab({ tocPath: getTocPath(mainEntry) })
    }
  }
}

function scrollContentMatch() {
  if (searchMode.value === 'content') {
    if (contentSearch.currentMatchLineIndex.value === -1) return
    linesContentRef.value?.scrollToLineIndex(contentSearch.currentMatchLineIndex.value)
  } else {
    if (commentarySearch.currentMatchFlatIndex.value === -1) return
    commentaryViewRef.value?.scrollToFlatIndex(commentarySearch.currentMatchFlatIndex.value)
  }
}

function openSearch(mode: SearchMode) {
  searchVisible.value = true
  searchMode.value = mode
}

function openContentSearch() {
  openSearch('content')
  nextTick(() => searchBarRef.value?.focus())
}

function openCommentarySearch() {
  openSearch('commentary')
  nextTick(() => searchBarRef.value?.focus())
}

function openBookInTab(bookId: number, lineIndex: number | undefined) {
  tabStore.openTab({
    title: groups.value.find((g) => g.bookId === bookId)?.bookTitle ?? '',
    route: '/book-view',
    bookId,
    openTocLineIndex: lineIndex,
  })
}

function onModeChange(mode: SearchMode) {
  const currentQuery = activeSearch.value.query.value
  contentSearch.clear()
  commentarySearch.clear()
  searchMode.value = mode
  if (!currentQuery) return
  const target = mode === 'content' ? contentSearch : commentarySearch
  target.query.value = currentQuery
  nextTick(() => scrollContentMatch())
}

function onQueryChange(q: string) {
  activeSearch.value.query.value = q
  scrollContentMatch()
}

function onSearchNext() {
  activeSearch.value.next()
  scrollContentMatch()
}
function onSearchPrev() {
  activeSearch.value.prev()
  scrollContentMatch()
}

let pendingNavStop: (() => void) | null = null

async function onNavigateSection(direction: 'next' | 'prev', commentaryBookId: number) {
  if (selectedLineId.value == null || bookId == null) return
  if (pendingNavStop) {
    pendingNavStop()
    pendingNavStop = null
  }

  // toc mode: navigate to next/prev toc entry at same level that has commentary
  const currentTocEntry = tocEntries.value.find((e) => e.lineId === selectedLineId.value)
  if (currentTocEntry) {
    const fn = direction === 'next' ? findNextTocCommentarySection : findPrevTocCommentarySection
    const entry = await fn(bookId, commentaryBookId, currentTocEntry, tocEntries.value)
    if (entry == null || entry.lineId == null) return
    selectedLineId.value = entry.lineId
    bottomVisible.value = true
    linesContentRef.value?.scrollToLineId(entry.lineId)
    const stop = watch(
      commentaryLoading,
      (loading) => {
        if (loading) return
        if (selectedLineId.value !== entry.lineId) return
        pendingNavStop = null
        stop()
        nextTick(() => commentaryViewRef.value?.scrollToGroup(commentaryBookId))
      },
      { flush: 'sync' },
    )
    pendingNavStop = stop
    return
  }

  // normal mode: navigate to next/prev line with commentary for this book
  const currentLine = lines.value.find((l) => l.id === selectedLineId.value)
  if (currentLine == null) return
  const fn = direction === 'next' ? findNextCommentarySection : findPrevCommentarySection
  const result = await fn(bookId, commentaryBookId, currentLine.lineIndex)
  if (result == null) return
  selectedLineId.value = result.id
  bottomVisible.value = true
  linesContentRef.value?.scrollToLineId(result.id)
  const stop = watch(
    commentaryLoading,
    (loading) => {
      if (loading) return
      if (selectedLineId.value !== result.id) return
      pendingNavStop = null
      stop()
      nextTick(() => commentaryViewRef.value?.scrollToGroup(commentaryBookId))
    },
    { flush: 'sync' },
  )
  pendingNavStop = stop
}

onMounted(async () => {
  const saved = await tabStore.getTabViewState(tabId)
  if (saved) {
    bottomVisible.value = saved.bottomVisible
  }
  if (bookId != null) {
    const bookSaved = await tabStore.getBookViewState(tabId, bookId)
    const lastRead = await tabStore.getLastReadPos(bookId)
    const restoredLineId = bookSaved?.selectedLineId ?? lastRead?.selectedLineId
    const si = bookSaved?.commentaryScrollIndex ?? lastRead?.commentaryScrollIndex
    const so = bookSaved?.commentaryScrollOffset ?? lastRead?.commentaryScrollOffset
    // restore zoom for this tab+book
    if (bookSaved?.zoom != null) bookViewStore.setZoom(tabId, bookId, bookSaved.zoom)
    // restore scroll position — only if not navigating to a specific TOC entry
    if (openTocLineIndex == null) {
      const scrollIndex = bookSaved?.scrollIndex ?? lastRead?.scrollIndex
      const scrollOffset = bookSaved?.scrollOffset ?? lastRead?.scrollOffset
      if (scrollIndex != null) {
        initialScrollTop.value = scrollIndex
        initialScrollOffset.value = scrollOffset ?? 0
      }
    }
    scrollStateReady.value = true
    if (restoredLineId != null) {
      selectedLineId.value = restoredLineId
      bottomVisible.value = true
    }
    if (si != null && so != null) {
      const stop = watch(
        commentaryLoading,
        async (loading) => {
          if (loading) return
          stop()
          await nextTick()
          if (commentaryViewRef.value) {
            commentaryViewRef.value.restoreCommentaryScrollPos(si, so)
          } else {
            const stopRef = watch(commentaryViewRef, (ref) => {
              if (!ref) return
              stopRef()
              ref.restoreCommentaryScrollPos(si, so)
            })
          }
        },
        { flush: 'sync' },
      )
    }
  }
})

onBeforeUnmount(() => tabStore.updateActiveTab({ tocPath: undefined }))

const pinnedCommentaryBookId = ref<number | null>(null)
const commentaryScrollIndex = ref<number | null>(null)
const commentaryScrollOffset = ref<number | null>(null)

// Fetch the first default commentator for this book (used as initial pin when no user selection exists)
let defaultCommentatorBookId: number | null = null
if (bookId != null) {
  query<{ commentatorBookId: number }>(SQL.GET_DEFAULT_COMMENTATOR, [bookId]).then((rows) => {
    defaultCommentatorBookId = rows[0]?.commentatorBookId ?? null
  })
}

function onCommentaryScroll(si: number, so: number) {
  commentaryScrollIndex.value = si
  commentaryScrollOffset.value = so
}

watch(selectedLineId, () => {
  if (commentaryViewRef.value?.activeBookId) {
    pinnedCommentaryBookId.value = commentaryViewRef.value.activeBookId
  } else if (defaultCommentatorBookId != null) {
    pinnedCommentaryBookId.value = defaultCommentatorBookId
  }
})
watch(bottomVisible, (val) => tabStore.setTabViewState(tabId, { bottomVisible: val }))
watch(
  () => bookViewStore.toggleBottomPanelSignal,
  () => {
    bottomVisible.value = !bottomVisible.value
  },
)
watch(searchVisible, (v) => {
  if (!v) {
    contentSearch.clear()
    commentarySearch.clear()
  }
})
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
      <BookViewSplitPane :bottom-visible="bottomVisible">
        <template #top>
          <BookViewLinesContent
            v-if="scrollStateReady"
            ref="linesContentRef"
            :lines="lines"
            :prioritise="prioritise"
            :alt-toc-label-map="altTocLabelMap"
            :selected-line-id="selectedLineId"
            :bottom-visible="bottomVisible"
            :initial-line-index="initialLineIndex"
            :initial-scroll-index="initialScrollTop"
            :initial-scroll-offset="initialScrollOffset"
            :search-highlight-line-index="searchHighlightLineIndex"
            :search-highlight-query="searchHighlightQuery"
            :commentary-scroll-index="commentaryScrollIndex"
            :commentary-scroll-offset="commentaryScrollOffset"
            :search-query="searchMode === 'content' ? contentSearch.query.value : ''"
            :current-match-line-index="
              searchMode === 'content' ? contentSearch.currentMatchLineIndex.value : undefined
            "
            :current-match-occurrence="
              searchMode === 'content' ? contentSearch.currentMatchOccurrence.value : undefined
            "
            @scrolled="onLinesScrolled"
            @line-selected="selectedLineId = $event"
            @ctrl-f="openContentSearch"
          />
        </template>
        <template #bottom>
          <CommentaryView
            ref="commentaryViewRef"
            :selected-line-id="selectedLineId"
            :groups="groups"
            :loading="commentaryLoading"
            :pinned-book-id="pinnedCommentaryBookId"
            :search-query="searchMode === 'commentary' ? commentarySearch.query.value : ''"
            :current-match-flat-index="
              searchMode === 'commentary' ? commentarySearch.currentMatchFlatIndex.value : undefined
            "
            :current-match-occurrence="
              searchMode === 'commentary'
                ? commentarySearch.currentMatchOccurrence.value
                : undefined
            "
            @close="bottomVisible = false"
            @navigate-section="onNavigateSection"
            @scroll="onCommentaryScroll"
            @toggle-search="openCommentarySearch"
            @open-book="openBookInTab"
          />
        </template>
      </BookViewSplitPane>
      <BookViewSearchBar
        ref="searchBarRef"
        :visible="searchVisible"
        :match-count="activeMatchCount"
        :current-match="activeMatchIdx"
        :commentary-visible="bottomVisible"
        :mode="searchMode"
        @close="searchVisible = false"
        @query-change="onQueryChange"
        @next="onSearchNext"
        @prev="onSearchPrev"
        @mode-change="onModeChange"
      />
      <BookViewTocTree
        v-show="tocVisible"
        :book-id="bookId"
        :book-title="bookTitle"
        :active-toc-entry-id="activeTocEntryId"
        :visible="tocVisible"
        :toc-entries="tocEntries"
        :alt-toc-sections="altTocSections"
        :loading="tocLoading"
        :error="tocError"
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
