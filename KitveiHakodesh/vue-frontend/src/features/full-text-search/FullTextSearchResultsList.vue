<script setup lang="ts">
import { ref, computed, watch, onBeforeUnmount, nextTick } from 'vue'
import { useVirtualizer } from '@tanstack/vue-virtual'
import { IconSearchSparkle24Regular, IconEye24Regular, IconDismiss24Regular, IconArrowReset24Regular } from '@iconify-prerendered/vue-fluent'
import { useSettingsStore } from '@/stores/settingsStore'
import { useEventListener } from '@vueuse/core'
import { censorDivineNames } from '@/utils/censorDivineNames'
import { useVirtualScrollerKeys } from '@/composables/useVirtualScrollerKeys'
import { wantsNewTab, withNewTabHint } from '@/composables/useOpenInNewTab'
import ContextMenu from '@/components/ContextMenu.vue'
import { useFullTextSearchCopyMenu, useFullTextSearchScopedCopy } from './useFullTextSearchCopyMenu'
import { useFullTextSearchPreview } from './useFullTextSearchPreview'
import FullTextSearchResultPreview from './FullTextSearchResultPreview.vue'
import BookViewAbbrevTooltip from '@/features/book-view/lines/BookViewAbbrevTooltip.vue'
import { useBookViewAbbrevTooltip } from '@/features/book-view/lines/useBookViewAbbrevTooltip'
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
  resultClick: [FullTextSearchResult, boolean?]
  saveScroll: [{ scrollIndex: number; scrollOffset: number }]
}>()

const SEARCH_ERROR_MESSAGES: Record<string, string> = {
  indexNotReady: 'האינדקס לא מוכן לחיפוש — נסה שוב בעוד כמה רגעים',
  indexMerging:  'האינדקס מבצע מיזוג — נסה שוב בעוד כמה רגעים',
  searchFailed:  'אירעה שגיאה בחיפוש',
}
const settingsStore = useSettingsStore()
const scrollEl = ref<HTMLElement | null>(null)

// The preview pane is nested inside this scroller and mounts its own instance,
// so selections in there are left to it.
const { abbrevTooltip } = useBookViewAbbrevTooltip(scrollEl, {
  ignoreWithin: '.preview-box',
})

// Right-click copy menu (העתק / העתק טקסט נקי) — matches the book & txt views.
const contextMenuRef = ref<InstanceType<typeof ContextMenu> | null>(null)
const { items: copyMenuItems } = useFullTextSearchCopyMenu()
useFullTextSearchScopedCopy(scrollEl)

const fontPx = computed(() => {
  const zoomFactor = (props.zoom ?? 100) / 100
  return zoomFactor * (settingsStore.fontSize / 100) * 15
})
let programmaticScrolling = false

// Progressive render window. All results stream into props.results (never capped), but we
// only ever hand the virtualizer a slice of them — grown as the user scrolls toward its end.
// This keeps the virtualizer's per-count measurement rebuild cheap no matter how many
// hundreds of thousands of results exist, and stops the list from churning while results
// stream in: the window stays put, only the result-count badge climbs. (No fetch on grow —
// the data is already in memory; we're just revealing more of it.)
const RENDER_PAGE = 200
const renderCount = ref(RENDER_PAGE)
const renderLimit = computed(() => Math.min(props.results.length, renderCount.value))

const virtualizer = useVirtualizer(
  computed(() => ({
    count: renderLimit.value,
    getScrollElement: () => scrollEl.value,
    estimateSize: () => 80,
    overscan: 8,
    getItemKey: (index) => props.results[index]?.lineId ?? index,
  })),
)

// Keep the render window large enough to cover the current viewport. Growing only in
// small steps as the viewport reaches the window's END (the old behaviour) lags behind a
// fast/far scroll: getTotalSize() — hence the scrollbar extent — is derived from renderLimit,
// so the track only ever represents the revealed rows. A fast drag then saturates at the
// window's end, captureScrollPos records an index capped to that partial window, and the
// saved position is wrong (this is the "scroll far outside the current range → restore lands
// wrong" bug). Instead, grow the window so it always reaches a full RENDER_PAGE beyond the
// furthest visible row: the window tracks the scroll position rather than trailing it, so the
// captured index is always a real index into the full result set. Still windowed (we never
// hand the virtualizer all N rows at once), just position-driven instead of end-triggered.
watch(
  () => virtualizer.value.getVirtualItems(),
  (items) => {
    const last = items[items.length - 1]
    if (!last) return
    const needed = Math.min(props.results.length, last.index + 1 + RENDER_PAGE)
    if (needed > renderCount.value) renderCount.value = needed
  },
)

function renderSnippet(snippet: string): string {
  if (!snippet) return snippet
  return censorDivineNames(snippet, settingsStore.censorOptions)
}

// "הצג עוד" — per-result windowed live preview (replaces the clamped snippet).
const { previewOf, togglePreview, loadAbove, loadBelow, reseedPreview, clearPreviews } = useFullTextSearchPreview()
watch(
  () => props.searchQuery,
  () => {
    clearPreviews()
    renderCount.value = RENDER_PAGE // new search → collapse the window to the first page
  },
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
  const base = result.tocText ? `${result.bookTitle} › ${result.tocText}` : result.bookTitle
  return withNewTabHint(base)
}

function captureScrollPos() {
  if (!scrollEl.value) return null
  const top = scrollEl.value.scrollTop
  const items = virtualizer.value.getVirtualItems()
  if (!items.length) return null
  // First VISIBLE row — NOT items[0], which is an overscan row ~8 rows above the viewport.
  // With items[0] the saved offset spanned all the overscan rows in pixels (observed: 924px
  // ≈ 11 rows), and restoring `item.start + offset` then depended on the ESTIMATED heights
  // of those rows — landing rows off and jumping when real measurements arrived. Anchoring
  // to the row that actually contains scrollTop keeps the offset intra-row (< one row
  // height), and `scrollTop = item.start + offset` is then row-exact regardless of how far
  // the estimates are off, because the row's rendered position and item.start come from the
  // same measurement table.
  const first = items.find((it) => it.end > top) ?? items[0]!
  return {
    scrollIndex: first.index,
    scrollOffset: Math.max(0, top - first.start),
  }
}

// Generation of the restore currently in charge. Bumped by armRestore when a new target
// supersedes an older one; any attempt whose captured generation no longer matches gives up
// instead of scrolling to a place the user has already navigated away from. Assigned by the
// restore block below — before then no restore can be in flight, so 0 is a safe stand-in.
let currentGeneration = () => 0

function restoreScrollPos(scrollIndex: number, scrollOffset: number) {
  // Pre-paint restore: the first applyOffset attempt runs synchronously (see call at the
  // bottom) so the scroll lands before the browser paints — no flash of the list top.
  // One rAF later the position is re-asserted to absorb TanStack's dynamic-measurement
  // shift from the target rows' first real measurement. If the item isn't in
  // measurementsCache yet (e.g. filtered set still building), retry up to 10 times at
  // 100ms intervals before giving up.
  const myGeneration = currentGeneration()
  const superseded = () => currentGeneration() !== myGeneration
  programmaticScrolling = true
  virtualizer.value.scrollToIndex(scrollIndex, { align: 'start' })
  let attempts = 0
  function applyOffset() {
    // A newer restore has taken over (the retry loop can span seconds while results stream,
    // and a recents pick lands mid-flight) — stop touching scrollTop and let it own the view.
    if (superseded()) return
    const item = virtualizer.value.measurementsCache.find((m) => m.index === scrollIndex)
    if (item && scrollEl.value) {
      scrollEl.value.scrollTop = item.start + scrollOffset
      requestAnimationFrame(() => {
        if (superseded()) return
        // Re-assert once after TanStack's dynamic-measurement pass: the rows rendered at
        // the target measure their real heights on this first frame, which can shift
        // item.start under us. Same coordinates re-read → at most a sub-row settle,
        // instead of the old visible jump.
        const settled = virtualizer.value.measurementsCache.find((m) => m.index === scrollIndex)
        if (settled && scrollEl.value) scrollEl.value.scrollTop = settled.start + scrollOffset
        requestAnimationFrame(() => { programmaticScrolling = false })
      })
    } else if (++attempts < 10) {
      virtualizer.value.scrollToIndex(scrollIndex, { align: 'start' })
      setTimeout(() => requestAnimationFrame(applyOffset), 100)
    } else {
      programmaticScrolling = false
    }
  }
  // First attempt runs SYNCHRONOUSLY — we're called from nextTick right after the render
  // window widened, so the DOM is up to date but nothing has painted yet. Landing the
  // scroll here means the first painted frame is already at the target (no top-flash).
  // The rAF path above and the 100ms retries remain as fallback when the target row
  // isn't measurable yet.
  applyOffset()
}

// Re-arms the restore watcher below for a NEW target. The watcher is one-shot by design —
// it stops itself the moment it restores — which is right for the mount-time restore but
// leaves nothing armed for a SECOND navigation into this same (never remounted) page:
// picking a search row from the address bar patches the tab in place, so the page stays
// mounted and only initialScrollIndex changes. armRestore resets the latch and re-runs the
// watch; the parent calls it after updating the target.
let armRestore = () => {}

{
  // Restore scroll once the target row is actually reachable. This is the tricky case on
  // reload: results stream in over time (fresh re-search) or arrive as a partial cache
  // prefix that then resumes streaming (loadCachedResults sets isSearching=true while
  // populating). The saved index can therefore be far beyond what has arrived so far.
  //
  // A one-shot restore against the first batch fails: restoreScrollPos would exhaust its
  // fixed retry budget long before index N streams in, then give up near the top and never
  // re-run. So instead we re-attempt on every results growth until the target index exists
  // in props.results — only THEN do we widen the render window to include it and scroll.
  // We stop when we've either restored, run out of a target, or the stream finished and the
  // saved index is past the (now final) result count — in which case we clamp to the last row.
  let restored = false
  const tryRestore = () => {
    if (restored) return true
    const target = props.initialScrollIndex
    // No target YET is not "nothing to restore" — on reload the parent sets
    // initialScrollIndex asynchronously in onMounted (after the IDB read), which can land
    // AFTER results already started streaming. Returning true here would stop the watcher
    // for good and the restore would silently never run (the sometimes-works reload bug:
    // it only worked when the IDB read happened to win the race against the first batch).
    // Keep waiting instead — if there is genuinely no saved position, the watcher just
    // stays armed and inert, which is harmless: initialScrollIndex is set at most once.
    if (target == null) return false
    const len = props.results.length
    if (!len) return false // no results yet — wait for more
    // If the target hasn't streamed in yet, keep waiting — UNLESS the stream is done, in
    // which case the target is unreachable (fewer results than when it was saved, e.g. the
    // filter/sort produced a shorter set); clamp to the last available row rather than
    // waiting forever.
    if (target >= len) {
      if (props.isSearching) return false // more may still arrive — keep waiting
    }
    const index = Math.min(target, len - 1)
    restored = true
    // Widen the render window NOW (synchronously) so the re-render it triggers — which
    // grows the scroller's inner height enough to contain the target — flushes in this
    // same task. Widening inside the nextTick callback (the old order) meant scrollTop was
    // set against the still-short scroller and clamped, deferring the real scroll by a
    // frame or more: the user saw the list paint at the TOP first, then jump to the target.
    renderCount.value = Math.max(renderCount.value, index + RENDER_PAGE)
    // After that render flush (nextTick), apply the scroll SYNCHRONOUSLY — still before the
    // browser paints this task's updates, so the first visible frame is already at the target.
    nextTick(() => restoreScrollPos(index, props.initialScrollOffset ?? 0))
    return true
  }
  // `generation` is what makes re-arming safe. Stopping and re-creating the watcher would
  // race: an in-flight restoreScrollPos retry loop from the PREVIOUS target keeps running
  // and would fight the new one for scrollTop. Instead the watcher lives for the component's
  // lifetime and every attempt captures the generation it started in — a stale attempt sees
  // the bump and bails rather than scrolling somewhere the user has already left.
  let generation = 0
  // Read by restoreScrollPos to abandon a superseded attempt.
  currentGeneration = () => generation
  watch(
    // initialScrollIndex is a watched source too: on reload it arrives asynchronously
    // (parent onMounted IDB read), and the watcher must re-fire when it lands.
    [() => props.results.length, () => props.isSearching, () => props.initialScrollIndex],
    () => { tryRestore() },
    { flush: 'post', immediate: true },
  )
  armRestore = () => {
    generation++   // abandon any retry loop still running for the previous target
    restored = false
    // Do NOT attempt the restore synchronously here. The navigation that re-armed us also
    // kicked off handleSearch → executeSearch, which is async and `await`s cancelSearch()
    // BEFORE it does `results.value = []`. So at this instant props.results may still hold
    // the OUTGOING search's rows: restoring against them would scroll into a list that is
    // about to be wiped, and the real results would then stream in with the latch already
    // spent. Leaving it to the watcher is both simpler and correct — it re-fires on every
    // results-length change and on isSearching, which is exactly the streaming case its
    // wait/clamp logic was written for.
    //
    // The one case the watcher would miss is a re-arm where NOTHING it watches ever changes
    // (the same query re-selected from recents, results already settled). nextTick catches
    // that: by then executeSearch has either emptied the list (watcher takes over) or the
    // navigation didn't re-search at all and the settled rows are the right ones.
    nextTick(() => { if (!restored) tryRestore() })
  }
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
  // Keyboard nav (PageDown/End) stays within the revealed window; scrolling to its end
  // triggers the grow watcher above, so holding PageDown progressively loads more.
  () => renderLimit.value,
)

function scrollToBook(bookId: number) {
  const index = props.results.findIndex((r) => r.bookId === bookId)
  if (index < 0) return
  // Make sure the target is inside the render window before scrolling to it.
  if (index >= renderCount.value) renderCount.value = index + RENDER_PAGE
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

defineExpose({ captureScrollPos, scrollToBook, armRestore: () => armRestore() })
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
      <BookViewAbbrevTooltip
        v-if="abbrevTooltip"
        :key="abbrevTooltip.id"
        :data="abbrevTooltip"
      />
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
                @click="emit('resultClick', results[vRow.index]!, wantsNewTab($event))"
                @auxclick.middle="emit('resultClick', results[vRow.index]!, wantsNewTab($event))"
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
                :reseed="() => reseedPreview(results[vRow.index]!)"
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
  /* Mirror the book view's text-column width cap (רוחב מקסימלי עבור עמודת הטקסט).
     Centering keeps the result block aligned under the same reading measure. */
  max-width: var(--lines-content-max-width, none);
  margin-inline: auto;
}
/* On a wide pane give the results a bit more breathing room from the edges
   (רווח נוסף מהצדדים במסך רחב). Container query on the app-shell pane, so it
   reacts to THIS pane's width, not the viewport (split-shell aware). */
@container app-shell (min-width: 600px) {
  .result-item {
    padding-inline: 24px;
  }
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
  opacity: 0.55;
  transition: color 120ms, opacity 120ms;
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
  opacity: 1;
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
  text-align-last: right;
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
