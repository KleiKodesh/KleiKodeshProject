<script setup lang="ts">
import { computed, ref, watch, nextTick } from 'vue'
import { useScopedKeys } from '@/composables/useTextSelectionKeys'
import { useScopedCopy } from '@/composables/useLineCopy'
import { useVirtualizer } from '@tanstack/vue-virtual'
import CommentaryHeader from './CommentaryHeader.vue'
import CommentaryHeaderNav from './CommentaryHeaderNav.vue'
import CommentaryTreePanel from './CommentaryTreePanel.vue'
import LoadingAnimation from '@/components/common/LoadingAnimation.vue'
import ContextMenu from '@/components/common/ContextMenu.vue'
import type { ContextMenuItem } from '@/components/common/ContextMenu.vue'
import type { CommentaryGroup } from './useCommentary'
import { useSettingsStore } from '@/stores/settingsStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import { storeToRefs } from 'pinia'
import { applyDiacriticsFilter, removeDiacriticsForSearch } from '@/utils/hebrewTextProcessing'
import { censorDivineNames } from '@/utils/censorDivineNames'
import { scrollToIndexWithRetry } from '@/utils/scrollToIndexWithRetry'
import { useVirtualScrollerKeys } from '@/composables/useVirtualScrollerKeys'

const props = defineProps<{
  selectedLineId: number | null
  groups: CommentaryGroup[]
  loading: boolean
  searchQuery?: string
  currentMatchFlatIndex?: number
  currentMatchOccurrence?: number
  pinnedBookId?: number | null
}>()
const emit = defineEmits<{
  close: []
  'navigate-section': [direction: 'next' | 'prev', bookId: number]
  'open-book': [bookId: number, lineIndex: number]
  'toggle-search': []
  scroll: [scrollIndex: number, scrollOffset: number]
}>()

const settingsStore = useSettingsStore()
const { zoom } = storeToRefs(useBookViewStore())
const diacriticsState = computed(() => settingsStore.diacriticsState)
const commentaryFontPx = computed(
  () => (zoom.value / 100) * (settingsStore.commentaryFontSize / 100) * 15,
)

function highlightMatches(
  content: string,
  query: string,
  isCurrent: boolean,
  currentOccurrence: number,
): string {
  const q = removeDiacriticsForSearch(query.trim())
  if (!q) return content
  const stripped = removeDiacriticsForSearch(content.replace(/<[^>]*>/g, ''))
  if (!stripped.includes(q)) return content

  const matchStarts = new Set<number>()
  let idx = 0
  while ((idx = stripped.indexOf(q, idx)) !== -1) {
    matchStarts.add(idx)
    idx++
  }

  const out: string[] = []
  let strippedPos = 0,
    inTag = false,
    inMatch = false,
    matchCount = 0,
    matchOccurrence = 0
  for (let i = 0; i < content.length; i++) {
    const ch = content[i]!
    if (ch === '<') {
      inTag = true
      out.push(ch)
      continue
    }
    if (ch === '>') {
      inTag = false
      out.push(ch)
      continue
    }
    if (inTag) {
      out.push(ch)
      continue
    }
    const isDiacritic = /[\u0591-\u05C7]/.test(ch)
    if (!isDiacritic && matchStarts.has(strippedPos) && !inMatch) {
      out.push(
        `<mark class="search-match${isCurrent && matchOccurrence === currentOccurrence ? ' current' : ''}">`,
      )
      inMatch = true
      matchCount = 0
    }
    out.push(ch)
    if (!isDiacritic) {
      if (inMatch && ++matchCount === q.length) {
        out.push('</mark>')
        inMatch = false
        matchOccurrence++
      }
      strippedPos++
    }
  }
  return out.join('')
}

// Cache rendered HTML per flat index — avoids re-running applyDiacriticsFilter (DOM TreeWalker)
// and censorDivineNames (6 regexes) on every render cycle for unchanged commentary lines.
const renderCache = new Map<number, string>()
let renderCacheKey = ''

function getRenderCacheKey(): string {
  return `${diacriticsState.value}|${settingsStore.censorDivineNames}|${props.searchQuery ?? ''}|${props.currentMatchFlatIndex ?? -1}|${props.currentMatchOccurrence ?? 0}`
}

function renderContent(content: string, flatIndex: number): string {
  const key = getRenderCacheKey()
  if (key !== renderCacheKey) {
    renderCache.clear()
    renderCacheKey = key
  }
  const cached = renderCache.get(flatIndex)
  if (cached !== undefined) return cached

  let result =
    diacriticsState.value === 0 ? content : applyDiacriticsFilter(content, diacriticsState.value)
  if (settingsStore.censorDivineNames) result = censorDivineNames(result)
  if (props.searchQuery?.trim())
    result = highlightMatches(
      result,
      props.searchQuery,
      flatIndex === props.currentMatchFlatIndex,
      props.currentMatchOccurrence ?? 0,
    )

  renderCache.set(flatIndex, result)
  return result
}

type FlatItem =
  | { type: 'header'; bookTitle: string; connectionTypes: string[]; sectionLabel?: string }
  | { type: 'line'; content: string; lineId: number }

const flatItems = computed<FlatItem[]>(() => {
  const items: FlatItem[] = []
  for (const g of visibleGroups.value) {
    items.push({
      type: 'header',
      bookTitle: g.bookTitle,
      connectionTypes: g.connectionTypes,
      sectionLabel: g.sectionLabel,
    })
    for (const l of g.lines) items.push({ type: 'line', content: l.content, lineId: l.lineId })
  }
  return items
})

// Invalidate render cache when groups change (new line content loaded)
watch(
  () => props.groups,
  () => {
    renderCache.clear()
    renderCacheKey = ''
  },
  { flush: 'sync' },
)

const scrollerEl = ref<HTMLElement | null>(null)
const scrollTop = ref(0)
const treeVisible = ref(false)
const hiddenBookIds = ref(new Set<number>())

function toggleBookVisibility(bookId: number) {
  // capture current scroll before visibleGroups changes
  const savedScrollTop = scrollerEl.value?.scrollTop ?? 0
  const next = new Set(hiddenBookIds.value)
  if (next.has(bookId)) next.delete(bookId)
  else next.add(bookId)
  hiddenBookIds.value = next
  nextTick(() => {
    if (scrollerEl.value) scrollerEl.value.scrollTop = savedScrollTop
  })
}

const visibleGroups = computed(() => props.groups.filter((g) => !hiddenBookIds.value.has(g.bookId)))

const { isSelectAll, selectAllInContainer } = useScopedKeys(scrollerEl, {
  onCtrlF: () => emit('toggle-search'),
})
useScopedCopy(
  scrollerEl,
  () => visibleGroups.value.flatMap((g) => g.lines.map((l) => l.content)),
  isSelectAll,
)
useVirtualScrollerKeys(
  scrollerEl,
  () =>
    virtualizer.value as unknown as import('@tanstack/vue-virtual').Virtualizer<Element, Element>,
  () => flatItems.value.length,
)

const contextMenuRef = ref<InstanceType<typeof ContextMenu> | null>(null)
const contextMenuItems: ContextMenuItem[] = [
  { label: 'העתק', action: () => document.execCommand('copy') },
  { label: 'בחר הכל', action: selectAllInContainer },
]

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

const NAV_HEIGHT = 32

const stickyHeader = computed(() => {
  let active: (FlatItem & { type: 'header' }) | null = null
  for (const m of virtualizer.value.measurementsCache) {
    const item = flatItems.value[m.index]
    if (item?.type !== 'header') continue
    // Switch only when the header's bottom edge has scrolled past the nav
    if (m.end <= scrollTop.value + NAV_HEIGHT + 5) active = item as FlatItem & { type: 'header' }
    else break
  }
  return active
})

const activeHeader = computed(
  () =>
    stickyHeader.value ??
    (flatItems.value.find((i) => i.type === 'header') as
      | (FlatItem & { type: 'header' })
      | undefined) ??
    null,
)

const activeBookId = computed(
  () => visibleGroups.value.find((g) => g.bookTitle === activeHeader.value?.bookTitle)?.bookId ?? 0,
)

const suppressTreeScroll = ref(false)

function scrollToGroup(bookId: number) {
  const idx = flatItems.value.findIndex(
    (item) =>
      item.type === 'header' &&
      visibleGroups.value.find((g) => g.bookId === bookId && g.bookTitle === item.bookTitle),
  )
  if (idx === -1) return
  virtualizer.value.scrollToIndex(idx, { align: 'start' })
  // scrollToIndex is synchronous for already-measured items — read scrollTop immediately
  scrollTop.value = scrollerEl.value?.scrollTop ?? 0
  // also update after paint in case the browser deferred the scroll
  requestAnimationFrame(() => {
    scrollTop.value = scrollerEl.value?.scrollTop ?? 0
  })
}

function onScroll() {
  scrollTop.value = scrollerEl.value?.scrollTop ?? 0
  const pos = captureScrollPos()
  if (pos) emit('scroll', pos.scrollIndex, pos.scrollOffset)
}

// When groups reload, scroll back to the pinned book (captured in parent before selectedLineId changes)
watch(
  () => props.groups,
  async (newGroups) => {
    const pinned = props.pinnedBookId
    if (!pinned || !newGroups.length) return
    if (newGroups.some((g) => g.bookId === pinned)) {
      await nextTick()
      scrollToGroup(pinned)
    }
  },
  { flush: 'post' },
)

// Reset hidden books when the line changes (new context)
watch(
  () => props.selectedLineId,
  () => {
    hiddenBookIds.value = new Set()
  },
)

const topVisibleFlatIndex = computed(() => {
  const st = scrollTop.value + NAV_HEIGHT
  for (const m of virtualizer.value.measurementsCache) {
    if (m.end > st) return m.index
  }
  return 0
})

function scrollToFlatIndex(flatIndex: number) {
  if (!scrollerEl.value) return
  scrollToIndexWithRetry(
    virtualizer.value as unknown as import('@tanstack/vue-virtual').Virtualizer<Element, Element>,
    scrollerEl.value,
    flatIndex,
    -52,
  )
}

function captureScrollPos(): { scrollIndex: number; scrollOffset: number } | null {
  const first = virtualizer.value.getVirtualItems()[0]
  if (!first || !scrollerEl.value) return null
  return {
    scrollIndex: first.index,
    scrollOffset: Math.max(0, scrollerEl.value.scrollTop - first.start),
  }
}

function restoreCommentaryScrollPos(scrollIndex: number, scrollOffset: number) {
  // Same two-rAF pattern as BookViewLinesContent:
  // scrollToIndex triggers TanStack's internal correction — wait one rAF for it to settle,
  // then set scrollTop directly using the real measured item.start.
  virtualizer.value.scrollToIndex(scrollIndex, { align: 'start' })
  requestAnimationFrame(() => {
    const item = virtualizer.value.measurementsCache.find((m) => m.index === scrollIndex)
    if (item && scrollerEl.value) scrollerEl.value.scrollTop = item.start + scrollOffset
  })
}

defineExpose({
  scrollToGroup,
  scrollToFlatIndex,
  topVisibleFlatIndex,
  activeBookId,
  captureScrollPos,
  restoreCommentaryScrollPos,
})
</script>

<template>
  <div class="commentary-view">
    <ContextMenu ref="contextMenuRef" :items="contextMenuItems" />
    <div class="body">
      <CommentaryTreePanel
        v-if="treeVisible && props.groups.length"
        :groups="props.groups"
        :hidden-book-ids="hiddenBookIds"
        @toggle="toggleBookVisibility"
        @close="treeVisible = false"
      />
      <div class="content-col">
        <CommentaryHeaderNav
          class="sticky-nav"
          :groups="visibleGroups"
          :scroll-to-group="scrollToGroup"
          :book-title="activeHeader?.bookTitle ?? ''"
          :active-book-id="activeBookId"
          @update:active-book-id="() => {}"
          @navigate-section="(d, id) => emit('navigate-section', d, id)"
          @toggle-search="emit('toggle-search')"
          @open-book="(bookId, lineIndex) => emit('open-book', bookId, lineIndex)"
          @close="emit('close')"
          @toggle-tree="treeVisible = !treeVisible"
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
          :style="{ fontSize: `${commentaryFontPx}px` }"
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
              <CommentaryHeader
                v-if="flatItems[vItem.index]?.type === 'header'"
                :book-title="(flatItems[vItem.index] as any).bookTitle"
                :section-label="(flatItems[vItem.index] as any).sectionLabel"
                :groups="visibleGroups"
                @navigate-section="(d, id) => emit('navigate-section', d, id)"
                @open-book="(bookId, lineIndex) => emit('open-book', bookId, lineIndex)"
              />
              <div
                v-else
                class="line"
                v-html="renderContent((flatItems[vItem.index] as any).content, vItem.index)"
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
