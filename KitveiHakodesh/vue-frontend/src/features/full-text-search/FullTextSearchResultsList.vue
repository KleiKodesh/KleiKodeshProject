<script setup lang="ts">
import { ref, computed, watch, onBeforeUnmount, nextTick } from 'vue'
import { useVirtualizer } from '@tanstack/vue-virtual'
import { IconSearchSparkle24Regular } from '@iconify-prerendered/vue-fluent'
import { useSettingsStore } from '@/stores/settingsStore'
import { useEventListener } from '@vueuse/core'
import { censorDivineNames } from '@/utils/censorDivineNames'
import { useVirtualScrollerKeys } from '@/composables/useVirtualScrollerKeys'
import type { FullTextSearchResult, SearchFailReason } from './fullTextSearchTypes'

const props = defineProps<{
  results: FullTextSearchResult[]
  totalResults: number
  searchQuery: string
  isSearching: boolean
  hasSearched: boolean
  searchError?: SearchFailReason | null
  dbNotFound?: boolean
  isIndexingReady?: boolean
  initialScrollIndex?: number
  initialScrollOffset?: number
  zoom?: number
}>()

const emit = defineEmits<{
  resultClick: [FullTextSearchResult]
  saveScroll: [{ scrollIndex: number; scrollOffset: number }]
}>()

const SEARCH_ERROR_MESSAGES: Record<string, string> = {
  indexNotReady: 'האינדקס עדיין לא מוכן לחיפוש',
  indexMerging:  'האינדקס מבצע מיזוג — נסה שוב בעוד כמה רגעים',
  searchFailed:  'אירעה שגיאה בחיפוש',
}
const settingsStore = useSettingsStore()
const scrollEl = ref<HTMLElement | null>(null)

const fontPx = computed(() => {
  const zoomFactor = (props.zoom ?? 100) / 100
  return zoomFactor * (settingsStore.fontSize / 100) * 15
})
let programmaticScrolling = false

const virtualizer = useVirtualizer(
  computed(() => ({
    count: props.results.length,
    getScrollElement: () => scrollEl.value,
    estimateSize: () => 80,
    overscan: 8,
    getItemKey: (index) => props.results[index]?.lineId ?? index,
    measureElement: (el) => el.getBoundingClientRect().height,
  })),
)

function renderSnippet(snippet: string): string {
  if (!snippet) return snippet
  return settingsStore.censorDivineNames ? censorDivineNames(snippet) : snippet
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
  // Restore scroll once results are populated — don't gate on isSearching because
  // loadCachedResults can set isSearching=true while simultaneously populating results
  // (partial cache + resume stream). We restore as soon as we have results to scroll into.
  const stopWatch = watch(
    () => props.results.length,
    (len) => {
      if (!len) return
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
  if (!pos) return
  emit('saveScroll', pos)
}

useEventListener(document, 'visibilitychange', () => {
  if (document.visibilityState === 'hidden') savePos()
})
useEventListener(window, 'beforeunload', savePos)
onBeforeUnmount(() => {
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

defineExpose({ captureScrollPos })
</script>

<template>
  <div class="results-wrap">
    <div v-if="dbNotFound || !hasSearched || (!results.length && !isSearching)" class="empty-state">
      <IconSearchSparkle24Regular class="empty-icon" />
      <span v-if="dbNotFound" class="empty-msg error-msg">מסד הנתונים לא נמצא — בחר קובץ מסד נתונים בהגדרות</span>
      <span v-else-if="searchError" class="empty-msg error-msg">
        {{ SEARCH_ERROR_MESSAGES[searchError] ?? SEARCH_ERROR_MESSAGES.searchFailed }}
      </span>
      <span v-else-if="hasSearched && !results.length" class="empty-msg">לא נמצאו תוצאות</span>
      <span v-else-if="isIndexingReady === false" class="empty-msg">האינדקס בהכנה — אנא המתן</span>
    </div>
    <template v-else>
      <div class="results-count">
        <span v-if="isSearching" class="count-searching">
          {{ results.length.toLocaleString() }} תוצאות עד כה...
        </span>
        <span v-else-if="results.length < totalResults">
          {{ results.length.toLocaleString() }} מתוך {{ totalResults.toLocaleString() }} תוצאות
        </span>
        <span v-else>
          {{ results.length.toLocaleString() }} תוצאות
        </span>
      </div>
      <div ref="scrollEl" class="scroller" tabindex="0" :style="{ fontSize: `${fontPx}px` }" @scroll="onScroll">
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
                      '\nלחץ לניווט למיקום'
                    : results[vRow.index]!.bookTitle + '\nלחץ לניווט למיקום'
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
              <div class="snippet" v-html="renderSnippet(results[vRow.index]!.snippet)" />
            </div>
          </div>
        </div>
      </div>
    </template>
  </div>
</template>

<style scoped>
.results-wrap {
  flex: 1;
  overflow: hidden;
  position: relative;
  display: flex;
  flex-direction: column;
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
.error-msg {
  color: color-mix(in srgb, var(--text-primary) 70%, #e05252);
}
.results-count {
  padding: 4px 14px;
  font-size: 11px;
  color: var(--text-secondary);
  border-bottom: 1px solid var(--border-color);
  direction: rtl;
  flex-shrink: 0;
}
.count-searching {
  opacity: 0.7;
}
.scroller {
  flex: 1;
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
  font-size: 1em;
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
  user-select: text;
}
.sep {
  color: var(--text-secondary);
  font-size: 0.85em;
  flex-shrink: 0;
  user-select: text;
}
.toc-text {
  color: inherit;
  font-size: 0.9em;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  flex-shrink: 2;
  min-width: 0;
  user-select: text;
}
.snippet {
  font-family: var(--text-font);
  font-size: 1em;
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
.snippet :deep(mark) {
  background: transparent;
  color: var(--accent-color);
  font-weight: 600;
  user-select: text;
}
</style>
