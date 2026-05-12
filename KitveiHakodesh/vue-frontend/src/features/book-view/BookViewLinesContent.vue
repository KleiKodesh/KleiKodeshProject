<script setup lang="ts">
import { computed, ref, watch, nextTick, onBeforeUnmount } from 'vue'
import { useResizeObserver } from '@vueuse/core'
import { storeToRefs } from 'pinia'
import { useVirtualizer } from '@tanstack/vue-virtual'
import { useTabStore } from '@/stores/tabStore'
import { useSettingsStore } from '@/stores/settingsStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import type { LineItem } from './useBookViewLinesTable'
import type { TocEntry } from './useBookViewToc'
import type { CommentaryTreeState, CommentaryVisibilityItem } from './bookViewTypes'
import ContextMenu from '@/components/ContextMenu.vue'
import { useEventListener } from '@vueuse/core'
import { useScopedKeys } from '@/composables/useTextSelectionKeys'
import { useScopedCopy } from '@/composables/useLineCopy'
import { scrollToIndexWithRetry } from '@/utils/scrollToIndexWithRetry'
import { useVirtualScrollerKeys } from '@/composables/useVirtualScrollerKeys'
import { useBookViewLineRenderer } from './useBookViewLineRenderer'
import { useBookViewLineCopyMenu } from './useBookViewLineCopyMenu'

const emit = defineEmits<{ scrolled: [number, number]; lineSelected: [number]; 'ctrl-f': [] }>()
const props = defineProps<{
  lines: LineItem[]
  prioritise: (lineIndex: number) => void
  altTocLabelMap?: Map<number, string>
  selectedLineId?: number | null
  bottomVisible?: boolean
  commentaryScrollIndex?: number | null
  commentaryScrollOffset?: number | null
  hiddenCommentaryBookIds?: Set<string>
  commentaryFilterState?: CommentaryTreeState
  searchQuery?: string
  currentMatchLineIndex?: number
  currentMatchOccurrence?: number
  // TOC / search-result navigation: scroll to this line index on first load
  initialLineIndex?: number
  // Session restore: scroll to this line index on first load (from persisted state)
  initialScrollIndex?: number
  initialScrollOffset?: number
  searchHighlightLineIndex?: number
  searchHighlightQuery?: string
  searchHighlightSnippet?: string
  searchHighlightTerms?: string[]
  searchBarVisible?: boolean
  getActiveTocEntry?: (lineIndex: number) => TocEntry | null
  getTocPath?: (entry: TocEntry) => string
}>()

const tabStore = useTabStore()
const settingsStore = useSettingsStore()
const bookViewStore = useBookViewStore()
const { autoSelectTopLine } = storeToRefs(bookViewStore)
const tabId = tabStore.activeTabId
const bookId = tabStore.activeTab.bookId!
const bookTitle = tabStore.activeTab.title

// Read zoom directly by tabId+bookId — NOT via bookViewStore.zoom computed which is
// gated on activeTab. If this tab is not active when savePos fires (e.g. user switched
// tabs before closing), the activeTab-based computed returns DEFAULT and overwrites the
// real zoom in IDB.
const zoom = computed({
  get: () => bookViewStore.getZoom(tabId, bookId),
  set: (v: number) => bookViewStore.setZoom(tabId, bookId, v),
})

const diacriticsState = computed(() => settingsStore.diacriticsState)
const fontPx = computed(() => (zoom.value / 100) * (settingsStore.fontSize / 100) * 15)

// ── Line rendering ────────────────────────────────────────────────────────────

const { lineContent } = useBookViewLineRenderer(settingsStore, diacriticsState, () => ({
  searchQuery: props.searchQuery,
  currentMatchLineIndex: props.currentMatchLineIndex,
  currentMatchOccurrence: props.currentMatchOccurrence,
  searchHighlightLineIndex: props.searchHighlightLineIndex,
  searchHighlightQuery: props.searchHighlightQuery,
  searchHighlightSnippet: props.searchHighlightSnippet,
  searchHighlightTerms: props.searchHighlightTerms,
}))

// ── Scroller setup ────────────────────────────────────────────────────────────

const scrollerEl = ref<HTMLElement | null>(null)

const { isSelectAll, selectAllInContainer } = useScopedKeys(scrollerEl, {
  onCtrlF: () => emit('ctrl-f'),
})
useScopedCopy(
  scrollerEl,
  () => props.lines.map((l) => l.content).filter(Boolean) as string[],
  isSelectAll,
)
useVirtualScrollerKeys(
  scrollerEl,
  () =>
    virtualizer.value as unknown as import('@tanstack/vue-virtual').Virtualizer<Element, Element>,
  () => props.lines.length,
)

// ── Context menu ──────────────────────────────────────────────────────────────

const contextMenuRef = ref<InstanceType<typeof ContextMenu> | null>(null)
const contextMenuItems = useBookViewLineCopyMenu({
  scrollerEl,
  lines: () => props.lines,
  isSelectAll,
  selectAllInContainer,
  bookTitle,
  tabStore,
  getActiveTocEntry: props.getActiveTocEntry,
  getTocPath: props.getTocPath,
})

// ── Virtualizer ───────────────────────────────────────────────────────────────

const virtualizer = useVirtualizer(
  computed(() => ({
    count: props.lines.length,
    getScrollElement: () => scrollerEl.value,
    estimateSize: () => 32,
    overscan: 10,
  })),
)

const virtualItems = computed(() => virtualizer.value.getVirtualItems())
const totalSize = computed(() => virtualizer.value.getTotalSize())

// ── Resize anchor ─────────────────────────────────────────────────────────────
// When the container is resized (window resize, split pane drag), lines reflow
// and the virtualizer's pixel positions become stale. We anchor to the middle
// visible line so the user's reading position stays on screen after reflow.
// The middle line is a better anchor than the first: when the window narrows and
// lines grow taller, the first line would be pushed to the top leaving the user's
// actual reading position off screen, whereas the middle line stays centered.
{
  let resizeAnchorIndex: number | null = null

  useResizeObserver(scrollerEl, () => {
    if (programmaticScrolling) return
    const items = virtualItems.value
    if (!items.length) return

    if (resizeAnchorIndex === null) {
      // Capture the middle visible line before the layout changes
      const middleItem = items[Math.floor(items.length / 2)]
      resizeAnchorIndex = middleItem?.index ?? items[0]!.index
    }

    const anchorIndex = resizeAnchorIndex
    resizeAnchorIndex = null

    virtualizer.value!.measure()
    nextTick(() => {
      setProgrammaticScroll()
      virtualizer.value!.scrollToIndex(anchorIndex, { align: 'center' })
    })
  })
}

// ── Scroll capture ────────────────────────────────────────────────────────────

function captureScrollPos() {
  const first = virtualItems.value[0]
  if (!first || !scrollerEl.value) return null
  return {
    scrollIndex: first.index,
    scrollOffset: Math.max(0, scrollerEl.value.scrollTop - first.start),
  }
}

// ── Scroll restore ────────────────────────────────────────────────────────────

let programmaticScrollTimer: ReturnType<typeof setTimeout> | null = null
let programmaticScrolling = false

function restoreScrollPos(lineIndex: number, scrollOffset = 0) {
  programmaticScrolling = true
  if (programmaticScrollTimer) clearTimeout(programmaticScrollTimer)
  virtualizer.value.scrollToIndex(lineIndex, { align: 'start' })
  requestAnimationFrame(() => {
    const item = virtualizer.value.measurementsCache.find((m) => m.index === lineIndex)
    if (item && scrollerEl.value) scrollerEl.value.scrollTop = item.start + scrollOffset
    requestAnimationFrame(() => { programmaticScrolling = false })
  })
}

// ── Initial scroll on load ────────────────────────────────────────────────────
// Watches lines and initialScrollIndex together. Waits for:
//   1. lines to be non-empty (placeholders allocated)
//   2. a target index to be known (either initialLineIndex from TOC nav, or
//      initialScrollIndex from IDB session restore — which may arrive after mount)
//   3. the target line's content to be loaded (not a placeholder)
// Stops itself after the first successful restore.
{
  let restored = false
  let stopContentWatch: (() => void) | null = null
  let stop: (() => void) | null = null

  stop = watch(
    () => [props.lines, props.initialScrollIndex] as const,
    ([val]) => {
      if (!val.length) return
      const targetIndex = props.initialLineIndex ?? props.initialScrollIndex
      if (targetIndex == null) return
      stop?.()
      stop = null
      props.prioritise(targetIndex)
      stopContentWatch = watch(
        () => props.lines[targetIndex]?.content,
        (content) => {
          if (content == null) return
          if (restored) { stopContentWatch?.(); return }
          restored = true
          stopContentWatch?.()
          nextTick(() => {
            const offset = props.initialScrollIndex != null ? (props.initialScrollOffset ?? 0) : 0
            restoreScrollPos(targetIndex, offset)
            requestAnimationFrame(() =>
              requestAnimationFrame(() => {
                const scrollTop = scrollerEl.value?.scrollTop ?? 0
                const items = virtualizer.value.getVirtualItems()
                const firstVisible = items.find((v) => v.start + v.size > scrollTop) ?? items[0]
                const firstFull = items.find((v) => v.start >= scrollTop) ?? firstVisible
                emit('scrolled', firstVisible?.index ?? targetIndex, firstFull?.index ?? firstVisible?.index ?? targetIndex)
              }),
            )
            if (props.searchHighlightLineIndex != null && scrollerEl.value) {
              nextTick(() => {
                const mark = scrollerEl.value!.querySelector('mark.search-match') as HTMLElement | null
                mark?.scrollIntoView({ block: 'center' })
              })
            }
            scrollerEl.value?.focus({ preventScroll: true })
          })
        },
        { immediate: true, flush: 'post' },
      )
    },
    { flush: 'post', immediate: true },
  )

  // If no target ever arrives (no saved position, no TOC nav), focus the scroller
  // once lines are loaded so keyboard navigation works immediately.
  watch(
    () => props.lines,
    (val) => {
      if (!val.length || restored) return
      if (props.initialLineIndex == null && props.initialScrollIndex == null) {
        stop?.()
        stop = null
        nextTick(() => scrollerEl.value?.focus({ preventScroll: true }))
      }
    },
    { flush: 'post' },
  )
}

// ── Persist scroll position ───────────────────────────────────────────────────

// Last known good position — updated on every scroll so unmount always has fresh data
// even if the DOM is already detached when onBeforeUnmount fires (WebView2 behaviour).
let lastKnownPos: { scrollIndex: number; scrollOffset: number } | null = null

function savePos() {
  if (programmaticScrolling) return
  const pos = lastKnownPos ?? captureScrollPos()
  if (pos) {
    // Serialize the reactive proxy to a plain object before writing to IDB.
    // IDB's structured clone algorithm cannot serialize Vue reactive proxies.
    const filterState = props.commentaryFilterState
      ? {
          searchQuery: props.commentaryFilterState.searchQuery,
          tokens: [...props.commentaryFilterState.tokens],
          visibilityList: props.commentaryFilterState.visibilityList.map(
            (item: CommentaryVisibilityItem) => ({ ...item }),
          ),
        }
      : undefined
    tabStore.setBookViewState(tabId, bookId, {
      ...pos,
      selectedLineId: props.selectedLineId,
      commentaryScrollIndex: props.commentaryScrollIndex,
      commentaryScrollOffset: props.commentaryScrollOffset,
      commentaryFilterState: filterState,
      zoom: zoom.value,
      bottomVisible: props.bottomVisible,
      autoSelectTopLine: autoSelectTopLine.value,
    })
    tabStore.setLastReadPos(bookId, {
      ...pos,
      selectedLineId: props.selectedLineId,
      commentaryScrollIndex: props.commentaryScrollIndex,
      commentaryScrollOffset: props.commentaryScrollOffset,
      commentaryFilterState: filterState,
    })
  }
}

// Save when the commentary panel closes so the commentary scroll position
// (which just arrived via prop update from onCommentaryScroll) is flushed to
// IDB before CommentaryView unmounts and the position would otherwise be lost.
watch(
  () => props.bottomVisible,
  (visible) => { if (!visible) savePos() },
)

useEventListener(document, 'visibilitychange', () => {
  if (document.visibilityState === 'hidden') savePos()
})
useEventListener(window, 'beforeunload', savePos)
onBeforeUnmount(() => {
  // Force-clear the programmatic flag so savePos is never silently skipped at unmount.
  programmaticScrolling = false
  if (programmaticScrollTimer) {
    clearTimeout(programmaticScrollTimer)
    programmaticScrollTimer = null
  }
  savePos()
})

function onScroll() {
  if (!scrollerEl.value || programmaticScrolling) return
  const scrollTop = scrollerEl.value.scrollTop
  const items = virtualizer.value.getVirtualItems()
  // For scroll position tracking (TOC, persistence): first item with any part visible
  const firstVisible = items.find((v) => v.start + v.size > scrollTop) ?? items[0]
  const lineIndex = firstVisible?.index ?? 0
  // For auto-select: first fully visible line (top edge at or below scrollTop)
  const firstFull = items.find((v) => v.start >= scrollTop) ?? firstVisible
  const fullLineIndex = firstFull?.index ?? lineIndex
  lastKnownPos = captureScrollPos()
  props.prioritise(lineIndex)
  emit('scrolled', lineIndex, fullLineIndex)
}

// ── Programmatic navigation ───────────────────────────────────────────────────

function setProgrammaticScroll() {
  programmaticScrolling = true
  if (programmaticScrollTimer) clearTimeout(programmaticScrollTimer)
  programmaticScrollTimer = setTimeout(() => { programmaticScrolling = false }, 300)
}

function scrollToLineId(lineId: number, fallbackLineIndex?: number) {
  const lineIndex = props.lines.find((l) => l.id === lineId)?.lineIndex ?? fallbackLineIndex
  if (lineIndex == null) return
  props.prioritise(lineIndex)
  const scroller = scrollerEl.value
  const vItem = virtualItems.value.find((v) => v.index === lineIndex)
  if (vItem && scroller) {
    const viewTop = scroller.scrollTop
    const viewBottom = viewTop + scroller.clientHeight
    if (vItem.start >= viewTop && vItem.start + vItem.size <= viewBottom) return
  }
  setProgrammaticScroll()
  virtualizer.value.scrollToIndex(lineIndex, { align: 'start' })
}

function scrollToLineIndex(lineIndex: number) {
  if (!scrollerEl.value) return
  setProgrammaticScroll()
  scrollToIndexWithRetry(
    virtualizer.value as unknown as import('@tanstack/vue-virtual').Virtualizer<Element, Element>,
    scrollerEl.value,
    lineIndex,
    props.searchBarVisible ? 44 : 0,
  )
}

function onLineClick(index: number) {
  const line = props.lines[index]
  if (props.bottomVisible && line) emit('lineSelected', line.id)
}

function focusScroller() {
  scrollerEl.value?.focus({ preventScroll: true })
}

defineExpose({ scrollToLineId, scrollToLineIndex, focusScroller })
</script>

<template>
  <div class="lines-content">
    <ContextMenu ref="contextMenuRef" :items="contextMenuItems" />
    <div
      ref="scrollerEl"
      class="scroller"
      tabindex="0"
      data-ctrlf-enabled
      :style="{ fontSize: `${fontPx}px` }"
      @scroll="onScroll"
      @contextmenu="contextMenuRef?.show($event)"
    >
      <div :style="{ height: `${totalSize}px`, position: 'relative' }">
        <div
          v-for="vItem in virtualItems"
          :key="String(vItem.key)"
          :ref="(el) => el && virtualizer.measureElement(el as Element)"
          :data-index="vItem.index"
          :style="{
            position: 'absolute',
            top: 0,
            right: 0,
            left: 0,
            transform: `translateY(${vItem.start}px)`,
          }"
        >
          <div
            v-if="lines[vItem.index]?.content != null"
            class="line"
            :class="{ selected: props.bottomVisible && selectedLineId === lines[vItem.index]?.id }"
            :data-alt-toc="props.altTocLabelMap?.get(vItem.index)"
            v-html="lineContent(lines[vItem.index]!.content!, vItem.index)"
            @click="onLineClick(vItem.index)"
          />
          <div v-else class="line placeholder" />
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.lines-content {
  height: 100%;
  position: relative;
}
.scroller {
  height: 100%;
  overflow-y: auto;
}
.line {
  padding-inline: 12px;
  font-family: var(--text-font);
  font-size: var(--font-size, 100%);
  line-height: var(--line-height, 1.7);
  color: var(--text-primary);
  text-align: justify;
  position: relative;
}
.line.placeholder {
  height: 28px;
  margin-inline: 12px;
  margin-block: 4px;
  border-radius: 4px;
  background: color-mix(in srgb, var(--text-primary) 5%, transparent);
}
.line::after {
  content: '';
  position: absolute;
  top: 0;
  bottom: 0;
  right: 4px;
  width: 3px;
  background: var(--accent-color);
  opacity: 0;
  transition: opacity 150ms ease;
}
.line.selected::after {
  opacity: 1;
}
.line[data-alt-toc]::before {
  content: attr(data-alt-toc);
  display: block;
  font-size: 0.85rem;
  font-weight: 600;
  opacity: 0.35;
  padding-block-end: 2px;
}
.line :deep(h1),
.line :deep(h2),
.line :deep(h3),
.line :deep(h4),
.line :deep(h5),
.line :deep(h6) {
  font-family: var(--header-font);
}
.line :deep(mark.search-match) {
  background: rgba(255, 165, 0, 0.4);
  color: inherit;
  border-radius: 2px;
}
.line :deep(mark.search-match.current) {
  background: rgba(255, 165, 0, 0.9);
  color: #000;
}
</style>
