<script setup lang="ts">
import { computed, ref, watch, nextTick, onBeforeUnmount } from 'vue'
import { useVirtualizer } from '@tanstack/vue-virtual'
import { useTabStore } from '@/stores/tabStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useLines } from './useLines'
import { query } from '@/db/db'
import { SQL } from '@/db/queries.sql'

const tabStore = useTabStore()
const bookViewStore = useBookViewStore()
const tabId = tabStore.activeTabId
const bookId = computed(() => tabStore.activeTab.bookId)
const { lines, loading, prioritise } = useLines(() => bookId.value)

const scrollerEl = ref<HTMLElement | null>(null)

const virtualizer = useVirtualizer(computed(() => ({
  count: lines.value.length,
  getScrollElement: () => scrollerEl.value,
  estimateSize: () => 32,
  overscan: 10,
})))

const virtualItems = computed(() => virtualizer.value.getVirtualItems())
const totalSize = computed(() => virtualizer.value.getTotalSize())

watch(virtualItems, () => {
  virtualizer.value.measureElement
})

// ── Scroll persistence ───────────────────────────────────────────────────────

interface ScrollPos { index: number; offset: number }

function captureScrollPos(): ScrollPos | null {
  const scroller = scrollerEl.value
  if (!scroller || virtualItems.value.length === 0) return null
  const first = virtualItems.value[0]!
  // offset = how far the top item has been scrolled past (pixels into the item)
  const offset = scroller.scrollTop - first.start
  return { index: first.index, offset }
}

async function restoreScrollPos(pos: ScrollPos) {
  prioritise(pos.index)

  // Phase 1: scroll the target index into view so tanstack measures it
  virtualizer.value.scrollToIndex(pos.index, { align: 'start' })
  await nextTick()

  // Phase 2: wait for the virtualizer to measure and settle the item heights
  await new Promise(resolve => setTimeout(resolve, 500))

  // Phase 3: read the now-accurate item start offset and apply the sub-item offset on top
  const item = virtualizer.value.getVirtualItems().find(v => v.index === pos.index)
  const itemStart = item?.start ?? virtualizer.value.scrollOffset
  if (scrollerEl.value) scrollerEl.value.scrollTop = itemStart + pos.offset

  await nextTick()
  await new Promise(resolve => setTimeout(resolve, 600))
}

// Restore once lines are loaded and scroller is mounted
watch(loading, async (val) => {
  if (!val && lines.value.length > 0 && bookId.value != null) {
    await nextTick()
    const saved = bookViewStore.getScrollIndex(tabId, bookId.value)
    if (saved) await restoreScrollPos(saved)
  }
})

let saveTimer: ReturnType<typeof setTimeout> | null = null

function onScroll() {
  const first = virtualItems.value[0]?.index ?? 0
  prioritise(first)
  if (bookId.value == null) return
  if (saveTimer) clearTimeout(saveTimer)
  saveTimer = setTimeout(() => {
    const pos = captureScrollPos()
    if (pos) bookViewStore.setScrollIndex(tabId, bookId.value!, pos)
  }, 500)
}

async function scrollToLineId(lineId: number) {
  const [row] = await query<{ lineIndex: number }>(SQL.GET_LINE_BY_ID, [lineId])
  if (row == null) return
  prioritise(row.lineIndex)
  virtualizer.value.scrollToIndex(row.lineIndex, { align: 'start' })
}

// Save position immediately when component unmounts (tab switch / close)
onBeforeUnmount(() => {
  if (saveTimer) clearTimeout(saveTimer)
  const pos = captureScrollPos()
  if (pos && bookId.value != null) bookViewStore.setScrollIndex(tabId, bookId.value, pos)
})

defineExpose({ scrollToLineId })
</script>

<template>
  <div class="lines-content">
    <div v-if="loading" class="state-msg">טוען...</div>
    <div v-else ref="scrollerEl" class="scroller" @scroll="onScroll">
      <div :style="{ height: `${totalSize}px`, position: 'relative' }">
        <div
          v-for="vItem in virtualItems"
          :key="String(vItem.key)"
          :ref="el => el && virtualizer.measureElement(el as Element)"
          :data-index="vItem.index"
          :style="{ position: 'absolute', top: 0, right: 0, left: 0, transform: `translateY(${vItem.start}px)` }"
        >
          <div
            v-if="lines[vItem.index]?.content !== null"
            class="line"
            v-html="lines[vItem.index]?.content"
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
  display: flex;
  flex-direction: column;
}

.state-msg {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 14px;
  color: var(--text-secondary);
}

.scroller {
  flex: 1;
  overflow-y: auto;
  height: 100%;
}

.line {
  padding-inline: 12px;
  font-size: 15px;
  line-height: 1.7;
  color: var(--text-primary);
  text-align: start;
}

.line.placeholder {
  height: 28px;
  margin-inline: 12px;
  margin-block: 4px;
  border-radius: 4px;
  background: color-mix(in srgb, var(--text-primary) 5%, transparent);
}
</style>
