<script setup lang="ts">
import { computed, ref, watch, nextTick, onBeforeUnmount } from 'vue'
import { useVirtualizer } from '@tanstack/vue-virtual'
import { useTabStore } from '@/stores/tabStore'
import { useLines } from './useLines'
import LoadingAnimation from '@/components/common/LoadingAnimation.vue'

const emit = defineEmits<{ scrolled: [lineIndex: number] }>()
const props = defineProps<{ altTocLabelMap?: Map<number, string> }>()

const tabStore = useTabStore()
const tabId = tabStore.activeTabId.value
const bookId = tabStore.activeTab.bookId!
const { lines, loading, prioritise } = useLines(() => bookId)

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
    if (saved) await restoreScrollPos(saved.scrollIndex, saved.scrollOffset)
  }
})

let saveTimer: ReturnType<typeof setTimeout> | null = null

function onScroll() {
  const scroller = scrollerEl.value
  const first = virtualItems.value[0]?.index ?? 0
  prioritise(first)
  if (scroller && !restoring.value) {
    const center = scroller.scrollTop + scroller.clientHeight / 2
    const mid = virtualItems.value.reduce((best, item) =>
      Math.abs(item.start + item.size / 2 - center) < Math.abs(best.start + best.size / 2 - center) ? item : best
    , virtualItems.value[0]!)
    emit('scrolled', mid.index)
  }
  if (saveTimer) clearTimeout(saveTimer)
  saveTimer = setTimeout(() => {
    const pos = captureScrollPos()
    if (pos) tabStore.setBookViewState(tabId, bookId, pos)
  }, 100)
}

function scrollToLineId(lineId: number) {
  const lineIndex = lines.value.find(l => l.id === lineId)?.lineIndex
  if (lineIndex == null) return
  prioritise(lineIndex)
  virtualizer.value.scrollToIndex(lineIndex, { align: 'start' })
}

onBeforeUnmount(() => {
  if (saveTimer) clearTimeout(saveTimer)
  const pos = captureScrollPos()
  if (pos) tabStore.setBookViewState(tabId, bookId, pos)
  else tabStore.clearBookViewState(tabId, bookId)
})

defineExpose({ scrollToLineId })
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
            :data-alt-toc="props.altTocLabelMap?.get(vItem.index)"
            v-html="lines[vItem.index]?.content" />
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
.line { padding-inline: 12px; font-size: 15px; line-height: 1.7; color: var(--text-primary); text-align: justify; }
.line[data-alt-toc]::before { content: attr(data-alt-toc); display: block; font-size: 0.85rem; font-weight: 600; opacity: 0.35; padding-block-end: 2px; }
.line.placeholder { height: 28px; margin-inline: 12px; margin-block: 4px; border-radius: 4px; background: color-mix(in srgb, var(--text-primary) 5%, transparent); }
</style>
