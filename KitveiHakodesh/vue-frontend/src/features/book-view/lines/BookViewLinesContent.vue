<script setup lang="ts">
import { computed, ref, watch, nextTick, onBeforeUnmount } from 'vue'
import { storeToRefs } from 'pinia'
import { useVirtualizer } from '@tanstack/vue-virtual'
import { useTabStore } from '@/stores/tabStore'
import { useSettingsStore } from '@/stores/settingsStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import type { LineItem } from './useBookViewLinesTable'
import type { TocEntry } from '../toc/useBookViewToc'
import type { CommentaryTreeState, CommentaryVisibilityItem, PinnedCommentaryGroup } from '../bookViewTypes'
import ContextMenu from '@/components/ContextMenu.vue'
import { useEventListener } from '@vueuse/core'
import { useScopedKeys } from '@/composables/useTextSelectionKeys'
import { useScopedCopy } from '@/composables/useLineCopy'
import { scrollToIndexWithRetry } from '@/utils/scrollToIndexWithRetry'
import { useVirtualScrollerKeys } from '@/composables/useVirtualScrollerKeys'
import { useBookViewLineRenderer } from './useBookViewLineRenderer'
import { useBookViewLineCopyMenu } from './useBookViewLineCopyMenu'
import { useBookViewHighlights } from './useBookViewHighlights'
import { useBookViewNotes } from './useBookViewNotes'
import BookViewNoteBubble from './BookViewNoteBubble.vue'

const emit = defineEmits<{ scrolled: [number, number]; lineSelected: [number]; 'ctrl-f': [] }>()
const props = defineProps<{
  lines: LineItem[]
  prioritise: (lineIndex: number) => void
  altTocLabelMap?: Map<number, string>
  selectedLineId?: number | null
  commentaryVisible?: boolean
  commentaryMode?: 'off' | 'bottom' | 'side'
  commentaryFraction?: number
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
  idbResolved?: boolean
  getActiveTocEntry?: (lineIndex: number) => TocEntry | null
  getTocPath?: (entry: TocEntry) => string
  pinnedCommentaryGroup?: PinnedCommentaryGroup | null
  selectedSectionLineIds?: number[] | null
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

// ── User highlights ───────────────────────────────────────────────────────────

const { getHighlightsForLine, applyHighlight, clearHighlight } = useBookViewHighlights(bookId)

// Active note bubble state — declared early so onAddNote/onMarkerClick can reference it
const activeBubbleNote = ref<import('./useBookViewNotes').Note | null>(null)
const activeBubbleAnchorRect = ref<DOMRect | null>(null)

function openNoteBubble(note: import('./useBookViewNotes').Note, markerEl: HTMLElement) {
  activeBubbleNote.value = note
  activeBubbleAnchorRect.value = markerEl.getBoundingClientRect()
}

function closeNoteBubble() {
  activeBubbleNote.value = null
  activeBubbleAnchorRect.value = null
}

// ── Selection → line offsets ──────────────────────────────────────────────────

interface SelectionOnLine {
  lineId: number
  lineIndex: number
  startOffset: number
  endOffset: number
}

/**
 * Multi-line selection support for highlights.
 * A selection spanning multiple lines is treated as a series of separate
 * highlights — one per line, each with its own start/end offsets.
 */
interface MultiLineSelection {
  lines: SelectionOnLine[]
}

function extractSelectionOnLine(): MultiLineSelection | null {
  const sel = window.getSelection()
  if (!sel || sel.rangeCount === 0 || sel.isCollapsed) return null
  const range = sel.getRangeAt(0)
  if (!scrollerEl.value) return null
  const lineEls = Array.from(scrollerEl.value.querySelectorAll('.line'))
  const intersected = lineEls.filter((el) => range.intersectsNode(el))
  if (intersected.length === 0) return null

  const selectionLines: SelectionOnLine[] = []

  for (let i = 0; i < intersected.length; i++) {
    const lineEl = intersected[i] as HTMLElement
    const vItemEl = lineEl.closest('[data-index]') as HTMLElement | null
    if (!vItemEl) continue
    const vIndex = parseInt(vItemEl.dataset['index'] ?? '', 10)
    const lineItem = props.lines[vIndex]
    if (!lineItem || lineItem.content == null) continue

    const strippedText = (lineEl.textContent ?? '').replace(/[\u0591-\u05C7]/g, '')

    function countStrippedOffset(node: Node, offsetInNode: number): number {
      const walker = document.createTreeWalker(lineEl, NodeFilter.SHOW_TEXT)
      let stripped = 0
      let current: Text | null
      while ((current = walker.nextNode() as Text | null)) {
        if (current === node) {
          const slice = current.textContent?.slice(0, offsetInNode) ?? ''
          stripped += slice.replace(/[\u0591-\u05C7]/g, '').length
          return stripped
        }
        stripped += (current.textContent ?? '').replace(/[\u0591-\u05C7]/g, '').length
      }
      return stripped
    }

    const isFirstLine = i === 0
    const isLastLine = i === intersected.length - 1

    let startOffset = 0
    let endOffset = strippedText.length

    if (isFirstLine) startOffset = countStrippedOffset(range.startContainer, range.startOffset)
    if (isLastLine) endOffset = countStrippedOffset(range.endContainer, range.endOffset)

    if (startOffset < endOffset) {
      selectionLines.push({
        lineId: lineItem.id,
        lineIndex: lineItem.lineIndex,
        startOffset,
        endOffset: Math.min(endOffset, strippedText.length),
      })
    }
  }

  if (selectionLines.length === 0) return null
  return { lines: selectionLines }
}

// ── Highlight actions (called from context menu) ──────────────────────────────

function onHighlight(colorArgb: number) {
  const selection = extractSelectionOnLine()
  if (!selection) return
  for (const line of selection.lines) {
    applyHighlight(line.lineId, line.startOffset, line.endOffset, colorArgb)
  }
  window.getSelection()?.removeAllRanges()
}

function onClearHighlight() {
  const selection = extractSelectionOnLine()
  if (!selection) return
  // Clear highlights from each line separately
  for (const line of selection.lines) {
    clearHighlight(line.lineId, line.startOffset, line.endOffset)
  }
  window.getSelection()?.removeAllRanges()
}

function onAddNote() {
  const selection = extractSelectionOnLine()
  if (!selection || selection.lines.length === 0) return
  const firstLine = selection.lines[0]!
  // Capture quoted text before clearing the selection
  const rawQuote = window.getSelection()?.toString() ?? ''
  const quote = rawQuote.replace(/[\u0591-\u05C7]/g, '').trim()
  window.getSelection()?.removeAllRanges()
  void createNote(firstLine.lineId, firstLine.startOffset, firstLine.endOffset, quote).then(
    (note) => {
      // After the renderer re-runs, find the new marker and open the bubble
      nextTick(() => {
        const marker = scrollerEl.value?.querySelector(
          `[data-note-id="${note.id}"]`,
        ) as HTMLElement | null
        if (marker) openNoteBubble(note, marker)
      })
    },
  )
}

function onMarkerClick(event: MouseEvent) {
  const marker = (event.target as HTMLElement).closest('[data-note-id]') as HTMLElement | null
  if (!marker) return
  const noteId = parseInt(marker.dataset['noteId'] ?? '', 10)
  if (isNaN(noteId)) return
  event.stopPropagation()
  // Walk notesByLine to find the note by id
  for (const notes of notesByLine.value.values()) {
    const found = notes.find((n) => n.id === noteId)
    if (found) { openNoteBubble(found, marker); return }
  }
}

// ── Line rendering ────────────────────────────────────────────────────────────

const { lineContent } = useBookViewLineRenderer(settingsStore, diacriticsState, () => ({
  searchQuery: props.searchQuery,
  currentMatchLineIndex: props.currentMatchLineIndex,
  currentMatchOccurrence: props.currentMatchOccurrence,
  searchHighlightLineIndex: props.searchHighlightLineIndex,
  searchHighlightQuery: props.searchHighlightQuery,
  searchHighlightSnippet: props.searchHighlightSnippet,
  searchHighlightTerms: props.searchHighlightTerms,
  getHighlightsForLine,
  getNotesForLine,
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
  onHighlight,
  onClearHighlight,
  onAddNote,
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

// ── User notes — lazy, viewport-driven ───────────────────────────────────────

const { notesByLine, getNotesForLine, createNote, updateNote, deleteNote } = useBookViewNotes(
  bookId,
  () => virtualItems.value.map((v) => props.lines[v.index]?.id ?? 0).filter((id) => id > 0),
)

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
  // Gate on idbResolved so we don't give up before IDB has had a chance to respond —
  // on page reload, lines (placeholders) arrive before IDB resolves, and without this
  // guard the fallback would kill the outer watcher before the saved position is known.
  watch(
    () => [props.lines, props.idbResolved] as const,
    ([val, resolved]) => {
      if (!val.length || restored || !resolved) return
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
    const pinnedGroup = props.pinnedCommentaryGroup
      ? {
          bookId: props.pinnedCommentaryGroup.bookId,
          sectionLabel: props.pinnedCommentaryGroup.sectionLabel,
          subSectionLabel: props.pinnedCommentaryGroup.subSectionLabel,
        }
      : null
    tabStore.setBookViewState(tabId, bookId, {
      ...pos,
      selectedLineId: props.selectedLineId,
      commentaryScrollIndex: props.commentaryScrollIndex,
      commentaryScrollOffset: props.commentaryScrollOffset,
      commentaryFilterState: filterState,
      zoom: zoom.value,
      commentaryVisible: props.commentaryVisible,
      commentaryMode: props.commentaryMode,
      commentaryFraction: props.commentaryFraction,
      autoSelectTopLine: autoSelectTopLine.value,
      pinnedCommentaryGroup: pinnedGroup,
    })
    tabStore.setLastReadPos(bookId, {
      ...pos,
      selectedLineId: props.selectedLineId,
      commentaryScrollIndex: props.commentaryScrollIndex,
      commentaryScrollOffset: props.commentaryScrollOffset,
      commentaryFilterState: filterState,
      commentaryMode: props.commentaryMode,
      commentaryFraction: props.commentaryFraction,
      pinnedCommentaryGroup: pinnedGroup,
    })
  }
}

// Save when the commentary panel closes so the commentary scroll position
// (which just arrived via prop update from onCommentaryScroll) is flushed to
// IDB before CommentaryView unmounts and the position would otherwise be lost.
watch(
  () => props.commentaryVisible,
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

  const reserved = props.searchBarVisible ? 44 : 0
  const virt = virtualizer.value as unknown as import('@tanstack/vue-virtual').Virtualizer<Element, Element>
  const m = virt.measurementsCache.find((c) => c.index === lineIndex)

  if (m) {
    // Line is already measured by the virtualizer. Scroll to the line top first,
    // then wait for Vue to render the new currentMatchOccurrence (which invalidates
    // the render cache and re-renders the line HTML). Use MutationObserver to detect
    // when the <mark class="current"> actually appears in the DOM, then adjust.
    setProgrammaticScroll()

    // Step 1: scroll to line top immediately so the line is visible.
    const targetScrollTop = m.start - reserved - 8
    if (Math.abs(scrollerEl.value.scrollTop - targetScrollTop) > 2) {
      scrollerEl.value.scrollTop = targetScrollTop
    }

    // Step 2: wait for the current mark to appear/move in the DOM, then fine-adjust.
    const scroller = scrollerEl.value
    let settled = false

    function adjustToMark() {
      if (settled || !scroller) return
      const mark = scroller.querySelector('mark.search-match.current') as HTMLElement | null
      if (!mark) return false
      const markRect = mark.getBoundingClientRect()
      const scrollerRect = scroller.getBoundingClientRect()
      const relativeTop = markRect.top - scrollerRect.top
      const relativeBottom = markRect.bottom - scrollerRect.top
      const alreadyVisible = relativeTop >= reserved + 4 && relativeBottom <= scrollerRect.height - 4
      if (!alreadyVisible) {
        scroller.scrollTop += relativeTop - reserved - 8
      }
      return true
    }

    // Try immediately after two rAFs (covers same-line occurrence changes where
    // the mark is already in the DOM and just needs its class updated).
    requestAnimationFrame(() => requestAnimationFrame(() => {
      if (adjustToMark()) { settled = true; return }

      // Mark not found yet — the render cache was just invalidated and Vue hasn't
      // re-rendered the line HTML yet. Watch for DOM mutations on the scroller.
      const observer = new MutationObserver(() => {
        if (adjustToMark()) {
          settled = true
          observer.disconnect()
        }
      })
      observer.observe(scroller, { childList: true, subtree: true, characterData: false, attributes: true, attributeFilter: ['class'] })
      // Safety timeout — disconnect after 500ms regardless.
      setTimeout(() => {
        if (!settled) {
          observer.disconnect()
        }
      }, 500)
    }))
    return
  }

  // Line not yet rendered — use scrollToIndexWithRetry to bring it into range,
  // then scroll to the mark once it's in the DOM.
  setProgrammaticScroll()
  scrollToIndexWithRetry(
    virt,
    scrollerEl.value,
    lineIndex,
    reserved,
    5,
    () => {
      // After scrollToIndexWithRetry positions the line, wait for the mark using
      // the same MutationObserver approach.
      const scroller = scrollerEl.value
      if (!scroller) return
      let settled = false

      function adjustToMark() {
        if (!scroller) return false
        const mark = scroller.querySelector('mark.search-match.current') as HTMLElement | null
        if (!mark) return false
        const markRect = mark.getBoundingClientRect()
        const scrollerRect = scroller.getBoundingClientRect()
        const relativeTop = markRect.top - scrollerRect.top
        const relativeBottom = markRect.bottom - scrollerRect.top
        const alreadyVisible = relativeTop >= reserved + 4 && relativeBottom <= scrollerRect.height - 4
        if (!alreadyVisible) {
          scroller.scrollTop += relativeTop - reserved - 8
        }
        return true
      }

      requestAnimationFrame(() => requestAnimationFrame(() => {
        if (adjustToMark()) { settled = true; return }
        const observer = new MutationObserver(() => {
          if (adjustToMark()) { settled = true; observer.disconnect() }
        })
        observer.observe(scroller, { childList: true, subtree: true, attributes: true, attributeFilter: ['class'] })
        setTimeout(() => {
          if (!settled) {
            observer.disconnect()
          }
        }, 500)
      }))
    },
  )
}

function onLineClick(index: number) {
  const line = props.lines[index]
  if (props.commentaryVisible && line) emit('lineSelected', line.id)
}

const selectedSectionLineIdSet = computed(() =>
  props.selectedSectionLineIds ? new Set(props.selectedSectionLineIds) : null,
)

function isInActiveSection(lineIndex: number): boolean {
  const set = selectedSectionLineIdSet.value
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
              selected: props.commentaryVisible && selectedLineId === lines[vItem.index]?.id,
              'toc-section': props.commentaryVisible && isInActiveSection(vItem.index),
            }"
            :data-alt-toc="props.altTocLabelMap?.get(vItem.index)"
            v-html="lineContent(lines[vItem.index]!.content!, vItem.index, lines[vItem.index]!.id)"
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
.line.toc-section::after {
  opacity: 0.2;
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
.line :deep(mark.user-highlight) {
  border-radius: 2px;
}
.line :deep(.user-note-marker) {
  font-size: 0.72em;
  vertical-align: super;
  line-height: 1;
  color: var(--accent-color);
  cursor: pointer;
  user-select: none;
  font-style: normal;
  font-weight: normal;
  letter-spacing: 0;
  transition: color 100ms;
}
.line :deep(.user-note-marker:hover) {
  color: color-mix(in srgb, var(--accent-color) 70%, var(--text-primary));
}
</style>
