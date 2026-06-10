<script setup lang="ts">
import { ref, computed, watch, nextTick } from 'vue'
import { useVirtualizer } from '@tanstack/vue-virtual'
import { IconSearchSparkle24Regular } from '@iconify-prerendered/vue-fluent'
import { useSettingsStore } from '@/stores/settingsStore'
import { useDebounceFn } from '@vueuse/core'
import { censorDivineNames } from '@/utils/censorDivineNames'
import { useVirtualScrollerKeys } from '@/composables/useVirtualScrollerKeys'
import type { FullTextSearchResult, SearchFailReason } from './fullTextSearchTypes'

const props = defineProps<{
  results: FullTextSearchResult[]
  searchQuery: string
  isSearching: boolean
  hasSearched: boolean
  searchError?: SearchFailReason | null
  dbNotFound?: boolean
  zoom?: number
  fetchSnippetsForWindow: (lineIds: number[]) => Promise<Map<number, {
    snippet: string
    score: number
    matchedTerms: string[]
    tocText: string
    isWeakMatch: boolean
    bookTitle: string
  }>>
}>()

const emit = defineEmits<{
  resultClick: [FullTextSearchResult]
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

// ── Snippet cache — in-memory only ────────────────────────────────────────────
const snippetCache = new Map<number, { snippet: string; score: number; matchedTerms: string[]; tocText: string; isWeakMatch: boolean; bookTitle: string }>()
const snippetVersion = ref(0)

watch(() => props.results, () => {
  snippetCache.clear()
  snippetVersion.value++
})

// ── Virtualizer ───────────────────────────────────────────────────────────────

const virtualizer = useVirtualizer(
  computed(() => ({
    count: props.results.length,
    getScrollElement: () => scrollEl.value,
    estimateSize: () => 80,
    overscan: 5,
    getItemKey: (index) => props.results[index]?.lineId ?? index,
    measureElement: (el) => el.getBoundingClientRect().height,
  })),
)

// ── Viewport-driven snippet fetching ─────────────────────────────────────────

const FETCH_OVERSCAN = 10

async function fetchVisibleSnippets() {
  const items = virtualizer.value.getVirtualItems()
  if (!items.length) return

  const firstIndex = Math.max(0, items[0]!.index - FETCH_OVERSCAN)
  const lastIndex  = Math.min(props.results.length - 1, items[items.length - 1]!.index + FETCH_OVERSCAN)

  const missingIds: number[] = []
  for (let i = firstIndex; i <= lastIndex; i++) {
    const lineId = props.results[i]?.lineId
    if (lineId != null && !snippetCache.has(lineId)) missingIds.push(lineId)
  }

  if (!missingIds.length) return

  for (const lineId of missingIds) snippetCache.set(lineId, null as any)

  try {
    const fetched = await props.fetchSnippetsForWindow(missingIds)
    for (const [lineId, data] of fetched) snippetCache.set(lineId, data)
    snippetVersion.value++
  } catch {
    for (const lineId of missingIds) {
      if ((snippetCache.get(lineId) as any) === null) snippetCache.delete(lineId)
    }
  }
}

const fetchVisibleSnippetsDebounced = useDebounceFn(fetchVisibleSnippets, 150)

function onScroll() {
  fetchVisibleSnippetsDebounced()
}

watch(
  () => props.results.length,
  (len) => { if (len) nextTick(fetchVisibleSnippets) },
  { flush: 'post' }
)

function renderSnippet(snippet: string): string {
  if (!snippet) return ''
  return settingsStore.censorDivineNames ? censorDivineNames(snippet) : snippet
}

useVirtualScrollerKeys(
  scrollEl,
  () => virtualizer.value as unknown as import('@tanstack/vue-virtual').Virtualizer<Element, Element>,
  () => props.results.length,
)

function captureScrollPos() {
  const first = virtualizer.value.getVirtualItems()[0]
  if (!first || !scrollEl.value) return null
  return {
    scrollIndex: first.index,
    scrollOffset: Math.max(0, scrollEl.value.scrollTop - first.start),
  }
}

defineExpose({ captureScrollPos })
</script>

<template>
  <div class="results-wrap" :data-version="snippetVersion">
    <div v-if="dbNotFound || !hasSearched || (!results.length && !isSearching)" class="empty-state">
      <IconSearchSparkle24Regular class="empty-icon" />
      <span v-if="dbNotFound" class="empty-msg error-msg">מסד הנתונים לא נמצא — בחר קובץ מסד נתונים בהגדרות</span>
      <span v-else-if="searchError" class="empty-msg error-msg">
        {{ SEARCH_ERROR_MESSAGES[searchError] ?? SEARCH_ERROR_MESSAGES.searchFailed }}
      </span>
      <span v-else-if="hasSearched && !results.length" class="empty-msg">לא נמצאו תוצאות</span>
    </div>
    <template v-else>
      <div
        ref="scrollEl"
        class="scroller"
        tabindex="0"
        :style="{ fontSize: `${fontPx}px` }"
        @scroll="onScroll"
      >
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
            <div
              v-if="!snippetCache.has(results[vRow.index]!.lineId) || (snippetCache.get(results[vRow.index]!.lineId)?.snippet && !snippetCache.get(results[vRow.index]!.lineId)?.isWeakMatch)"
              class="result-item"
            >
              <!-- While snippet not yet loaded: show shimmer for header + body -->
              <template v-if="!snippetCache.has(results[vRow.index]!.lineId)">
                <div class="placeholder-header">
                  <div class="placeholder-line" style="width: 45%" />
                  <div class="placeholder-line" style="width: 30%" />
                </div>
                <div class="snippet-placeholder">
                  <div class="placeholder-line" style="width: 92%" />
                  <div class="placeholder-line" style="width: 60%" />
                </div>
              </template>

              <!-- Snippet loaded -->
              <template v-else-if="snippetCache.get(results[vRow.index]!.lineId)?.snippet && !snippetCache.get(results[vRow.index]!.lineId)?.isWeakMatch">
                <div
                  class="result-header"
                  :title="snippetCache.get(results[vRow.index]!.lineId)!.bookTitle + '\nלחץ לניווט למיקום'"
                  @click="emit('resultClick', {
                    ...results[vRow.index]!,
                    bookTitle: snippetCache.get(results[vRow.index]!.lineId)?.bookTitle ?? '',
                    tocText: snippetCache.get(results[vRow.index]!.lineId)?.tocText ?? '',
                    snippet: snippetCache.get(results[vRow.index]!.lineId)?.snippet ?? '',
                    matchedTerms: snippetCache.get(results[vRow.index]!.lineId)?.matchedTerms ?? [],
                  })"
                >
                  <span class="book-title">{{ snippetCache.get(results[vRow.index]!.lineId)!.bookTitle }}</span>
                  <template v-if="snippetCache.get(results[vRow.index]!.lineId)?.tocText">
                    <span class="sep">›</span>
                    <span class="toc-text">{{ snippetCache.get(results[vRow.index]!.lineId)!.tocText }}</span>
                  </template>
                </div>
                <div
                  class="snippet"
                  v-html="renderSnippet(snippetCache.get(results[vRow.index]!.lineId)!.snippet)"
                />
              </template>
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
.scroller {
  flex: 1;
  overflow-y: auto;
  overflow-x: hidden;
  outline: none;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
}
.result-item {
  padding: 8px 14px;
  border-bottom: 1px solid var(--border-color);
}
.placeholder-header {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 6px;
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
  cursor: pointer;
}
.result-header:hover {
  color: color-mix(in srgb, var(--accent-color) 60%, white);
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
.snippet :deep(.match),
.snippet :deep(mark) {
  background: transparent;
  color: var(--accent-color);
  font-weight: 600;
  user-select: text;
}
.weak-snippet {
  opacity: 0.6;
}
.snippet-placeholder {
  display: flex;
  flex-direction: column;
  gap: 5px;
  padding-block: 2px;
}
.placeholder-line {
  height: 10px;
  border-radius: 4px;
  background: color-mix(in srgb, var(--text-secondary) 12%, transparent);
  animation: shimmer 1.4s ease-in-out infinite;
}
@keyframes shimmer {
  0%, 100% { opacity: 0.5; }
  50%       { opacity: 1;   }
}
</style>
