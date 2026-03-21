<script setup lang="ts">
import { computed, ref, nextTick } from 'vue'
import { useVirtualizer } from '@tanstack/vue-virtual'
import CommentaryHeader from './CommentaryHeader.vue'
import LoadingAnimation from '@/components/common/LoadingAnimation.vue'
import type { CommentaryGroup } from './useCommentary'
import { useSettingsStore } from '@/stores/settingsStore'
import { applyDiacriticsFilter, removeDiacriticsForSearch } from '@/utils/hebrewTextProcessing'
import { censorDivineNames } from '@/utils/censorDivineNames'

const props = defineProps<{ selectedLineId: number | null; groups: CommentaryGroup[]; loading: boolean; searchQuery?: string; currentMatchFlatIndex?: number; currentMatchOccurrence?: number }>()
const emit = defineEmits<{ close: [] }>()

const settingsStore = useSettingsStore()
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

type FlatItem = { type: 'header'; bookTitle: string; connectionTypes: string[] } | { type: 'line'; content: string; lineId: number }

const flatItems = computed<FlatItem[]>(() => {
  const items: FlatItem[] = []
  for (const g of props.groups) {
    items.push({ type: 'header', bookTitle: g.bookTitle, connectionTypes: g.connectionTypes })
    for (const l of g.lines) items.push({ type: 'line', content: l.content, lineId: l.lineId })
  }
  return items
})

const scrollerEl = ref<HTMLElement | null>(null)
const scrollTop = ref(0)

const virtualizer = useVirtualizer(computed(() => ({
  count: flatItems.value.length,
  getScrollElement: () => scrollerEl.value,
  estimateSize: i => flatItems.value[i]?.type === 'header' ? 40 : 48,
  overscan: 10,
})))

const virtualItems = computed(() => virtualizer.value.getVirtualItems())
const totalSize = computed(() => virtualizer.value.getTotalSize())

const stickyHeader = computed(() => {
  let active: (FlatItem & { type: 'header' }) | null = null
  for (const m of virtualizer.value.measurementsCache) {
    const item = flatItems.value[m.index]
    if (item?.type !== 'header') continue
    if (m.start <= scrollTop.value) active = item as FlatItem & { type: 'header' }
    else break
  }
  return active
})

function onScroll() { scrollTop.value = scrollerEl.value?.scrollTop ?? 0 }

function scrollToGroup(bookId: number) {
  const idx = flatItems.value.findIndex(item => item.type === 'header' && props.groups.find(g => g.bookId === bookId && g.bookTitle === item.bookTitle))
  if (idx !== -1) virtualizer.value.scrollToIndex(idx, { align: 'start' })
}

function scrollToFlatIndex(flatIndex: number) {
  virtualizer.value.scrollToIndex(flatIndex, { align: 'nearest' as any })
  requestAnimationFrame(() => {
    if (scrollerEl.value) scrollerEl.value.scrollTop -= 52
  })
}

defineExpose({ scrollToGroup, scrollToFlatIndex })
</script>

<template>
  <div class="commentary-view">
    <div v-if="props.loading" class="state-overlay"><LoadingAnimation /></div>
    <div v-else-if="!flatItems.length" class="state-overlay"><span class="hint">בחר שורה לצפייה בפרשנות</span></div>
    <template v-else>
      <CommentaryHeader v-if="stickyHeader" class="sticky-header"
        :book-title="stickyHeader.bookTitle" :connection-types="stickyHeader.connectionTypes"
        :groups="props.groups" :scroll-to-group="scrollToGroup" :is-sticky="true" @close="emit('close')" />
      <div ref="scrollerEl" class="scroller" @scroll="onScroll">
        <div :style="{ height: `${totalSize}px`, position: 'relative' }">
          <div v-for="vItem in virtualItems" :key="String(vItem.key)"
            :ref="el => el && virtualizer.measureElement(el as Element)"
            :data-index="vItem.index"
            :style="{ position: 'absolute', top: 0, right: 0, left: 0, transform: `translateY(${vItem.start}px)` }">
            <CommentaryHeader v-if="flatItems[vItem.index]?.type === 'header'"
              :book-title="(flatItems[vItem.index] as any).bookTitle"
              :connection-types="(flatItems[vItem.index] as any).connectionTypes"
              :groups="props.groups" :scroll-to-group="scrollToGroup" />
            <div v-else class="line" v-html="renderContent((flatItems[vItem.index] as any).content, vItem.index)" />
          </div>
        </div>
      </div>
    </template>
  </div>
</template>

<style scoped>
.commentary-view { height: 100%; display: flex; flex-direction: column; overflow: hidden; position: relative; }
.state-overlay { flex: 1; display: flex; align-items: center; justify-content: center; }
.hint { font-size: 13px; color: var(--text-secondary); }
.sticky-header { position: absolute; top: 0; left: 16px; right: 0; z-index: 2; }
.scroller { flex: 1; overflow-y: auto; }
.line { padding-inline: 12px; padding-block: 2px; font-size: 15px; line-height: 1.7; color: var(--text-primary); text-align: justify; user-select: text; }
.line :deep(mark.search-match) { background: rgba(255, 165, 0, 0.4); color: inherit; border-radius: 2px; }
.line :deep(mark.search-match.current) { background: rgba(255, 165, 0, 0.9); color: #000; }
</style>
