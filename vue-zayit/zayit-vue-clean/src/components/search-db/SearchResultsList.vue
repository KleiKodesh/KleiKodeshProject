<script setup lang="ts">
import { ref, computed, watch, onBeforeUnmount, nextTick } from 'vue'
import { useVirtualizer } from '@tanstack/vue-virtual'
import { IconSearchSparkle24 } from '@iconify-prerendered/vue-fluent-color'
import { useSettingsStore } from '@/stores/settingsStore'
import { useTabStore } from '@/stores/tabStore'
import { useEventListener } from '@vueuse/core'
import { censorDivineNames } from '@/utils/censorDivineNames'
import { useVirtualScrollerKeys } from '@/composables/useVirtualScrollerKeys'
import type { BloomSearchResult } from './searchTypes'

const props = defineProps<{
  results: BloomSearchResult[]
  searchQuery: string
  isSearching: boolean
  hasSearched: boolean
  initialScrollIndex?: number
  initialScrollOffset?: number
}>()

const emit = defineEmits<{ resultClick: [BloomSearchResult]; scrolled: [number] }>()

const settingsStore = useSettingsStore()
const tabStore = useTabStore()
const tabId = tabStore.activeTabId
const scrollEl = ref<HTMLElement | null>(null)
let programmaticScrolling = false

const virtualizer = useVirtualizer(
  computed(() => ({
    count: props.results.length,
    getScrollElement: () => scrollEl.value,
    estimateSize: () => 80,
    overscan: 8,
    measureElement: (el) => el.getBoundingClientRect().height,
  })),
)

function highlight(snippet: string): string {
  if (!props.searchQuery || !snippet) return snippet
  let text = settingsStore.censorDivineNames ? censorDivineNames(snippet) : snippet
  for (const term of props.searchQuery.trim().split(/\s+/).filter(Boolean)) {
    const esc = term.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
    text = text.replace(new RegExp(`(${esc})`, 'gi'), '<span class="match">$1</span>')
  }
  return text
}

function captureScrollPos() {
  const first = virtualizer.value.getVirtualItems()[0]
  if (!first || !scrollEl.value) return null
  return {
    scrollIndex: first.index,
    scrollOffset: Math.max(0, scrollEl.value.scrollTop - first.start),
  }
}

function restoreScrollPos(scrollIndex: number, scrollOffset: number) {
  // Two-rAF pattern: scrollToIndex triggers TanStack's internal correction.
  // Wait one rAF for it to settle, then set scrollTop directly — TanStack is idle by then.
  programmaticScrolling = true
  virtualizer.value.scrollToIndex(scrollIndex, { align: 'start' })
  requestAnimationFrame(() => {
    const item = virtualizer.value.measurementsCache.find((m) => m.index === scrollIndex)
    if (item && scrollEl.value) scrollEl.value.scrollTop = item.start + scrollOffset
    requestAnimationFrame(() => {
      programmaticScrolling = false
    })
  })
}

{
  const stopWatch = watch(
    () => props.results.length,
    (len) => {
      if (!len || props.isSearching) return
      if (props.initialScrollIndex == null) {
        stopWatch()
        return
      }
      stopWatch()
      nextTick(() => restoreScrollPos(props.initialScrollIndex!, props.initialScrollOffset ?? 0))
    },
    { flush: 'post', immediate: true },
  )
}

function savePos() {
  if (programmaticScrolling) return
  const pos = captureScrollPos()
  if (pos)
    tabStore.setTabViewState(tabId, {
      searchScrollIndex: pos.scrollIndex,
      searchScrollOffset: pos.scrollOffset,
    })
}

useEventListener(document, 'visibilitychange', () => {
  if (document.visibilityState === 'hidden') savePos()
})
useEventListener(window, 'beforeunload', savePos)
onBeforeUnmount(() => {
  // Force-clear the programmatic flag so savePos is never silently skipped at unmount.
  programmaticScrolling = false
  savePos()
})

useVirtualScrollerKeys(
  scrollEl,
  () =>
    virtualizer.value as unknown as import('@tanstack/vue-virtual').Virtualizer<Element, Element>,
  () => props.results.length,
)

function onScroll() {}
</script>

<template>
  <div class="results-wrap">
    <div v-if="!hasSearched || (!results.length && !isSearching)" class="empty-state">
      <IconSearchSparkle24 class="empty-icon" />
      <span v-if="hasSearched && !results.length" class="empty-msg">לא נמצאו תוצאות</span>
    </div>
    <div v-else ref="scrollEl" class="scroller" tabindex="0" @scroll="onScroll">
      <div :style="{ height: `${virtualizer.getTotalSize()}px`, position: 'relative' }">
        <div
          v-for="vRow in virtualizer.getVirtualItems()"
          :key="String(vRow.key)"
          :ref="(el) => el && virtualizer.measureElement(el as Element)"
          :data-index="vRow.index"
          :style="{
            position: 'absolute',
            top: 0,
            left: 0,
            right: 0,
            transform: `translateY(${vRow.start}px)`,
          }"
        >
          <div class="result-item">
            <div
              class="result-header"
              :title="
                results[vRow.index]!.tocText
                  ? results[vRow.index]!.bookTitle +
                    ' › ' +
                    results[vRow.index]!.tocText +
                    '\n\nלחץ לניווט למיקום'
                  : results[vRow.index]!.bookTitle + '\n\nלחץ לניווט למיקום'
              "
              @click="emit('resultClick', results[vRow.index]!)"
            >
              <span class="book-title">{{ results[vRow.index]!.bookTitle }}</span>
              <span v-if="results[vRow.index]!.tocText" class="sep">›</span>
              <span v-if="results[vRow.index]!.tocText" class="toc-text">{{
                results[vRow.index]!.tocText
              }}</span>
            </div>
            <!-- eslint-disable-next-line vue/no-v-html -->
            <div class="snippet" v-html="highlight(results[vRow.index]!.snippet)" />
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.results-wrap {
  flex: 1;
  overflow: hidden;
  position: relative;
}
.empty-state {
  height: 100%;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 12px;
}
.empty-icon {
  width: 56px;
  height: 56px;
  opacity: 0.25;
}
.empty-msg {
  font-size: 14px;
  color: var(--text-secondary);
}
.scroller {
  height: 100%;
  overflow-y: auto;
  overflow-x: hidden;
  outline: none;
}
.result-item {
  padding: 8px 14px;
  border-bottom: 1px solid var(--border-color);
}
.result-header {
  display: flex;
  align-items: center;
  gap: 5px;
  margin-bottom: 4px;
  font-family: var(--header-font);
  font-weight: 500;
  font-size: 13px;
  min-width: 0;
  overflow: hidden;
  user-select: text;
  color: var(--accent-color);
  transition: color 120ms;
}
.result-header:hover {
  color: color-mix(in srgb, var(--accent-color) 60%, white);
  cursor: pointer;
}
.book-title {
  color: inherit;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  flex-shrink: 1;
  min-width: 0;
}
.sep {
  color: var(--text-secondary);
  font-size: 11px;
  flex-shrink: 0;
}
.toc-text {
  color: inherit;
  font-size: 12px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  flex-shrink: 2;
  min-width: 0;
}
.snippet {
  font-family: var(--text-font);
  font-size: var(--font-size, 100%);
  line-height: var(--line-height, 1.5);
  color: var(--text-secondary);
  direction: rtl;
  text-align: justify;
  user-select: text;
}
.snippet :deep(.match) {
  color: var(--accent-color);
  font-weight: 600;
  user-select: text;
}
</style>
