<script setup lang="ts">
import { computed, ref, watch, nextTick } from 'vue'
import { storeToRefs } from 'pinia'
import { useVirtualizer } from '@tanstack/vue-virtual'
import { useTabStore } from '@/stores/tabStore'
import { useSettingsStore } from '@/stores/settingsStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import type { LineItem } from './useBookViewLinesTable'
import type { TocEntry } from '../toc/useBookViewToc'
import type { CommentaryTreeState, PinnedCommentaryGroup } from '../bookViewTypes'
import ContextMenu from '@/components/ContextMenu.vue'
import { onLongPress } from '@vueuse/core'
import { useScopedKeys } from '@/composables/useTextSelectionKeys'
import { useScopedCopy, triggerCopy } from '@/composables/useLineCopy'
import { useVirtualScrollerKeys } from '@/composables/useVirtualScrollerKeys'
import { useZoomHandler } from '@/composables/useZoom'
import { useBookViewLineRenderer, setCurrentMark } from './useBookViewLineRenderer'
import { useBookViewLineCopyMenu } from './useBookViewLineCopyMenu'
import { useBookViewAnnotations } from './useBookViewAnnotations'
import { useBookViewLinesScroll } from './useBookViewLinesScroll'
import { useBookViewLinesNavigation } from './useBookViewLinesNavigation'
import BookViewNoteBubble from './BookViewNoteBubble.vue'
import { pasteIntoWord } from '@/webview-host/bridge'

const emit = defineEmits<{
  scrolled: [number, number]
  lineSelected: [lineId: number, isShiftClick: boolean]
  'ctrl-f': []
}>()
const props = defineProps<{
  lines: LineItem[]
  prioritise: (lineIndex: number) => void
  altTocLabelMap?: Map<number, string>
  selectedLineId?: number | null
  commentaryVisible?: boolean
  commentaryMode?: 'off' | 'bottom' | 'side'
  commentaryFraction?: number
  stackedCommentaryFraction?: number
  commentaryScrollIndex?: number | null
  commentaryScrollOffset?: number | null
  commentaryFilterState?: CommentaryTreeState
  searchQuery?: string
  currentMatchLineIndex?: number
  currentMatchOccurrence?: number
  initialLineIndex?: number
  initialScrollIndex?: number
  initialScrollOffset?: number
  searchHighlightLineIndex?: number
  searchHighlightQuery?: string
  searchHighlightSnippet?: string
  searchHighlightTerms?: string[]
  searchBarVisible?: boolean
  idbResolved?: boolean
  getActiveTocEntry?: (lineIndex: number) => TocEntry | null
  getTocPath?: (entry: TocEntry) => string
  pinnedCommentaryGroup?: PinnedCommentaryGroup | null
  selectedSectionLineIds?: number[] | null
  multiSelectLineIds?: number[] | null
}>()

const tabStore = useTabStore()
const settingsStore = useSettingsStore()
const bookViewStore = useBookViewStore()
const paneNavigation = usePaneNavigation()
const { autoSelectTopLine } = storeToRefs(bookViewStore)
const tabId = paneNavigation.activeTabId
const bookId = paneNavigation.activeTab.bookId!
const bookTitle = paneNavigation.activeTab.title

// Read lines zoom directly by tabId+bookId — NOT via bookViewStore.zoom computed (gated on
// activeTab). If this tab is not active when savePos fires, the computed returns DEFAULT.
const zoom = computed({
  get: () => bookViewStore.getLinesZoom(tabId, bookId),
  set: (value: number) => bookViewStore.setLinesZoom(tabId, bookId, value),
})

const diacriticsState = computed(() => settingsStore.diacriticsState)
const fontPx = computed(() => (zoom.value / 100) * (settingsStore.fontSize / 100) * 15)

const scrollerEl = ref<HTMLElement | null>(null)
useZoomHandler({ zoom, target: scrollerEl, keyboard: true })
const { isSelectAll, selectAllInContainer } = useScopedKeys(scrollerEl, {
  onCtrlF: () => emit('ctrl-f'),
  onCtrlV: () => triggerCopy(() => pasteIntoWord().catch(() => {})),
  onCtrlShiftC: () => onSearchInRepository(),
})

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

// Type alias for the concrete Virtualizer type — avoids repeating the double-cast
// every time a composable that expects the concrete type receives virtualizer.value.
type ConcreteVirtualizer = import('@tanstack/vue-virtual').Virtualizer<Element, Element>
const getVirtualizer = () => virtualizer.value as unknown as ConcreteVirtualizer

useVirtualScrollerKeys(
  scrollerEl,
  getVirtualizer,
  () => props.lines.length,
)

const {
  getHighlightsForLine,
  getNotesForLine,
  updateNote,
  deleteNote,
  activeBubbleNote,
  activeBubbleAnchorRect,
  closeNoteBubble,
  onHighlight,
  onClearHighlight,
  onAddNote,
  onMarkerClick,
} = useBookViewAnnotations(
  bookId,
  scrollerEl,
  () => props.lines,
  () => virtualItems.value.map((v) => props.lines[v.index]?.id ?? 0).filter((id) => id > 0),
)

const { setProgrammaticScroll, onScroll } = useBookViewLinesScroll(
  scrollerEl,
  getVirtualizer,
  () => virtualItems.value,
  () => props.lines,
  props,
  { tabStore, bookViewStore, autoSelectTopLine, zoom, tabId, bookId },
  (event, firstVisible, firstFull) => emit(event, firstVisible, firstFull),
  props.prioritise,
)

const { lineContent } = useBookViewLineRenderer(settingsStore, diacriticsState, () => ({
  searchQuery: props.searchQuery,
  searchHighlightLineIndex: props.searchHighlightLineIndex,
  searchHighlightQuery: props.searchHighlightQuery,
  searchHighlightSnippet: props.searchHighlightSnippet,
  searchHighlightTerms: props.searchHighlightTerms,
  getHighlightsForLine,
  getNotesForLine,
}))

// Apply .current class via DOM toggle — no re-render needed when only occurrence changes.
watch(
  () => [props.currentMatchLineIndex, props.currentMatchOccurrence] as const,
  ([lineIndex, occurrence]) => {
    if (!scrollerEl.value) return
    nextTick(() => {
      if (!scrollerEl.value) return
      setCurrentMark(scrollerEl.value, lineIndex ?? -1, occurrence ?? 0)
    })
  },
)

const contextMenuRef = ref<InstanceType<typeof ContextMenu> | null>(null)
const { items: contextMenuItems, buildFormattedHtml, onPasteIntoWord, onSearchInRepository } = useBookViewLineCopyMenu({
  scrollerEl,
  lines: () => props.lines,
  isSelectAll,
  selectAllInContainer,
  bookTitle,
  tabStore,
  paneNavigation,
  getActiveTocEntry: props.getActiveTocEntry,
  getTocPath: props.getTocPath,
  getNotesForLine,
  getRenderedLineContent: lineContent,
  onHighlight,
  onClearHighlight,
  onAddNote,
})
useScopedCopy(
  scrollerEl,
  () => props.lines.map((line) => line.content).filter(Boolean) as string[],
  isSelectAll,
  buildFormattedHtml,
)
onLongPress(scrollerEl, (event) => {
  if (!scrollerEl.value) return
  // In RTL layout the scrollbar is on the physical LEFT side of the container.
  // clientWidth excludes the scrollbar track, so the scrollbar occupies the gap
  // between the element's left edge and (left + offsetWidth - clientWidth).
  // Any pointer position to the left of (rect.left + scrollbarWidth) hit the scrollbar.
  const el = scrollerEl.value
  const rect = el.getBoundingClientRect()
  const scrollbarWidth = el.offsetWidth - el.clientWidth
  if (event.clientX <= rect.left + scrollbarWidth) return
  contextMenuRef.value?.showAtPosition(event.clientX, event.clientY)
})

const { scrollToLineId, scrollToLineIndex } = useBookViewLinesNavigation(
  scrollerEl,
  getVirtualizer,
  () => virtualItems.value,
  () => props.lines,
  () => props.searchBarVisible ?? false,
  setProgrammaticScroll,
  props.prioritise,
)

function onLineClick(index: number, event: MouseEvent) {
  const line = props.lines[index]
  if (props.commentaryVisible && line) emit('lineSelected', line.id, event.ctrlKey || event.metaKey)
}

const selectedSectionLineIdSet = computed(() =>
  props.selectedSectionLineIds ? new Set(props.selectedSectionLineIds) : null,
)

const multiSelectLineIdSet = computed(() =>
  props.multiSelectLineIds ? new Set(props.multiSelectLineIds) : null,
)

function isInActiveSection(lineIndex: number): boolean {
  const set = selectedSectionLineIdSet.value
  if (!set) return false
  const line = props.lines[lineIndex]
  if (!line) return false
  return set.has(line.id)
}

function isMultiSelected(lineIndex: number): boolean {
  const set = multiSelectLineIdSet.value
  if (!set) return false
  const line = props.lines[lineIndex]
  if (!line) return false
  return set.has(line.id)
}

function focusScroller() {
  scrollerEl.value?.focus({ preventScroll: true })
}

defineExpose({ scrollToLineId, scrollToLineIndex, focusScroller })
</script>

<template>
  <div class="lines-content">
    <ContextMenu ref="contextMenuRef" :items="contextMenuItems" />
    <BookViewNoteBubble
      v-if="activeBubbleNote && activeBubbleAnchorRect"
      :note="activeBubbleNote"
      :anchor-rect="activeBubbleAnchorRect"
      :update-note="updateNote"
      :delete-note="deleteNote"
      @close="closeNoteBubble"
      @deleted="closeNoteBubble"
    />
    <div
      ref="scrollerEl"
      class="scroller"
      tabindex="0"
      data-ctrlf-enabled
      :style="{ fontSize: `${fontPx}px` }"
      @scroll="onScroll"
      @click="onMarkerClick"
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
            :class="{
              selected: props.commentaryVisible && !multiSelectLineIdSet && selectedLineId === lines[vItem.index]?.id,
              'toc-section': props.commentaryVisible && !multiSelectLineIdSet && isInActiveSection(vItem.index),
              'multi-selected': props.commentaryVisible && isMultiSelected(vItem.index),
            }"
            :data-alt-toc="props.altTocLabelMap?.get(vItem.index)"
            v-html="lineContent(lines[vItem.index]!.content!, vItem.index, lines[vItem.index]!.id)"
            @click="onLineClick(vItem.index, $event)"
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
  padding-block-end: 24px;
}
.line {
  padding-inline: 12px;
  max-width: var(--lines-content-max-width, none);
  margin-inline: auto;
  font-family: var(--text-font);
  font-size: var(--font-size, 100%);
  line-height: var(--line-height, 1.7);
  color: var(--text-primary);
  text-align: justify;
  position: relative;
}
.line.placeholder {
  height: 28px;
  max-width: var(--lines-content-max-width, none);
  margin-inline: auto;
  padding-inline: 0;
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
.line.toc-section::after {
  opacity: 0.2;
}
.line.selected::after {
  opacity: 1;
}
.line.multi-selected {
  background: color-mix(in srgb, var(--accent-color) 8%, transparent);
}
.line.multi-selected::after {
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
.line :deep(mark.user-highlight) {
  border-radius: 2px;
}
.line :deep(.user-note-marker) {
  font-size: 0.72em;
  vertical-align: super;
  line-height: 1;
  color: var(--accent-color);
  cursor: pointer;
  font-style: normal;
  font-weight: normal;
  letter-spacing: 0;
  transition: color 100ms;
}
.line :deep(.user-note-marker:hover) {
  color: color-mix(in srgb, var(--accent-color) 70%, var(--text-primary));
}
</style>
