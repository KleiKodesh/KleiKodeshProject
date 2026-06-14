<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { useScopedKeys } from '@/composables/useTextSelectionKeys'
import { useScopedCopy } from '@/composables/useLineCopy'
import { useVirtualizer } from '@tanstack/vue-virtual'
import CommentaryHeader from './CommentaryHeader.vue'
import CommentaryHeaderNav from './CommentaryHeaderNav.vue'
import LoadingAnimation from '@/components/LoadingAnimation.vue'
import ContextMenu from '@/components/ContextMenu.vue'
import type { CommentaryGroup } from './useCommentary'
import type { CommentaryVisibilityItem, PinnedCommentaryGroup } from '../bookViewTypes'
import { isCommentaryItemVisible } from '../bookViewTypes'
import { useVirtualScrollerKeys } from '@/composables/useVirtualScrollerKeys'
import { useCommentaryScroll } from './useCommentaryScroll'
import { useCommentaryCopy } from './useCommentaryCopy'
import type { Highlight } from '../lines/useBookViewHighlights'
import type { Note } from '../lines/useBookViewNotes'
import BookViewNoteBubble from '../lines/BookViewNoteBubble.vue'

const props = defineProps<{
  selectedLineId: number | null
  groups: CommentaryGroup[]
  loading: boolean
  visibilityList: CommentaryVisibilityItem[]
  // Hoisted annotation & render state — initialized in useBookView, survive v-if toggle
  getHighlightsForLine: (lineId: number) => Highlight[]
  applyHighlight: (lineId: number, startOffset: number, endOffset: number, colorArgb: number) => void
  clearHighlight: (lineId: number, startOffset: number, endOffset: number) => void
  getNotesForLine: (lineId: number) => Note[]
  scheduleNotesLoad: (lineIds: number[]) => void
  createNote: (lineId: number, startOffset: number, endOffset: number, quote: string) => Promise<Note>
  updateNote: (note: Note, newText: string) => Promise<void>
  deleteNote: (note: Note) => Promise<void>
  commentaryFontPx: number
  renderContent: (content: string, flatIndex: number, lineId: number | undefined, searchQuery: string | undefined) => string
  setCurrentMark: (scroller: HTMLElement, flatIndex: number, occurrence: number) => void
  commentaryTocPaths: Map<number, string>
  searchQuery?: string
  currentMatchFlatIndex?: number
  currentMatchOccurrence?: number
  pinnedGroup?: PinnedCommentaryGroup | null
  filterVisible?: boolean
}>()
const emit = defineEmits<{
  close: []
  'navigate-section': [direction: 'next' | 'prev', bookId: number]
  'open-book': [bookId: number, lineIndex: number]
  'toggle-filter-panel': []
  'toggle-search': []
  scroll: [scrollIndex: number, scrollOffset: number]
}>()

// ── Note bubble state ─────────────────────────────────────────────────────────

const activeBubbleNote = ref<Note | null>(null)
const activeBubbleAnchorRect = ref<DOMRect | null>(null)

function openNoteBubble(note: Note, markerEl: HTMLElement) {
  activeBubbleNote.value = note
  activeBubbleAnchorRect.value = markerEl.getBoundingClientRect()
}

function closeNoteBubble() {
  activeBubbleNote.value = null
  activeBubbleAnchorRect.value = null
}

// ── Flat item list ────────────────────────────────────────────────────────────

type FlatItem =
  | {
      type: 'header'
      bookId: number
      bookTitle: string
      connectionTypes: string[]
      sectionLabel?: string
      subSectionLabel?: string
      firstLineIndex?: number
      tocPath?: string
    }
  | { type: 'line'; content: string; lineId: number }

const scrollerEl = ref<HTMLElement | null>(null)
const headerNavRef = ref<InstanceType<typeof CommentaryHeaderNav> | null>(null)

const visibleGroups = computed(() => {
  if (!props.visibilityList.length) return props.groups
  const visibleKeys = new Set(
    props.visibilityList
      .filter(isCommentaryItemVisible)
      .map((item) => `${item.bookId}::${item.sectionLabel}::${item.subSectionLabel}`),
  )
  return props.groups.filter((group) =>
    visibleKeys.has(`${group.bookId}::${group.sectionLabel ?? ''}::${group.subSectionLabel ?? ''}`),
  )
})

const flatItems = computed<FlatItem[]>(() => {
  const tocPaths = props.commentaryTocPaths
  const items: FlatItem[] = []
  for (const g of visibleGroups.value) {
    items.push({
      type: 'header',
      bookId: g.bookId,
      bookTitle: g.bookTitle,
      connectionTypes: g.connectionTypes,
      sectionLabel: g.sectionLabel,
      subSectionLabel: g.subSectionLabel,
      firstLineIndex: g.lines[0]?.lineIndex,
      tocPath: tocPaths.get(g.bookId),
    })
    for (const l of g.lines) items.push({ type: 'line', content: l.content, lineId: l.lineId })
  }
  return items
})

// ── Virtualizer ───────────────────────────────────────────────────────────────

const { isSelectAll, selectAllInContainer } = useScopedKeys(scrollerEl, {
  onCtrlF: () => emit('toggle-search'),
})
useScopedCopy(
  scrollerEl,
  () => visibleGroups.value.flatMap((g) => g.lines.map((l) => l.content)),
  isSelectAll,
)

const contextMenuRef = ref<InstanceType<typeof ContextMenu> | null>(null)

const virtualizer = useVirtualizer(
  computed(() => ({
    count: flatItems.value.length,
    getScrollElement: () => scrollerEl.value,
    estimateSize: (i) => (flatItems.value[i]?.type === 'header' ? 40 : 48),
    overscan: 10,
  })),
)

const virtualItems = computed(() => virtualizer.value.getVirtualItems())
const totalSize = computed(() => virtualizer.value.getTotalSize())

// ── Notes — viewport-driven lazy load trigger ─────────────────────────────────
// The notes data layer lives in useBookView (hoisted). We only trigger loading
// for lines that enter the viewport.

watch(
  virtualItems,
  (items) => {
    const lineIds = items
      .map((v) => flatItems.value[v.index])
      .filter((item): item is { type: 'line'; content: string; lineId: number } =>
        item?.type === 'line',
      )
      .map((item) => item.lineId)
      .filter((id) => id > 0)
    props.scheduleNotesLoad(lineIds)
  },
  { immediate: true },
)

// ── Rendering ─────────────────────────────────────────────────────────────────

// Apply .current class via DOM toggle — no re-render needed when only the
// active occurrence changes within an already-rendered commentary line.
watch(
  () => [props.currentMatchFlatIndex, props.currentMatchOccurrence] as const,
  ([flatIndex, occurrence]) => {
    if (!scrollerEl.value) return
    nextTick(() => {
      if (!scrollerEl.value) return
      props.setCurrentMark(scrollerEl.value, flatIndex ?? -1, occurrence ?? 0)
    })
  },
)

// ── Scroll ────────────────────────────────────────────────────────────────────

const {
  activeHeader,
  activePinnedGroup,
  onScroll: handleScroll,
  scrollToGroup,
  scrollToFlatIndex,
  captureScrollPos,
  restoreCommentaryScrollPos,
  topVisibleFlatIndex,
  setupGroupReloadScroll,
} = useCommentaryScroll(
  () => flatItems.value,
  () => visibleGroups.value,
  () => virtualizer.value,
  () => scrollerEl.value,
)

setupGroupReloadScroll(
  () => props.groups,
  () => props.pinnedGroup,
  () => props.loading,
)

useVirtualScrollerKeys(
  scrollerEl,
  () =>
    virtualizer.value as unknown as import('@tanstack/vue-virtual').Virtualizer<Element, Element>,
  () => flatItems.value.length,
)

// ── Context menu ──────────────────────────────────────────────────────────────

const { contextMenuItems } = useCommentaryCopy(
  () => {
    const pinned = activePinnedGroup.value
    return pinned
      ? visibleGroups.value.find(
          (g) =>
            g.bookId === pinned.bookId &&
            (g.sectionLabel ?? '') === pinned.sectionLabel &&
            (g.subSectionLabel ?? '') === pinned.subSectionLabel,
        ) ?? null
      : null
  },
  (bookId) => props.commentaryTocPaths.get(bookId),
  selectAllInContainer,
  scrollerEl,
  (lineId, startOffset, endOffset, colorArgb) =>
    props.applyHighlight(lineId, startOffset, endOffset, colorArgb),
  (lineId, startOffset, endOffset) => props.clearHighlight(lineId, startOffset, endOffset),
  (lineId, startOffset, endOffset, quote) =>
    props.createNote(lineId, startOffset, endOffset, quote).then((note) => {
      nextTick(() => {
        const marker = scrollerEl.value?.querySelector(
          `[data-note-id="${note.id}"]`,
        ) as HTMLElement | null
        if (marker) openNoteBubble(note, marker)
      })
    }),
)

function onScroll() {
  handleScroll((scrollIndex, scrollOffset) => {
    emit('scroll', scrollIndex, scrollOffset)
  })
}

function onMarkerClick(event: MouseEvent) {
  const marker = (event.target as HTMLElement).closest('[data-note-id]') as HTMLElement | null
  if (!marker) return
  const noteId = parseInt(marker.dataset['noteId'] ?? '', 10)
  if (isNaN(noteId)) return
  event.stopPropagation()
  const lineId = parseInt(
    (marker.closest('[data-line-id]') as HTMLElement | null)?.dataset['lineId'] ?? '',
    10,
  )
  const found = props.getNotesForLine(lineId).find((n) => n.id === noteId)
  if (found) openNoteBubble(found, marker)
}

const activeBookId = computed(() => activePinnedGroup.value?.bookId ?? null)

defineExpose({
  scrollToGroup,
  scrollToFlatIndex,
  topVisibleFlatIndex,
  activePinnedGroup,
  activeBookId,
  captureScrollPos,
  restoreCommentaryScrollPos,
  getFilterButtonEl: () => headerNavRef.value?.filterBtnRef ?? null,
})

const activeTocPath = computed(() =>
  activePinnedGroup.value ? props.commentaryTocPaths.get(activePinnedGroup.value.bookId) : undefined,
)
</script>

<template>
  <div class="commentary-view">
    <ContextMenu ref="contextMenuRef" :items="contextMenuItems" />
    <BookViewNoteBubble
      v-if="activeBubbleNote && activeBubbleAnchorRect"
      :note="activeBubbleNote"
      :anchor-rect="activeBubbleAnchorRect"
      :update-note="props.updateNote"
      :delete-note="props.deleteNote"
      @close="closeNoteBubble"
      @deleted="closeNoteBubble"
    />
    <div class="body">
      <div class="content-col" :style="{ fontSize: `${commentaryFontPx}px` }">
        <CommentaryHeaderNav
          ref="headerNavRef"
          class="sticky-nav"
          :groups="visibleGroups"
          :scroll-to-group="scrollToGroup"
          :active-pinned-group="activePinnedGroup"
          :filter-visible="props.filterVisible"
          :active-toc-path="activeTocPath"
          @navigate-section="(d, id) => emit('navigate-section', d, id)"
          @toggle-filter="emit('toggle-filter-panel')"
          @toggle-search="emit('toggle-search')"
          @open-book="(bookId, lineIndex) => emit('open-book', bookId, lineIndex)"
          @close="emit('close')"
        />
        <div v-if="props.loading" class="state-overlay"><LoadingAnimation /></div>
        <div v-else-if="!flatItems.length" class="state-overlay">
          <span class="hint">{{
            props.selectedLineId == null ? 'בחר שורה לצפייה במפרשים' : 'אין מפרשים לשורה זו'
          }}</span>
        </div>
        <div
          v-else
          ref="scrollerEl"
          class="scroller"
          tabindex="0"
          data-ctrlf-enabled
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
              <CommentaryHeader
                v-if="flatItems[vItem.index]?.type === 'header'"
                :book-id="(flatItems[vItem.index] as any).bookId"
                :book-title="(flatItems[vItem.index] as any).bookTitle"
                :first-line-index="(flatItems[vItem.index] as any).firstLineIndex"
                :section-label="(flatItems[vItem.index] as any).sectionLabel"
                :sub-section-label="(flatItems[vItem.index] as any).subSectionLabel"
                :own-toc-path="(flatItems[vItem.index] as any).tocPath"
                @navigate-section="(d, id) => emit('navigate-section', d, id)"
                @open-book="(bookId, lineIndex) => emit('open-book', bookId, lineIndex)"
              />
              <div
                v-else
                class="line"
                :class="{ 'line-no-text': (flatItems[vItem.index] as any).lineId === -1 }"
                :data-line-id="(flatItems[vItem.index] as any).lineId"
                v-html="renderContent((flatItems[vItem.index] as any).content, vItem.index, (flatItems[vItem.index] as any).lineId, props.searchQuery)"
              />
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.commentary-view {
  height: 100%;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}
.body {
  flex: 1;
  display: flex;
  flex-direction: row;
  min-height: 0;
  position: relative;
}
.content-col {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-width: 0;
}
.sticky-nav {
  flex-shrink: 0;
  height: 32px;
  font-size: 13px;
}
.state-overlay {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
}
.hint {
  font-size: 13px;
  color: var(--text-secondary);
}
.scroller {
  flex: 1;
  overflow-y: auto;
}
.line {
  padding-inline: 12px;
  padding-block: 2px;
  font-family: var(--commentary-text-font);
  font-size: var(--commentary-font-size, 100%);
  line-height: var(--commentary-line-height, 1.7);
  color: var(--text-primary);
  text-align: justify;
}
.line :deep(h1),
.line :deep(h2),
.line :deep(h3),
.line :deep(h4),
.line :deep(h5),
.line :deep(h6) {
  font-family: var(--commentary-header-font);
}
.line-no-text {
  color: var(--text-secondary);
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
