<script setup lang="ts">
import { ref, watch, onMounted, onBeforeUnmount, computed, nextTick } from 'vue'
import { storeToRefs } from 'pinia'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useTabStore } from '@/stores/tabStore'
import { useSettingsStore } from '@/stores/settingsStore'
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
import { query } from '@/host/seforimDb'
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
const settingsStore = useSettingsStore()
const { zoom, isBookViewActive, toolbarPosition, autoSelectTopLine } = storeToRefs(bookViewStore)

useZoomHandler({ zoom, enabled: isBookViewActive })
const tabId = tabStore.activeTabId
const bookId = tabStore.activeTab.bookId
const bookTitle = tabStore.activeTab.title
const openTocEntryId = tabStore.activeTab.openTocEntryId
const openTocLineIndex = tabStore.activeTab.openTocLineIndex
const searchHighlightLineIndex = tabStore.activeTab.searchHighlightLineIndex
const searchHighlightQuery = tabStore.activeTab.searchHighlightQuery ?? ''
const searchHighlightSnippet = tabStore.activeTab.searchHighlightSnippet
const searchHighlightTerms = tabStore.activeTab.searchHighlightTerms
if (openTocEntryId != null)
  tabStore.updateActiveTab({
    openTocEntryId: undefined,
    openTocLineIndex: undefined,
    searchHighlightLineIndex: undefined,
    searchHighlightQuery: undefined,
    searchHighlightSnippet: undefined,
    searchHighlightTerms: undefined,
  })

const bottomVisible = ref(false)
const searchVisible = ref(false)
const tocVisible = ref(false)
const toolbarRef = ref<InstanceType<typeof BookViewToolbar> | null>(null)
const selectedLineId = ref<number | null>(null)
const commentaryLineId = ref<number | null>(null)
const hiddenCommentaryBookIds = ref(new Set<number>())
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
  tocSearchTree,
  loading: tocLoading,
  error: tocError,
} = useToc(
  () => bookId,
  () => bookTitle,
)
// Delay lines loading until TOC is ready — ensures getActiveTocEntry works correctly
// on the first scroll event and avoids the flash-to-entry-1 race on session restore.
const { lines, prioritise, hasCommentaries } = useLines(() =>
  tocEntries.value.length > 0 ? bookId : undefined,
)

// When selected line is a toc entry line, collect all line IDs in that section
const selectedSectionLineIds = computed<number[] | null>(() => {
  if (commentaryLineId.value == null || !tocEntries.value.length || !lines.value.length) return null
  const tocEntry = tocEntries.value.find((e) => e.lineId === commentaryLineId.value)
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
  () => commentaryLineId.value,
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
    if (entry != null) {
      activeTocEntryId.value = entry.id
      tabStore.updateActiveTab({ tocPath: getTocPath(entry) })
    }
    stop()
  })
}

let tocScrolling = false
let tocScrollTargetLineIndex: number | null = null
let tocScrollTimer: ReturnType<typeof setTimeout> | null = null

const currentScrollLineIndex = ref(0)
const currentFullLineIndex = ref(0)

let autoSelectCommentaryTimer: ReturnType<typeof setTimeout> | null = null

function onLinesScrolled(lineIndex: number, fullLineIndex: number) {
  currentScrollLineIndex.value = lineIndex
  currentFullLineIndex.value = fullLineIndex
  if (tocScrolling) {
    const reached =
      tocScrollTargetLineIndex == null
        ? false
        : tocScrollTargetLineIndex === 0
          ? lineIndex === 0
          : lineIndex >= tocScrollTargetLineIndex
    if (reached) {
      tocScrolling = false
      tocScrollTargetLineIndex = null
      if (tocScrollTimer) {
        clearTimeout(tocScrollTimer)
        tocScrollTimer = null
      }
    }
    // activeTocEntryId was already set in onTocSelect — don't overwrite during programmatic scroll
    return
  }
  const entry = getActiveTocEntry(lineIndex)
  if (entry && entry.id !== activeTocEntryId.value) {
    activeTocEntryId.value = entry.id
    tabStore.updateActiveTab({ tocPath: getTocPath(entry) })
  }
  if (!autoSelectTopLine.value) return
  const line = lines.value.find((l) => l.lineIndex === currentFullLineIndex.value)
  if (line && line.id > 0) {
    selectedLineId.value = line.id
    bottomVisible.value = true
    if (autoSelectCommentaryTimer) clearTimeout(autoSelectCommentaryTimer)
    autoSelectCommentaryTimer = setTimeout(() => {
      commentaryLineId.value = line.id
    }, 120)
  }
}

watch(autoSelectTopLine, (enabled) => {
  if (!enabled && autoSelectCommentaryTimer) {
    clearTimeout(autoSelectCommentaryTimer)
    autoSelectCommentaryTimer = null
  }
})

function onTocSelect(entry: TocEntry) {
  if (entry.lineId == null) return
  activeTocEntryId.value = entry.id
  tabStore.updateActiveTab({ tocPath: getTocPath(entry) })
  tocScrolling = true
  tocScrollTargetLineIndex = entry.lineIndex ?? null
  if (tocScrollTimer) clearTimeout(tocScrollTimer)
  tocScrollTimer = setTimeout(() => {
    tocScrolling = false
    tocScrollTargetLineIndex = null
    tocScrollTimer = null
  }, 300)
  linesContentRef.value?.scrollToLineId(entry.lineId, entry.lineIndex ?? undefined)
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
  if (searchVisible.value && searchMode.value === 'content') {
    searchVisible.value = false
    nextTick(() => linesContentRef.value?.focusScroller())
    return
  }
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

function onLineSelected(lineId: number) {
  selectedLineId.value = lineId
  commentaryLineId.value = lineId
}

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
    commentaryLineId.value = entry.lineId
    bottomVisible.value = true
    linesContentRef.value?.scrollToLineId(entry.lineId)
    const stop = watch(
      commentaryLoading,
      (loading) => {
        if (loading) return
        if (commentaryLineId.value !== entry.lineId) return
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
  commentaryLineId.value = result.id
  bottomVisible.value = true
  linesContentRef.value?.scrollToLineId(result.id)
  const stop = watch(
    commentaryLoading,
    (loading) => {
      if (loading) return
      if (commentaryLineId.value !== result.id) return
      pendingNavStop = null
      stop()
      nextTick(() => commentaryViewRef.value?.scrollToGroup(commentaryBookId))
    },
    { flush: 'sync' },
  )
  pendingNavStop = stop
}

onMounted(async () => {
  if (bookId != null) {
    const [bookSaved, lastRead] = await Promise.all([
      tabStore.getBookViewState(tabId, bookId),
      tabStore.getLastReadPos(bookId),
    ])
    const restoredLineId = bookSaved?.selectedLineId ?? lastRead?.selectedLineId
    const si = bookSaved?.commentaryScrollIndex ?? lastRead?.commentaryScrollIndex
    const so = bookSaved?.commentaryScrollOffset ?? lastRead?.commentaryScrollOffset
    // restore zoom for this tab+book
    if (bookSaved?.zoom != null) bookViewStore.setZoom(tabId, bookId, bookSaved.zoom)
    // restore bottom panel visibility
    if (bookSaved?.bottomVisible != null) bottomVisible.value = bookSaved.bottomVisible
    // restore per-book auto-sync commentary (fall back to global default)
    if (bookSaved?.autoSelectTopLine != null) {
      bookViewStore.autoSelectTopLine = bookSaved.autoSelectTopLine
    }
    // restore commentary filter — BookState takes priority; fall back to lastRead if resumeLastRead is on
    const savedFilter =
      bookSaved?.hiddenCommentaryBookIds ??
      (settingsStore.resumeLastRead ? lastRead?.hiddenCommentaryBookIds : undefined)
    if (savedFilter?.length) hiddenCommentaryBookIds.value = new Set(savedFilter)
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
      commentaryLineId.value = restoredLineId
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

// Fetch all default commentators for this book ordered by position (used as fallback pin when no user selection exists)
let defaultCommentatorBookIds: number[] = []
if (bookId != null) {
  query<{ commentatorBookId: number }>(SQL.GET_DEFAULT_COMMENTATORS, [bookId]).then((rows) => {
    defaultCommentatorBookIds = rows.map((r) => r.commentatorBookId)
  })
}

function onCommentaryScroll(si: number, so: number) {
  commentaryScrollIndex.value = si
  commentaryScrollOffset.value = so
}

watch(commentaryLineId, () => {
  if (commentaryViewRef.value?.activeBookId) {
    pinnedCommentaryBookId.value = commentaryViewRef.value.activeBookId
  } else if (defaultCommentatorBookIds.length > 0) {
    pinnedCommentaryBookId.value = defaultCommentatorBookIds[0]!
  }
})

// When groups load, if the pinned default has no links for this line, fall back to the next default that does
watch(groups, (newGroups) => {
  if (!newGroups.length || !defaultCommentatorBookIds.length) return
  // Only apply fallback when the pin came from defaults (not a user selection)
  const currentPin = pinnedCommentaryBookId.value
  if (currentPin == null || !defaultCommentatorBookIds.includes(currentPin)) return
  const available = defaultCommentatorBookIds.find((id) => newGroups.some((g) => g.bookId === id))
  if (available != null) pinnedCommentaryBookId.value = available
})
watch(
  () => bookViewStore.toggleBottomPanelSignal,
  () => {
    bottomVisible.value = !bottomVisible.value
  },
)
// If the bottom panel was restored open but the book has no commentaries, close it.
// hasCommentaries resolves asynchronously after the book data loads, so we can't
// check it at restore time — instead we watch for it to settle to false.
watch(hasCommentaries, (has) => {
  if (!has) bottomVisible.value = false
})
watch(searchVisible, (v) => {
  if (!v) {
    contentSearch.clear()
    commentarySearch.clear()
  }
})
</script>

<template>
  <div class="book-view">
    <!-- Top toolbar -->
    <BookViewToolbar
      v-if="bookViewStore.toolbarVisible && toolbarPosition === 'top'"
      ref="toolbarRef"
      :bottom-visible="bottomVisible"
      :search-visible="searchVisible"
      :toc-visible="tocVisible"
      :has-commentaries="hasCommentaries"
      @toggle-bottom="bottomVisible = !bottomVisible"
      @toggle-search="searchVisible = !searchVisible"
      @toggle-toc="tocVisible = !tocVisible"
    />
    <!-- Middle row: right toolbar + content + left toolbar (RTL: first child = physical right) -->
    <div class="body-row">
      <BookViewToolbar
        v-if="bookViewStore.toolbarVisible && toolbarPosition === 'right'"
        ref="toolbarRef"
        :bottom-visible="bottomVisible"
        :search-visible="searchVisible"
        :toc-visible="tocVisible"
        :has-commentaries="hasCommentaries"
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
              :search-highlight-snippet="searchHighlightSnippet"
              :search-highlight-terms="searchHighlightTerms"
              :search-bar-visible="searchVisible"
              :commentary-scroll-index="commentaryScrollIndex"
              :commentary-scroll-offset="commentaryScrollOffset"
              :hidden-commentary-book-ids="hiddenCommentaryBookIds"
              :search-query="searchMode === 'content' ? contentSearch.query.value : ''"
              :current-match-line-index="
                searchMode === 'content' ? contentSearch.currentMatchLineIndex.value : undefined
              "
              :current-match-occurrence="
                searchMode === 'content' ? contentSearch.currentMatchOccurrence.value : undefined
              "
              @scrolled="onLinesScrolled"
              @line-selected="onLineSelected"
              @ctrl-f="openContentSearch"
            />
          </template>
          <template #bottom>
            <CommentaryView
              ref="commentaryViewRef"
              :selected-line-id="selectedLineId"
              :groups="groups"
              :loading="commentaryLoading"
              :hidden-book-ids="hiddenCommentaryBookIds"
              :pinned-book-id="pinnedCommentaryBookId"
              :search-query="searchMode === 'commentary' ? commentarySearch.query.value : ''"
              :current-match-flat-index="
                searchMode === 'commentary'
                  ? commentarySearch.currentMatchFlatIndex.value
                  : undefined
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
              @update:hidden-book-ids="hiddenCommentaryBookIds = $event"
            />
          </template>
        </BookViewSplitPane>
        <BookViewSearchBar
          ref="searchBarRef"
          :visible="searchVisible"
          :toolbar-visible="bookViewStore.toolbarVisible"
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
          :book-id="bookId"
          :book-title="bookTitle"
          :active-toc-entry-id="activeTocEntryId"
          :visible="tocVisible"
          :toc-entries="tocEntries"
          :toc-search-tree="tocSearchTree"
          :alt-toc-sections="altTocSections"
          :loading="tocLoading"
          :error="tocError"
          :toggle-button-el="toolbarRef?.tocBtnRef ?? null"
          @close="tocVisible = false"
          @select="onTocSelect"
          @alt-select="onAltTocSelect"
        />
      </div>
      <BookViewToolbar
        v-if="bookViewStore.toolbarVisible && toolbarPosition === 'left'"
        ref="toolbarRef"
        :bottom-visible="bottomVisible"
        :search-visible="searchVisible"
        :toc-visible="tocVisible"
        :has-commentaries="hasCommentaries"
        @toggle-bottom="bottomVisible = !bottomVisible"
        @toggle-search="searchVisible = !searchVisible"
        @toggle-toc="tocVisible = !tocVisible"
      />
    </div>
    <!-- Bottom toolbar -->
    <BookViewToolbar
      v-if="bookViewStore.toolbarVisible && toolbarPosition === 'bottom'"
      ref="toolbarRef"
      :bottom-visible="bottomVisible"
      :search-visible="searchVisible"
      :toc-visible="tocVisible"
      :has-commentaries="hasCommentaries"
      @toggle-bottom="bottomVisible = !bottomVisible"
      @toggle-search="searchVisible = !searchVisible"
      @toggle-toc="tocVisible = !tocVisible"
    />
  </div>
</template>

<style scoped>
.book-view {
  display: flex;
  flex-direction: column;
  height: 100%;
  background: var(--bg-primary);
}
.body-row {
  display: flex;
  flex-direction: row;
  flex: 1;
  min-height: 0;
}
.content-area {
  position: relative;
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}
</style>
