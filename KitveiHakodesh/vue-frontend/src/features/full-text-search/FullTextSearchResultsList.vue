<script setup lang="ts">
import { ref, computed, watch, onBeforeUnmount, nextTick } from 'vue'
import { useVirtualizer } from '@tanstack/vue-virtual'
import { IconSearchSparkle24Regular, IconEye24Regular, IconDismiss24Regular, IconArrowReset24Regular } from '@iconify-prerendered/vue-fluent'
import { useSettingsStore } from '@/stores/settingsStore'
import { useEventListener } from '@vueuse/core'
import { censorDivineNames } from '@/utils/censorDivineNames'
import { useVirtualScrollerKeys } from '@/composables/useVirtualScrollerKeys'
import ContextMenu from '@/components/ContextMenu.vue'
import { useFullTextSearchCopyMenu, useFullTextSearchScopedCopy } from './useFullTextSearchCopyMenu'
import { useFullTextSearchPreview } from './useFullTextSearchPreview'
import FullTextSearchResultPreview from './FullTextSearchResultPreview.vue'
import type { FullTextSearchResult, SearchFailReason } from './fullTextSearchTypes'

const props = defineProps<{
  results: FullTextSearchResult[]
  totalResults: number
  searchQuery: string
  isSearching: boolean
  hasSearched: boolean
  searchError?: SearchFailReason | null
  dbNotFound?: boolean
  initialScrollIndex?: number
  initialScrollOffset?: number
  zoom?: number
}>()

const emit = defineEmits<{
  resultClick: [FullTextSearchResult]
  saveScroll: [{ scrollIndex: number; scrollOffset: number }]
}>()

const SEARCH_ERROR_MESSAGES: Record<string, string> = {
  indexNotReady: 'האינדקס לא מוכן לחיפוש — נסה שוב בעוד כמה רגעים',
  indexMerging:  'האינדקס מבצע מיזוג — נסה שוב בעוד כמה רגעים',
  searchFailed:  'אירעה שגיאה בחיפוש',
}
const settingsStore = useSettingsStore()
const scrollEl = ref<HTMLElement | null>(null)

// Right-click copy menu (העתק / העתק טקסט נקי) — matches the book & txt views.
const contextMenuRef = ref<InstanceType<typeof ContextMenu> | null>(null)
const { items: copyMenuItems } = useFullTextSearchCopyMenu()
useFullTextSearchScopedCopy(scrollEl)

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
  })),
)

function renderSnippet(snippet: string): string {
  if (!snippet) return snippet
  return settingsStore.censorDivineNames ? censorDivineNames(snippet) : snippet
}

// "הצג עוד" — per-result windowed live preview (replaces the clamped snippet).
const { previewOf, togglePreview, loadAbove, loadBelow, clearPreviews } = useFullTextSearchPreview()
watch(
  () => props.searchQuery,
  () => clearPreviews(),
)

// Open previews' component instances, keyed by lineId — the header recenter button
// lives outside the preview component, so it reaches the scroll through this map.
const previewRefs = new Map<number, InstanceType<typeof FullTextSearchResultPreview>>()
function setPreviewRef(lineId: number, el: unknown) {
  if (el) previewRefs.set(lineId, el as InstanceType<typeof FullTextSearchResultPreview>)
  else previewRefs.delete(lineId)
}
function recenterPreview(result: FullTextSearchResult) {
  previewRefs.get(result.lineId)?.recenter()
}

function resultTitle(result: FullTextSearchResult): string {
  const base = result.tocText
    ? `${result.bookTitle} › ${result.tocText}\nלחץ לניווט למיקום`
    : `${result.bookTitle}\nלחץ לניווט למיקום`
  return base
}

function captureScrollPos() {
  if (!scrollEl.value) return null
  const first = virtualizer.value.getVirtualItems()[0]
  if (!first) return null
  return {
    scrollIndex: first.index,
    scrollOffset: Math.max(0, scrollEl.value.scrollTop - first.start),
  }
}

function restoreScrollPos(scrollIndex: number, scrollOffset: number) {
  // Two-rAF pattern: scrollToIndex triggers TanStack's internal correction.
  // Wait one rAF for it to settle, then set scrollTop directly — TanStack is idle by then.
  // If the item isn't in measurementsCache yet (e.g. filtered set still building),
  // retry up to 10 times at 100ms intervals before giving up.
  programmaticScrolling = true
  virtualizer.value.scrollToIndex(scrollIndex, { align: 'start' })
  let attempts = 0
  function applyOffset() {
    const item = virtualizer.value.measurementsCache.find((m) => m.index === scrollIndex)
    if (item && scrollEl.value) {
      scrollEl.value.scrollTop = item.start + scrollOffset
      requestAnimationFrame(() => { programmaticScrolling = false })
    } else if (++attempts < 10) {
      virtualizer.value.scrollToIndex(scrollIndex, { align: 'start' })
      setTimeout(() => requestAnimationFrame(applyOffset), 100)
    } else {
      programmaticScrolling = false
    }
  }
  requestAnimationFrame(applyOffset)
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

function scrollToBook(bookId: number) {
  const index = props.results.findIndex((r) => r.bookId === bookId)
  if (index < 0) return
  programmaticScrolling = true
  virtualizer.value.scrollToIndex(index, { align: 'start' })
  let attempts = 0
  function applyOffset() {
    const item = virtualizer.value.measurementsCache.find((m) => m.index === index)
    if (item && scrollEl.value) {
      scrollEl.value.scrollTop = item.start
      requestAnimationFrame(() => { programmaticScrolling = false })
    } else if (++attempts < 10) {
      virtualizer.value.scrollToIndex(index, { align: 'start' })
      setTimeout(() => requestAnimationFrame(applyOffset), 100)
    } else {
      programmaticScrolling = false
    }
  }
  requestAnimationFrame(applyOffset)
}

function onScroll() {}

defineExpose({ captureScrollPos, scrollToBook })
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
    </div>
    <template v-else>
      <div
        ref="scrollEl"
        class="scroller"
        tabindex="0"
        :style="{ fontSize: `${fontPx}px` }"
        @scroll="onScroll"
        @contextmenu="contextMenuRef?.show($event)"
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
            <div class="result-item">
              <div
                class="result-header"
                :title="resultTitle(results[vRow.index]!)"
                @click="emit('resultClick', results[vRow.index]!)"
              >
                <span class="book-title">{{ results[vRow.index]!.bookTitle }}</span>
                <span v-if="results[vRow.index]!.tocText" class="sep">›</span>
                <span v-if="results[vRow.index]!.tocText" class="toc-text">{{
                  results[vRow.index]!.tocText
                }}</span>
                <button
                  v-if="previewOf(results[vRow.index]!)"
                  class="preview-recenter-btn"
                  title="חזרה לשורת התוצאה"
                  @click.stop="recenterPreview(results[vRow.index]!)"
                >
                  <IconArrowReset24Regular />
                </button>
                <button
                  class="preview-toggle-btn"
                  :title="previewOf(results[vRow.index]!) ? 'סגור תצוגה מקדימה' : 'הצג עוד'"
                  @click.stop="togglePreview(results[vRow.index]!)"
                >
                  <IconDismiss24Regular v-if="previewOf(results[vRow.index]!)" />
                  <IconEye24Regular v-else />
                </button>
              </div>
              <FullTextSearchResultPreview
                v-if="previewOf(results[vRow.index]!)"
                :ref="(el) => setPreviewRef(results[vRow.index]!.lineId, el)"
                :state="previewOf(results[vRow.index]!)!"
                :render-html="renderSnippet"
                :load-above="() => loadAbove(results[vRow.index]!)"
                :load-below="() => loadBelow(results[vRow.index]!)"
              />
              <!-- eslint-disable-next-line vue/no-v-html -->
              <div v-else class="snippet" v-html="renderSnippet(results[vRow.index]!.snippet)" />
            </div>
          </div>
        </div>
      </div>
    </template>

    <ContextMenu ref="contextMenuRef" :items="copyMenuItems" />
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
  color: color-mix(in srgb, var(--text-primary) 70%, var(--status-danger));
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
/* Header icon buttons (recenter + preview toggle), pushed together to the
   inline-end (far left in RTL) of the header row. Only the toggle's icon
   switches between its two states — no background change. */
.preview-toggle-btn,
.preview-recenter-btn {
  flex-shrink: 0;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border: none;
  background: none;
  color: var(--text-secondary);
  cursor: pointer;
  padding: 2px;
  border-radius: 4px;
  transition: color 120ms;
}
/* The auto margin sits on the first button of the group; the recenter button only
   exists while the preview is open, so the toggle carries it when alone. */
.preview-toggle-btn,
.preview-recenter-btn {
  margin-inline-start: auto;
}
.preview-recenter-btn + .preview-toggle-btn {
  margin-inline-start: 0;
}
.preview-toggle-btn:hover,
.preview-recenter-btn:hover {
  color: var(--accent-color);
}
.preview-toggle-btn:active,
.preview-recenter-btn:active {
  color: color-mix(in srgb, var(--accent-color) 75%, black);
}
.preview-toggle-btn svg,
.preview-recenter-btn svg {
  width: 1.1em;
  height: 1.1em;
  display: block;
  color: inherit; /* theme.css pins svg color globally — restore inheritance so hover shows */
}
.snippet {
  font-family: var(--text-font);
  font-size: 1em;
  line-height: var(--line-height, 1.5);
  color: var(--text-secondary);
  direction: rtl;
  text-align: justify;
  user-select: text;
  display: -webkit-box;
  -webkit-line-clamp: 4;
  -webkit-box-orient: vertical;
  overflow: hidden;
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
