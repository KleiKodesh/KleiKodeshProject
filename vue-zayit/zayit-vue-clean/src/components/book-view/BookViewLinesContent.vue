<script setup lang="ts">
import { computed, ref, watch, nextTick, onBeforeUnmount } from 'vue'
import { useVirtualizer } from '@tanstack/vue-virtual'
import { useTabStore } from '@/stores/tabStore'
import { useSettingsStore } from '@/stores/settingsStore'
import { useLines } from './useLines'
import { applyDiacriticsFilter, removeDiacriticsForSearch } from '@/utils/hebrewTextProcessing'
import { censorDivineNames } from '@/utils/censorDivineNames'
import LoadingAnimation from '@/components/common/LoadingAnimation.vue'

const emit = defineEmits<{ scrolled: [lineIndex: number]; lineSelected: [lineId: number] }>()
const props = defineProps<{
  altTocLabelMap?: Map<number, string>
  selectedLineId?: number | null
  bottomVisible?: boolean
  searchQuery?: string
}>()

const tabStore = useTabStore()
const settingsStore = useSettingsStore()
const tabId = tabStore.activeTabId
const bookId = tabStore.activeTab.bookId!
const { lines, loading, prioritise } = useLines(() => bookId)

const diacriticsState = computed(() => settingsStore.diacriticsState)

function lineContent(raw: string | null, lineIndex: number): string | null {
  if (raw === null) return null
  let content = diacriticsState.value === 0 ? raw : applyDiacriticsFilter(raw, diacriticsState.value)
  if (settingsStore.censorDivineNames) content = censorDivineNames(content)
  if (props.searchQuery?.trim()) content = highlightMatches(raw, content, props.searchQuery)
  return content
}

function highlightMatches(raw: string, content: string, query: string): string {
  const q = removeDiacriticsForSearch(query.trim())
  if (!q) return content

  const result: string[] = []
  let strippedPos = 0
  let inTag = false
  let matchStrippedCount = 0
  let inMatch = false

  // Find all match start positions in stripped space
  const stripped = removeDiacriticsForSearch(content.replace(/<[^>]*>/g, ''))
  const matchStarts = new Set<number>()
  let idx = 0
  while ((idx = stripped.indexOf(q, idx)) !== -1) { matchStarts.add(idx); idx++ }
  if (!matchStarts.size) return content

  for (let i = 0; i < content.length; i++) {
    const ch = content[i]!
    if (ch === '<') { inTag = true; result.push(ch); continue }
    if (ch === '>') { inTag = false; result.push(ch); continue }
    if (inTag) { result.push(ch); continue }

    const isDiacritic = /[\u0591-\u05C7]/.test(ch)

    if (!isDiacritic && matchStarts.has(strippedPos) && !inMatch) {
      result.push('<mark class="search-match">')
      inMatch = true
      matchStrippedCount = 0
    }

    result.push(ch)

    if (!isDiacritic) {
      if (inMatch) {
        matchStrippedCount++
        if (matchStrippedCount === q.length) {
          result.push('</mark>')
          inMatch = false
        }
      }
      strippedPos++
    }
  }

  return result.join('')
}

const scrollerEl = ref<HTMLElement | null>(null)
const restoring = ref(false)

const virtualizer = useVirtualizer(computed(() => ({
  count: lines.value.length,
  getScrollElement: () => scrollerEl.value,
  estimateSize: () => 32,
  overscan: 10,
})))

const virtualItems = computed(() => virtualizer.value.getVirtualItems())
const totalSize = computed(() => virtualizer.value.getTotalSize())

function captureScrollPos() {
  const first = virtualItems.value[0]
  if (!first || !scrollerEl.value) return null
  return { scrollIndex: first.index, scrollOffset: scrollerEl.value.scrollTop - first.start }
}

async function restoreScrollPos(scrollIndex: number, scrollOffset: number) {
  restoring.value = true
  try {
    prioritise(scrollIndex)
    virtualizer.value.scrollToIndex(scrollIndex, { align: 'start' })
    await nextTick()
    await new Promise(r => setTimeout(r, 500))
    const item = virtualizer.value.getVirtualItems().find(v => v.index === scrollIndex)
    if (scrollerEl.value) scrollerEl.value.scrollTop = (item?.start ?? scrollerEl.value?.scrollTop ?? 0) + scrollOffset
    await new Promise(r => setTimeout(r, 600))
  } finally {
    restoring.value = false
  }
}

watch(loading, async (val) => {
  if (!val && lines.value.length > 0) {
    const saved = await tabStore.getBookViewState(tabId, bookId)
    if (saved) {
      if (saved.selectedLineId != null) {
        // Prioritise scrolling to the selected line
        const lineIndex = lines.value.find(l => l.id === saved.selectedLineId)?.lineIndex
        if (lineIndex != null) {
          await restoreScrollPos(lineIndex, 0)
        } else {
          await restoreScrollPos(saved.scrollIndex, saved.scrollOffset)
        }
      } else {
        await restoreScrollPos(saved.scrollIndex, saved.scrollOffset)
      }
    }
  }
})

let saveTimer: ReturnType<typeof setTimeout> | null = null

function onScroll() {
  const scroller = scrollerEl.value
  const first = virtualItems.value[0]?.index ?? 0
  prioritise(first)
  if (scroller && !restoring.value && !programmaticScrolling) {
    const center = scroller.scrollTop + scroller.clientHeight / 2
    const mid = virtualItems.value.reduce((best, item) =>
      Math.abs(item.start + item.size / 2 - center) < Math.abs(best.start + best.size / 2 - center) ? item : best
    , virtualItems.value[0]!)
    emit('scrolled', mid.index)
  }
  if (saveTimer) clearTimeout(saveTimer)
  saveTimer = setTimeout(() => {
    const pos = captureScrollPos()
    if (pos) tabStore.setBookViewState(tabId, bookId, { ...pos, selectedLineId: props.selectedLineId })
  }, 100)
}

let programmaticScrollTimer: ReturnType<typeof setTimeout> | null = null
let programmaticScrolling = false

function scrollToLineId(lineId: number) {
  const lineIndex = lines.value.find(l => l.id === lineId)?.lineIndex
  if (lineIndex == null) return
  programmaticScrolling = true
  if (programmaticScrollTimer) clearTimeout(programmaticScrollTimer)
  prioritise(lineIndex)
  virtualizer.value.scrollToIndex(lineIndex, { align: 'start' })
  programmaticScrollTimer = setTimeout(() => { programmaticScrolling = false }, 300)
}

onBeforeUnmount(() => {
  if (saveTimer) clearTimeout(saveTimer)
  if (programmaticScrollTimer) clearTimeout(programmaticScrollTimer)
  const pos = captureScrollPos()
  if (pos) tabStore.setBookViewState(tabId, bookId, { ...pos, selectedLineId: props.selectedLineId })
  else tabStore.clearBookViewState(tabId, bookId)
})

defineExpose({ scrollToLineId, scrollToLineIndex })

function scrollToLineIndex(lineIndex: number) {
  programmaticScrolling = true
  if (programmaticScrollTimer) clearTimeout(programmaticScrollTimer)
  prioritise(lineIndex)
  virtualizer.value.scrollToIndex(lineIndex, { align: 'nearest' as any })
  programmaticScrollTimer = setTimeout(() => { programmaticScrolling = false }, 300)
}
</script>

<template>
  <div class="lines-content">
    <div v-if="loading || restoring" class="loading-overlay"><LoadingAnimation /></div>
    <div ref="scrollerEl" class="scroller" @scroll="onScroll">
      <div :style="{ height: `${totalSize}px`, position: 'relative' }">
        <div
          v-for="vItem in virtualItems"
          :key="String(vItem.key)"
          :ref="el => el && virtualizer.measureElement(el as Element)"
          :data-index="vItem.index"
          :style="{ position: 'absolute', top: 0, right: 0, left: 0, transform: `translateY(${vItem.start}px)` }"
        >
          <div v-if="lines[vItem.index]?.content !== null" class="line"
            :class="{ selected: props.bottomVisible && selectedLineId === lines[vItem.index]?.id }"
            :data-alt-toc="props.altTocLabelMap?.get(vItem.index)"
            v-html="lineContent(lines[vItem.index]?.content ?? null, vItem.index)"
            @click="props.bottomVisible && lines[vItem.index] && lines[vItem.index]!.id > 0 && emit('lineSelected', lines[vItem.index]!.id)" />
          <div v-else class="line placeholder" />
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.lines-content { height: 100%; position: relative; }
.loading-overlay { position: absolute; inset: 0; z-index: 10; background: var(--bg-primary); }
.scroller { height: 100%; overflow-y: auto; }
.line { padding-inline: 12px; font-size: 15px; line-height: 1.7; color: var(--text-primary); text-align: justify; position: relative; }
.line.selected::after { content: ''; position: absolute; top: 0; bottom: 0; right: 4px; width: 3px; background: var(--accent-color); }
.line[data-alt-toc]::before { content: attr(data-alt-toc); display: block; font-size: 0.85rem; font-weight: 600; opacity: 0.35; padding-block-end: 2px; }
.line.placeholder { height: 28px; margin-inline: 12px; margin-block: 4px; border-radius: 4px; background: color-mix(in srgb, var(--text-primary) 5%, transparent); }
.line :deep(mark.search-match) { background: rgba(255, 165, 0, 0.4); color: inherit; border-radius: 2px; }
</style>
