<script setup lang="ts">
import { computed, ref, watch, nextTick } from 'vue'
import { useScopedKeys } from '@/composables/useScopedKeys'
import { useScopedCopy } from '@/composables/useScopedCopy'
import { useVirtualizer } from '@tanstack/vue-virtual'
import CommentaryHeader from './CommentaryHeader.vue'
import CommentaryHeaderNav from './CommentaryHeaderNav.vue'
import CommentaryTreePanel from './CommentaryTreePanel.vue'
import LoadingAnimation from '@/components/common/LoadingAnimation.vue'
import ContextMenu from '@/components/common/ContextMenu.vue'
import type { ContextMenuItem } from '@/components/common/ContextMenu.vue'
import type { CommentaryGroup, CommentaryTreeNode } from './useCommentary'
import { useSettingsStore } from '@/stores/settingsStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import { storeToRefs } from 'pinia'
import { applyDiacriticsFilter, removeDiacriticsForSearch } from '@/utils/hebrewTextProcessing'
import { censorDivineNames } from '@/utils/censorDivineNames'
import { scrollToIndexWithRetry } from '@/utils/scrollToIndexWithRetry'

const props = defineProps<{ selectedLineId: number | null; groups: CommentaryGroup[]; loading: boolean; searchQuery?: string; currentMatchFlatIndex?: number; currentMatchOccurrence?: number; pinnedBookId?: number | null }>()
const emit = defineEmits<{ close: []; 'navigate-section': [direction: 'next' | 'prev', bookId: number]; 'open-book': [bookId: number, lineIndex: number]; 'toggle-search': []; 'scroll': [scrollIndex: number, scrollOffset: number] }>()

const settingsStore = useSettingsStore()
const { zoom } = storeToRefs(useBookViewStore())
const diacriticsState = computed(() => settingsStore.diacriticsState)

function highlightMatches(content: string, query: string, isCurrent: boolean, currentOccurrence: number): string {
  const q = removeDiacriticsForSearch(query.trim())
  if (!q) return content
  const stripped = removeDiacriticsForSearch(content.replace(/<[^>]*>/g, ''))
  if (!stripped.includes(q)) return content

  const matchStarts = new Set<number>()
  let idx = 0
  while ((idx = stripped.indexOf(q, idx)) !== -1) { matchStarts.add(idx); idx++ }

  const out: string[] = []
  let strippedPos = 0, inTag = false, inMatch = false, matchCount = 0, matchOccurrence = 0
  for (let i = 0; i < content.length; i++) {
    const ch = content[i]!
    if (ch === '<') { inTag = true; out.push(ch); continue }
    if (ch === '>') { inTag = false; out.push(ch); continue }
    if (inTag) { out.push(ch); continue }
    const isDiacritic = /[\u0591-\u05C7]/.test(ch)
    if (!isDiacritic && matchStarts.has(strippedPos) && !inMatch) {
      out.push(`<mark class="search-match${isCurrent && matchOccurrence === currentOccurrence ? ' current' : ''}">`)
      inMatch = true; matchCount = 0
    }
    out.push(ch)
    if (!isDiacritic) {
      if (inMatch && ++matchCount === q.length) { out.push('</mark>'); inMatch = false; matchOccurrence++ }
      strippedPos++
    }
  }
  return out.join('')
}

function renderContent(content: string, flatIndex: number): string {
  let result = diacriticsState.value === 0 ? content : applyDiacriticsFilter(content, diacriticsState.value)
  if (settingsStore.censorDivineNames) result = censorDivineNames(result)
  if (props.searchQuery?.trim()) result = highlightMatches(result, props.searchQuery, flatIndex === props.currentMatchFlatIndex, props.currentMatchOccurrence ?? 0)
  return result
}

type FlatItem = { type: 'header'; bookTitle: string; connectionTypes: string[]; sectionLabel?: string } | { type: 'line'; content: string; lineId: number }

const flatItems = computed<FlatItem[]>(() => {
  const items: FlatItem[] = []
  for (const g of props.groups) {
    items.push({ type: 'header', bookTitle: g.bookTitle, connectionTypes: g.connectionTypes, sectionLabel: g.sectionLabel })
    for (const l of g.lines) items.push({ type: 'line', content: l.content, lineId: l.lineId })
  }
  return items
})

const scrollerEl = ref<HTMLElement | null>(null)
const scrollTop = ref(0)
const treeVisible = ref(false)

const { isSelectAll, selectAllInContainer } = useScopedKeys(scrollerEl, { onCtrlF: () => emit('toggle-search') })
useScopedCopy(scrollerEl, () => props.groups.flatMap(g => g.lines.map(l => l.content)), isSelectAll)

const contextMenuRef = ref<InstanceType<typeof ContextMenu> | null>(null)
const contextMenuItems: ContextMenuItem[] = [
  { label: 'העתק', action: () => document.execCommand('copy') },
  { label: 'בחר הכל', action: selectAllInContainer },
]
let treeInitiatedScroll = false

function onTreeSelect(node: CommentaryTreeNode) {
  if (node.bookId == null) return
  suppressTreeScroll.value = true
  scrollToGroup(node.bookId)
  requestAnimationFrame(() => { suppressTreeScroll.value = false })
}

const virtualizer = useVirtualizer(computed(() => ({
  count: flatItems.value.length,
  getScrollElement: () => scrollerEl.value,
  estimateSize: i => flatItems.value[i]?.type === 'header' ? 40 : 48,
  overscan: 10,
})))

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

const activeHeader = computed(() => stickyHeader.value ?? (flatItems.value.find(i => i.type === 'header') as (FlatItem & { type: 'header' }) | undefined) ?? null)

const activeBookId = computed(() =>
  props.groups.find(g => g.bookTitle === activeHeader.value?.bookTitle)?.bookId ?? 0
)

const suppressTreeScroll = ref(false)

function scrollToGroup(bookId: number) {
  const idx = flatItems.value.findIndex(item => item.type === 'header' && props.groups.find(g => g.bookId === bookId && g.bookTitle === item.bookTitle))
  if (idx === -1) return
  virtualizer.value.scrollToIndex(idx, { align: 'start' })
  // scrollToIndex is synchronous for already-measured items — read scrollTop immediately
  scrollTop.value = scrollerEl.value?.scrollTop ?? 0
  // also update after paint in case the browser deferred the scroll
  requestAnimationFrame(() => { scrollTop.value = scrollerEl.value?.scrollTop ?? 0 })
}

function onScroll() {
  scrollTop.value = scrollerEl.value?.scrollTop ?? 0
  const pos = captureScrollPos()
  if (pos) emit('scroll', pos.scrollIndex, pos.scrollOffset)
}

// When groups reload, scroll back to the pinned book (captured in parent before selectedLineId changes)
watch(() => props.groups, async (newGroups) => {
  const pinned = props.pinnedBookId
  if (!pinned || !newGroups.length) return
  if (newGroups.some(g => g.bookId === pinned)) {
    await nextTick()
    scrollToGroup(pinned)
  }
}, { flush: 'post' })

const topVisibleFlatIndex = computed(() => {
  const st = scrollTop.value + NAV_HEIGHT
  for (const m of virtualizer.value.measurementsCache) {
    if (m.end > st) return m.index
  }
  return 0
})

function scrollToFlatIndex(flatIndex: number) {
  if (!scrollerEl.value) return
  scrollToIndexWithRetry(virtualizer.value as unknown as import('@tanstack/vue-virtual').Virtualizer<Element, Element>, scrollerEl.value, flatIndex, -52)
}

function captureScrollPos(): { scrollIndex: number; scrollOffset: number } | null {
  const first = virtualizer.value.getVirtualItems()[0]
  if (!first || !scrollerEl.value) return null
  return { scrollIndex: first.index, scrollOffset: scrollerEl.value.scrollTop - first.start }
}

async function restoreCommentaryScrollPos(scrollIndex: number, scrollOffset: number) {
  virtualizer.value.scrollToIndex(scrollIndex, { align: 'start' })
  await nextTick()
  await new Promise(r => setTimeout(r, 500))
  const item = virtualizer.value.getVirtualItems().find(v => v.index === scrollIndex)
  if (scrollerEl.value) scrollerEl.value.scrollTop = (item?.start ?? scrollerEl.value.scrollTop) + scrollOffset
  await new Promise(r => setTimeout(r, 600))
}

defineExpose({ scrollToGroup, scrollToFlatIndex, topVisibleFlatIndex, activeBookId, captureScrollPos, restoreCommentaryScrollPos })
</script>

<template>
  <div class="commentary-view">
    <ContextMenu ref="contextMenuRef" :items="contextMenuItems" />
    <div v-if="props.loading" class="state-overlay"><LoadingAnimation /></div>
    <div v-else-if="!flatItems.length" class="state-overlay"><span class="hint">בחר שורה לצפייה בפרשנות</span></div>
    <template v-else>
      <div class="body">
        <CommentaryTreePanel v-if="treeVisible" class="tree-panel"
          :groups="props.groups"
          :selected-book-id="activeBookId"
          :suppress-scroll="suppressTreeScroll"
          @select="onTreeSelect" />
        <div class="content-col">
          <div ref="scrollerEl" class="scroller" tabindex="0" :style="{ fontSize: `${zoom / 100 * 15}px` }" @scroll="onScroll" @contextmenu="contextMenuRef?.show($event)">
            <CommentaryHeaderNav v-if="activeHeader" class="sticky-nav"
              :groups="props.groups"
              :scroll-to-group="scrollToGroup"
              :book-title="activeHeader.bookTitle"
              :active-book-id="activeBookId"
              :tree-visible="treeVisible"
              @update:active-book-id="() => {}"
              @navigate-section="(d, id) => emit('navigate-section', d, id)"
              @toggle-search="emit('toggle-search')"
              @close="emit('close')"
              @toggle-tree="treeVisible = !treeVisible" />
            <div :style="{ height: `${totalSize}px`, position: 'relative' }">
              <div v-for="vItem in virtualItems" :key="String(vItem.key)"
                :ref="el => el && virtualizer.measureElement(el as Element)"
                :data-index="vItem.index"
                :style="{ position: 'absolute', top: 0, right: 0, left: 0, transform: `translateY(${vItem.start}px)` }">
                <CommentaryHeader v-if="flatItems[vItem.index]?.type === 'header'"
                  :book-title="(flatItems[vItem.index] as any).bookTitle"
                  :section-label="(flatItems[vItem.index] as any).sectionLabel"
                  :groups="props.groups"
                  @navigate-section="(d, id) => emit('navigate-section', d, id)"
                  @open-book="(bookId, lineIndex) => emit('open-book', bookId, lineIndex)" />
                <div v-else class="line" v-html="renderContent((flatItems[vItem.index] as any).content, vItem.index)" />
              </div>
            </div>
          </div>
        </div>
      </div>
    </template>
  </div>
</template>

<style scoped>
.commentary-view { height: 100%; display: flex; flex-direction: column; overflow: hidden; }
.state-overlay { flex: 1; display: flex; align-items: center; justify-content: center; }
.hint { font-size: 13px; color: var(--text-secondary); }
.body { flex: 1; display: flex; flex-direction: row; min-height: 0; }
.tree-panel { width: max-content; max-width: 35%; flex-shrink: 0; border-inline-start: 1px solid var(--border-color); }
.content-col { flex: 1; display: flex; flex-direction: column; min-width: 0; position: relative; }
.sticky-nav { position: sticky; top: 0; z-index: 2; height: 32px; }
.scroller { flex: 1; overflow-y: auto; }
.line { padding-inline: 12px; padding-block: 2px; font-size: 1em; line-height: 1.7; color: var(--text-primary); text-align: justify; }
.line :deep(mark.search-match) { background: rgba(255, 165, 0, 0.4); color: inherit; border-radius: 2px; }
.line :deep(mark.search-match.current) { background: rgba(255, 165, 0, 0.9); color: #000; }
</style>
