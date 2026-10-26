<script setup lang="ts">
import { t } from '@/i18n'
import { ref, shallowRef, computed, watch, onBeforeUnmount, onScopeDispose, nextTick } from 'vue'
import { IconSearchSparkle24Regular, IconEye24Regular, IconDismiss24Regular, IconArrowReset24Regular } from '@iconify-prerendered/vue-fluent'
import { useSettingsStore } from '@/stores/settingsStore'
import { useEventListener } from '@vueuse/core'
import { registerPositionCapture } from '@/webview-host/persistenceFlush'
import { censorDivineNames } from '@/utils/censorDivineNames'
import WindowedScroller from '@/components/WindowedScroller.vue'
import type { WindowedScrollerApi } from '@/components/windowedScrollerTypes'
import { wantsNewTab, withNewTabHint } from '@/composables/useOpenInNewTab'
import { useFullTextSearchPreview } from './useFullTextSearchPreview'
import FullTextSearchResultPreview from './FullTextSearchResultPreview.vue'
import MessageBanner from '@/components/MessageBanner.vue'
import { INDEX_REBUILD_BUTTON_LABELS } from '@/composables/useSearchIndexStatus'
import BookViewAbbrevTooltip from '@/features/book-view/lines/BookViewAbbrevTooltip.vue'
import { useBookViewAbbrevTooltip } from '@/features/book-view/lines/useBookViewAbbrevTooltip'
import BookViewScrollbarTocTooltip from '@/features/book-view/lines/BookViewScrollbarTocTooltip.vue'
import { useBookViewScrollbarTocTooltip } from '@/features/book-view/lines/useBookViewScrollbarTocTooltip'
import { useVirtualizedTextSelection } from '@/composables/useVirtualizedTextSelection'
import { wrapRtlHtml, htmlToPlainText } from '@/composables/useLineCopy'
import { buildSelectedResultsHtml, resultReference } from './fullTextSearchResultsCopy'
import { ensureSnippets, snippetHtmlOf } from './fullTextSearchSnippets'
import { bookTitleOf, requestReferences, tocPathOf } from './fullTextSearchReferences'
import { decideRestore } from './scrollRestore'
import type { FullTextSearchResult, SearchFailReason } from './fullTextSearchTypes'
import { teamimFontStyle } from '@/utils/teamimFont'

const props = defineProps<{
  results: FullTextSearchResult[]
  totalResults: number
  /**
   * Bumped whenever the results array is REBUILT rather than appended to — a new
   * search, a sort change, or a book/@-token filter change. Row indexes address
   * different results after any of those, which the selection pins and the mounted
   * rows must know about; see useFullTextSearchFilters.filteredGeneration.
   */
  resultsGeneration: number
  searchQuery: string
  isSearching: boolean
  hasSearched: boolean
  searchError?: SearchFailReason | null
  dbNotFound?: boolean
  /** The index build failed — searches cannot succeed until it is rebuilt. */
  indexBuildFailed?: boolean
  initialScrollIndex?: number
  initialScrollOffset?: number
  zoom?: number
}>()

const emit = defineEmits<{
  resultClick: [FullTextSearchResult, boolean?]
  saveScroll: [{ scrollIndex: number; scrollOffset: number }]
}>()

const DB_NOT_FOUND_MESSAGE = t('fullTextSearch.error.dbNotFound')
const INDEX_BUILD_FAILED_MESSAGE =
  t('fullTextSearch.error.indexBuildFailed', { button: INDEX_REBUILD_BUTTON_LABELS.fullTextSearch })
const SEARCH_ERROR_MESSAGES: Record<SearchFailReason, string> = {
  indexNotReady: t('fullTextSearch.error.indexNotReady'),
  indexMerging:  t('fullTextSearch.error.indexMerging'),
  searchFailed:  t('fullTextSearch.error.searchFailed'),
}
const settingsStore = useSettingsStore()

// The windowed scroller mounts only the rows around the viewport, and fetches their
// snippets BEFORE mounting them — no placeholder rows, no estimated heights, and a
// jump (restore, book link) lands exactly, once. Its viewport is the scroll element
// every listener below uses; it exists only while results are shown (v-if below).
const windowedScroller = ref<InstanceType<typeof WindowedScroller> | null>(null)
const scroller = (): WindowedScrollerApi | null => windowedScroller.value
const scrollEl = computed<HTMLElement | null>(() => windowedScroller.value?.scrollerElement ?? null)

// The preview pane is nested inside this scroller and mounts its own instance,
// so selections in there are left to it.
const { abbrevTooltip } = useBookViewAbbrevTooltip(scrollEl, {
  ignoreWithin: '.preview-box',
})

// Hovering the scrollbar names the result there — its number, book and TOC path —
// the same label the book view's lines and commentary bars show.
const scrollbarElement = computed(() => windowedScroller.value?.scrollbarElement ?? null)
const { scrollbarTocTooltip, refreshScrollbarTocTooltip, closeScrollbarTocTooltip } = useBookViewScrollbarTocTooltip(scrollbarElement, {
  getCurrentLineIndex: () => null,
  getTargetLineIndex: (clientY) => scroller()?.barTargetAt(clientY) ?? null,
  viewportElement: scrollEl,
  getBookTitle: () => '',
  getLabelAt: (index) => {
    const result = shownResults.value[index]
    if (!result) return { bookTitle: '', path: '' }
    return { bookTitle: `${(index + 1).toLocaleString()}. ${bookTitleOf(result)}`, path: tocPathOf(result) ?? '' }
  },
  // Streaming results resize the thumb under a resting pointer; the label waits for them.
  isSuspended: () => props.isSearching,
})
useEventListener(scrollEl, 'scroll', refreshScrollbarTocTooltip, { passive: true })
watch(() => props.isSearching, (searching) => { if (searching) closeScrollbarTocTooltip() })

// Right-click copy and the clean-text toggle are served by the app-wide fallback
// menu (GlobalContextMenu). The PAYLOAD, though, is built here: see
// buildSelectedResultsCopyHtml for why the DOM selection is not enough.

const fontPx = computed(() => {
  const zoomFactor = (props.zoom ?? 100) / 100
  return zoomFactor * (settingsStore.fontSize / 100) * 15
})
// True while the app is moving the list (a restore, a book jump): its position is
// transient and must not be persisted.
let programmaticScrolling = false

// Keeps the rows holding a selection's two ends mounted however far they scroll
// away. Without it, dragging a selection down the results list unmounts the row it
// started in, and the browser re-homes the anchor onto whatever row now occupies
// that position — so the selection silently comes to mean something else.
const { pinnedRowIndexes, selectedRowIndexRange } = useVirtualizedTextSelection(
  scrollEl,
  // The rebuild generation, which is the only thing that tracks this exactly: the
  // result count grows WHILE the user drags (results stream in), and the leading
  // result's id survives a filter change that re-indexes everything behind it.
  () => props.resultsGeneration,
)

/**
 * The results the list SHOWS. Streamed appends pass straight through; a rebuild (new
 * sort, filter change: `resultsGeneration` bumps) is held back until the snippets of the
 * rows that will be on screen have arrived, then swaps in with one render — never a frame
 * of the new order's rows with their snippets still missing.
 */
const shownResults = shallowRef<FullTextSearchResult[]>(props.results)
const shownGeneration = ref(props.resultsGeneration)
let swapToken = 0
watch(
  () => [props.results, props.resultsGeneration] as const,
  async ([results, generation]) => {
    const token = ++swapToken
    if (generation === shownGeneration.value) { shownResults.value = results; return }
    // The scroller keeps its window at the same INDEXES through a rebuild (it re-asks
    // its rows on `rowSourceVersion`), so those are the rows to warm — clamped to the
    // end when the new list is shorter than where the reader was.
    const range = scroller()?.renderedRange
    const visible = range ? Math.max(range.end - range.start, 1) : 0
    if (visible && results.length) {
      const end = Math.min(range!.end, results.length)
      const start = Math.max(0, Math.min(range!.start, end - visible))
      try { await ensureSnippets(results.slice(start, end)) } catch { /* show what there is */ }
    }
    if (token !== swapToken) return
    shownGeneration.value = generation
    shownResults.value = results
  },
)

function renderSnippet(snippet: string): string {
  if (!snippet) return snippet
  return censorDivineNames(snippet, settingsStore.censorOptions)
}

/**
 * The scroller's row source: a range's snippets are fetched before the range mounts,
 * so every row arrives complete. Its reference (book title, TOC path) is asked for at
 * the same time but not awaited — the header is one line either way, so the path
 * filling in a moment later changes no row's height.
 */
function ensureRows(start: number, end: number): Promise<void> {
  const hits = shownResults.value.slice(start, end)
  requestReferences(hits)
  scheduleLookahead(start, end)
  return ensureSnippets(hits)
}

// ── Lookahead ──────────────────────────────────────────────────────────────────
//
// The scroller asks for a range only when the viewport nears the mounted edge, and
// waits for its snippets (two service round trips) before mounting it — so a steady
// wheel scroll caught up with the fetch and SAT at the edge for the wait. Each asked
// range therefore also warms, without being awaited, the next windows in the scroll
// direction (and a smaller margin behind): by the time the scroller asks for them,
// their snippets are already here and the range mounts at once. Debounced so a fast
// fling warms only the window it is heading to, not every window it passed; the
// request itself is bounded (LOOKAHEAD_AHEAD + LOOKAHEAD_BEHIND hits at most).
const LOOKAHEAD_AHEAD = 60
const LOOKAHEAD_BEHIND = 20
const LOOKAHEAD_DEBOUNCE_MS = 40
let lookaheadTimer: ReturnType<typeof setTimeout> | null = null
let lastAskedStart = 0
function scheduleLookahead(start: number, end: number) {
  const downward = start >= lastAskedStart
  lastAskedStart = start
  if (lookaheadTimer) clearTimeout(lookaheadTimer)
  lookaheadTimer = setTimeout(() => {
    lookaheadTimer = null
    const list = shownResults.value
    const [aheadFrom, aheadTo] = downward
      ? [end, Math.min(list.length, end + LOOKAHEAD_AHEAD)]
      : [Math.max(0, start - LOOKAHEAD_AHEAD), start]
    const [behindFrom, behindTo] = downward
      ? [Math.max(0, start - LOOKAHEAD_BEHIND), start]
      : [end, Math.min(list.length, end + LOOKAHEAD_BEHIND)]
    const hits = [...list.slice(aheadFrom, aheadTo), ...list.slice(behindFrom, behindTo)]
    if (!hits.length) return
    requestReferences(hits)
    ensureSnippets(hits).catch(() => { /* the real ask retries on its own */ })
  }, LOOKAHEAD_DEBOUNCE_MS)
}
onScopeDispose(() => { if (lookaheadTimer) clearTimeout(lookaheadTimer) })

// "הצג עוד" — per-result windowed live preview (replaces the clamped snippet).
const { previewOf, togglePreview, loadAbove, loadBelow, reseedPreview, clearPreviews } = useFullTextSearchPreview()
watch(
  () => props.searchQuery,
  (query, previousQuery) => {
    clearPreviews()
    // The FIRST query this list sees is the tab's own search coming back (a reload, a
    // tab switch): its saved position is exactly what must be restored, not cancelled.
    if (!previousQuery) return
    // A navigation that armed a restore (Back/Forward or a recents pick onto a search)
    // changes the query too — and the query arrives LATER than the arm (the search has to
    // cancel the running one first). That query change is the restore's own, not a new
    // search: it spends the arm, and only NOW may the restore run — against this query's
    // results. Running it at the arm would land on the OUTGOING search's rows, still
    // present at that instant, and spend the restore on them.
    if (armedForNextQuery) {
      armedForNextQuery = false
      restored = false
      nextTick(tryRestore)
      return
    }
    // A new query means a new result list: kill any pending restore so the previous
    // search's saved index can't be applied to it, and start at the top.
    cancelRestore()
    void scroller()?.scrollToIndex(0)
  },
)

/**
 * The clipboard payload for the current selection, built from the RESULT MODEL
 * rather than the DOM.
 *
 * A selection dragged over many results has only a screenful of them in the DOM, so
 * serializing the live Range would silently drop the rest — the same windowing gap
 * the pinned rows fix for the selection itself. Copying a range of results is a
 * normal thing to want (collecting every hit for a phrase), so the payload spans
 * every selected result and gives each one its reference.
 *
 * Returns null when nothing is selected here, which leaves the copy event alone for
 * GlobalContextMenu or the browser to handle.
 */
function buildSelectedResultsCopyHtml(): string | null {
  const selection = window.getSelection()
  if (!selection || selection.isCollapsed || selection.rangeCount === 0) return null
  const range = selection.getRangeAt(0)
  if (!scrollEl.value?.contains(range.commonAncestorContainer)) return null

  // An open preview replaces the row's snippet with up to 120 lines of the real book
  // text, nested INSIDE the row — so a selection in there resolves to the row like any
  // other, and rebuilding from the model would hand back the short clamped snippet
  // instead of the paragraphs the user highlighted. Leave the preview's own selection
  // to the native copy, the same way the abbreviation tooltip ignores it.
  const container =
    range.commonAncestorContainer instanceof Element
      ? range.commonAncestorContainer
      : range.commonAncestorContainer.parentElement
  if (container?.closest('.preview-box')) return null

  // Likewise when the selection merely CROSSES an open preview: that result shows text
  // its snippet does not contain, so a model rebuild would substitute different content.
  const rowIndexRange = selectedRowIndexRange()
  if (rowIndexRange) {
    for (let rowIndex = rowIndexRange.firstRowIndex; rowIndex <= rowIndexRange.lastRowIndex; rowIndex++) {
      const result = props.results[rowIndex]
      if (result && previewOf(result)) return null
    }
  }

  return buildSelectedResultsHtml(
    props.results,
    rowIndexRange,
    (result) => renderSnippet(snippetHtmlOf(result) ?? ''),
    referenceOf,
    settingsStore.copyCleanText,
  )
}

// Serves Ctrl+C and the context menu's copy — both fire a native copy event on the
// scroller — plus a drag of the selection into Word. Writing a payload preventDefaults
// the event, which is also what stands GlobalContextMenu's document-level fallback down
// (it returns early on event.defaultPrevented) so the two cannot both write.
useEventListener(scrollEl, 'copy', (event: ClipboardEvent) => {
  const html = buildSelectedResultsCopyHtml()
  if (html === null) return
  event.clipboardData?.setData('text/html', wrapRtlHtml(html))
  event.clipboardData?.setData('text/plain', htmlToPlainText(html))
  event.preventDefault()
})

useEventListener(scrollEl, 'dragstart', (event: DragEvent) => {
  const html = buildSelectedResultsCopyHtml()
  if (html === null) return
  event.dataTransfer?.setData('text/html', wrapRtlHtml(html))
  event.dataTransfer?.setData('text/plain', htmlToPlainText(html))
})

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

/** The row's citation, from the hit's ids (fullTextSearchReferences). '' for the TOC path
 *  while the book's TOC has not arrived yet. */
function referenceOf(result: FullTextSearchResult): string {
  return resultReference(bookTitleOf(result), tocPathOf(result) ?? '')
}

function resultTitle(result: FullTextSearchResult): string {
  // Shares resultReference with the copy path so a citation reads the same whether it
  // is hovered or pasted.
  return withNewTabHint(referenceOf(result))
}

function captureScrollPos() {
  const first = scroller()?.readFirstVisible(0)
  if (!first) return null
  return { scrollIndex: first.index, scrollOffset: Math.max(0, first.offsetWithinRow) }
}

/**
 * Lands the list on a row, exactly: the scroller fetches the rows' snippets, mounts
 * them around the target and sets the scroll from the row's real position. The
 * position is not persisted while the landing is in flight.
 */
async function landOn(index: number, offsetPx = 0) {
  const api = scroller()
  if (!api) return
  programmaticScrolling = true
  try {
    await api.scrollToIndex(index, { offsetPx })
  } finally {
    // The landing's scroll event is dispatched at the next frame's start.
    requestAnimationFrame(() => { programmaticScrolling = false })
  }
}

// ── Restore ───────────────────────────────────────────────────────────────────
//
// Results stream in over time (a fresh re-search, or a cached prefix that resumes
// streaming), and on reload the parent sets initialScrollIndex asynchronously after its
// IDB read — so the target row may not exist yet when the list mounts. decideRestore
// (scrollRestore.ts, unit-tested) says on each change whether to wait, land, or stop.
//
// When the target is already reachable as the list first mounts, the scroller OPENS
// on it (`openingIndex`): the first render is the saved place, never the top.

let restored = false
// Set when the parent arms a restore target; the next query change belongs to that
// navigation (see the searchQuery watcher) and spends it.
let armedForNextQuery = false

function restoreDecision() {
  return decideRestore({ target: props.initialScrollIndex, loaded: props.results.length, isSearching: props.isSearching })
}

const openingIndex = computed(() => {
  if (restored) return undefined
  const decision = restoreDecision()
  return decision.action === 'restore' ? decision.index : undefined
})

function tryRestore() {
  if (restored) return
  const decision = restoreDecision()
  if (decision.action === 'wait') return
  if (decision.action === 'done') return
  const api = scroller()
  // Not mounted yet: the scroller will open on `openingIndex` itself.
  if (!api) return
  restored = true
  void landOn(decision.index, props.initialScrollOffset ?? 0)
}

/** Wired to the scroller's `opened` event: it opened on the restore target. */
function onScrollerOpened() {
  restored = true
}

watch(
  // initialScrollIndex is a watched source too: on reload it arrives asynchronously
  // (parent onMounted IDB read), and the restore must run when it lands.
  [() => props.results.length, () => props.isSearching, () => props.initialScrollIndex],
  tryRestore,
  { flush: 'post' },
)

/**
 * Re-arms the restore for a NEW target. A second navigation into this same (never
 * remounted) page — Back/Forward, or picking a search row from the address bar — patches
 * the tab in place; the parent calls this after seeding initialScrollIndex, with the
 * query that navigation shows. The restore runs once THAT query's results are the ones
 * in the list (see the searchQuery watcher); when it is the query already shown, the
 * rows are already the right ones.
 */
function armRestore(targetQuery: string) {
  if (targetQuery === props.searchQuery) {
    armedForNextQuery = false
    restored = false
    nextTick(tryRestore)
  } else {
    armedForNextQuery = true
  }
}

/** Spends the restore: the results about to arrive belong to a NEW search, which starts at the top. */
function cancelRestore() {
  restored = true
  armedForNextQuery = false
}

function savePos() {
  if (programmaticScrolling) return
  const pos = captureScrollPos()
  if (!pos) return
  emit('saveScroll', pos)
}

// Browser hide/unload and the host's close all run through the shared flush.
onScopeDispose(registerPositionCapture(savePos))
onBeforeUnmount(() => {
  programmaticScrolling = false
  savePos()
})

function scrollToBook(bookId: number) {
  const index = props.results.findIndex((r) => r.bookId === bookId)
  if (index >= 0) void landOn(index)
}

defineExpose({
  captureScrollPos,
  scrollToBook,
  armRestore,
  cancelRestore,
})
</script>

<template>
  <div dir="rtl" class="results-wrap">
    <div v-if="dbNotFound || indexBuildFailed || !hasSearched || (!results.length && !isSearching)" class="empty-state">
      <IconSearchSparkle24Regular class="empty-icon" />
      <MessageBanner v-if="dbNotFound" class="empty-error" :message="DB_NOT_FOUND_MESSAGE" :dismissible="false" />
      <MessageBanner
        v-else-if="indexBuildFailed"
        class="empty-error"
        :message="INDEX_BUILD_FAILED_MESSAGE"
        :dismissible="false"
      />
      <MessageBanner
        v-else-if="searchError"
        class="empty-error"
        :message="SEARCH_ERROR_MESSAGES[searchError]"
        :dismissible="false"
      />
      <span v-else-if="hasSearched && !results.length" class="empty-msg ui-chrome">{{ $t('fullTextSearch.noResults') }}</span>
    </div>
    <template v-else>
      <BookViewAbbrevTooltip
        v-if="abbrevTooltip"
        :key="abbrevTooltip.id"
        :data="abbrevTooltip"
      />
      <BookViewScrollbarTocTooltip v-if="scrollbarTocTooltip" :data="scrollbarTocTooltip" />
      <WindowedScroller
        ref="windowedScroller"
        class="scroller"
        :count="shownResults.length"
        :initial-index="openingIndex"
        :initial-offset-px="initialScrollOffset ?? 0"
        :ensure-rows="ensureRows"
        :row-source-version="shownGeneration"
        :pinned-indexes="pinnedRowIndexes"
        tabindex="0"
        :style="{ fontSize: `${fontPx}px` }"
        @opened="onScrollerOpened"
      >
        <template #row="{ index }">
          <div v-if="shownResults[index]" class="result-item">
            <div
              class="result-header"
              :title="resultTitle(shownResults[index]!)"
              @click="emit('resultClick', shownResults[index]!, wantsNewTab($event))"
              @auxclick.middle="emit('resultClick', shownResults[index]!, wantsNewTab($event))"
            >
              <span class="book-title">{{ bookTitleOf(shownResults[index]!) }}</span>
              <span v-if="tocPathOf(shownResults[index]!)" class="sep">›</span>
              <span v-if="tocPathOf(shownResults[index]!)" class="toc-text">{{
                tocPathOf(shownResults[index]!)
              }}</span>
              <button
                v-if="previewOf(shownResults[index]!)"
                class="preview-recenter-btn"
                :title="$t('fullTextSearch.list.backToResultLine')"
                @click.stop="recenterPreview(shownResults[index]!)"
              >
                <IconArrowReset24Regular />
              </button>
              <button
                class="preview-toggle-btn"
                :title="previewOf(shownResults[index]!) ? $t('fullTextSearch.list.closePreview') : $t('fullTextSearch.list.showMore')"
                @click.stop="togglePreview(shownResults[index]!)"
              >
                <IconDismiss24Regular v-if="previewOf(shownResults[index]!)" />
                <IconEye24Regular v-else />
              </button>
            </div>
            <FullTextSearchResultPreview
              v-if="previewOf(shownResults[index]!)"
              :ref="(el) => setPreviewRef(shownResults[index]!.lineId, el)"
              :state="previewOf(shownResults[index]!)!"
              :render-html="renderSnippet"
              :load-above="() => loadAbove(shownResults[index]!)"
              :load-below="() => loadBelow(shownResults[index]!)"
              :reseed="() => reseedPreview(shownResults[index]!)"
            />
            <!-- Never a placeholder: the scroller fetched this row's snippet before mounting it. -->
            <!-- eslint-disable-next-line vue/no-v-html -->
            <div v-else class="snippet" :style="teamimFontStyle(snippetHtmlOf(shownResults[index]!) ?? '')" v-html="renderSnippet(snippetHtmlOf(shownResults[index]!) ?? '')" />
          </div>
        </template>
      </WindowedScroller>
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
.empty-error {
  width: min(100% - 32px, 480px);
}
/* `.scroller` lands on the WindowedScroller's viewport (attributes fall through);
   the scroller's root fills the column beside its drawn bar. */
.results-wrap > :deep(.windowed-scroller) {
  flex: 1;
  min-height: 0;
}
.scroller {
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
