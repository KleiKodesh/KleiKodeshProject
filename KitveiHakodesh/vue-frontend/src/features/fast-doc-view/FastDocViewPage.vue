<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useDebounceFn, useStyleTag, useThrottleFn } from '@vueuse/core'
import WindowedScroller from '@/components/WindowedScroller.vue'
import SplitPane from '@/components/SplitPane.vue'
import SidePanel from '@/components/SidePanel.vue'
import MessageBanner from '@/components/MessageBanner.vue'
import LoadingAnimation from '@/components/LoadingAnimation.vue'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { useScopedKeys } from '@/composables/useTextSelectionKeys'
import { useVirtualizedTextSelection } from '@/composables/useVirtualizedTextSelection'
import { useZoomHandler, zoomIn, zoomOut, ZOOM_CONFIG } from '@/composables/useZoom'
import { useSettingsStore } from '@/stores/settingsStore'
import { useTabStore } from '@/stores/tabStore'
import { fastDocViewApi } from '@/webview-host/fastDocViewApi'
import { cancelLocalFileConversion, openFileInDefaultApp, restoreLocalFile } from '@/webview-host/bridge'
import { useI18n } from 'vue-i18n'
import { showToast } from '@/composables/useToast'
import { parseBookLink } from '@/utils/appDeepLink'
import { useHostSearchStore } from '@/stores/hostSearchStore'
import type { ConversionActionState } from '@/components/ConversionActionButton.vue'
import { useFastDocViewDocument } from './useFastDocViewDocument'
import { buildFastDocViewOutline, type FastDocViewOutlineNode } from './fastDocViewOutline'
import FastDocViewToolbar from './FastDocViewToolbar.vue'
import FastDocViewToc from './FastDocViewToc.vue'
import FastDocViewSearchBar from './FastDocViewSearchBar.vue'
import { useFastDocViewSearch } from './useFastDocViewSearch'
import { highlightSearchMatches } from '@/utils/searchMatchHighlight'
import { useSidePanelLayout } from '@/composables/useSidePanelLayout'
import FastDocViewNotesPane, { fastDocViewNoteKey } from './FastDocViewNotesPane.vue'
import type { FastDocViewPlacedNote } from './useFastDocViewDocument'

const paneNavigation = usePaneNavigation()
const tabStore = useTabStore()
const settingsStore = useSettingsStore()
const tabId = paneNavigation.activeTabId
// Read once: AppPageView keys this page by tab and file, so another file remounts it.
const filePath = paneNavigation.activeTab.localFilePath ?? null

const windowedScroller = ref<InstanceType<typeof WindowedScroller> | null>(null)
const scrollerElement = ref<HTMLElement | null>(null)
const renderedRange = computed(() => windowedScroller.value?.renderedRange ?? { start: 0, end: 0 })

// The retained ranges are read only when blocks are stored, after setup has run.
const fastDocument = useFastDocViewDocument({
  api: fastDocViewApi,
  retainedRanges: () => {
    const ranges: (readonly [number, number])[] = [[renderedRange.value.start, renderedRange.value.end]]
    for (const index of pinnedRowIndexes.value) ranges.push([index, index + 1])
    const selection = selectedRowIndexRange()
    if (selection) ranges.push([selection.firstRowIndex, selection.lastRowIndex + 1])
    return ranges
  },
})

const { pinnedRowIndexes, selectedRowIndexRange } = useVirtualizedTextSelection(
  scrollerElement,
  () => `${fastDocument.contentVersion.value}:${fastDocument.blockCount.value}`,
)

// ── Position and zoom (per tab, in TabState) ─────────────────────────────────
const initialIndex = ref<number | undefined>(undefined)
const initialOffsetPx = ref(0)
const zoom = ref<number>(ZOOM_CONFIG.DEFAULT)
const stateReady = ref(false)

/**
 * Tab view state writes, one at a time: each reads the stored state and writes it back with its
 * fields, so two interleaved writers (position, zoom, notes layout) would drop each other's.
 */
let tabStateWrites: Promise<void> = Promise.resolve()
function patchTabState(patch: Record<string, unknown>): Promise<void> {
  tabStateWrites = tabStateWrites.then(async () => {
    const existing = (await tabStore.getTabViewState(tabId)) ?? {}
    tabStore.setTabViewState(tabId, { ...existing, ...patch })
  }).catch(() => {})
  return tabStateWrites
}

/** Saves where the reader is now (block + offset) into the tab's view state. */
async function savePositionNow() {
  const first = windowedScroller.value?.readFirstVisible(0)
  if (!first) return
  await patchTabState({
    fastDocViewBlockIndex: first.index,
    fastDocViewBlockOffset: Math.round(first.offsetWithinRow),
  })
}
const savePosition = useDebounceFn(savePositionNow, 300)

async function saveZoom() {
  await patchTabState({ fastDocViewZoom: zoom.value !== ZOOM_CONFIG.DEFAULT ? zoom.value : undefined })
}
function setZoom(value: number) {
  zoom.value = value
  void saveZoom()
}
const zoomModel = computed({ get: () => zoom.value, set: setZoom })
useZoomHandler({ zoom: zoomModel, target: scrollerElement, keyboard: true })
const fontPx = computed(() => (zoom.value / 100) * (settingsStore.fontSize / 100) * 15)

// Page text width: each block's column is as wide as its section's text area in Word (page
// width minus margins), scaled by how much larger than the document's own default size the
// text is shown - so lines break at the same words at any zoom. A narrow pane fills its width.
/** The document's default font size in px (the library stylesheet's .word-document size; Word's 12 pt without one). */
const documentFontPx = computed(() => {
  const match = /\.word-document\s*\{[^}]*font-size:\s*([\d.]+)pt/.exec(fastDocument.summary.value?.stylesheet ?? '')
  const points = match ? Number(match[1]) : 12
  return (points > 0 ? points : 12) * (96 / 72)
})
/** The column style of block `index`: its section's width (twips / 15 = CSS px at 96 dpi) at the text's scale. */
function blockColumnStyle(index: number): Record<string, string> | undefined {
  const twips = fastDocument.textWidthTwipsAt(index)
  if (twips == null) return undefined
  const widthPx = (twips / 15) * (fontPx.value / documentFontPx.value)
  return { '--lines-content-max-width': `calc(${widthPx.toFixed(1)}px + 2 * var(--fast-doc-view-column-padding))` }
}

useScopedKeys(scrollerElement, { onCtrlF: () => openSearch() })

// In-document search (Ctrl+F): the service finds hit blocks in bounded steps from the reader's
// block, wrapping; matches are highlighted inside the rendered blocks only.
const searchVisible = ref(false)
const search = useFastDocViewSearch({
  api: fastDocViewApi,
  documentId: () => fastDocument.summary.value?.documentId,
  startBlock: () => windowedScroller.value?.readFirstVisible(0)?.index ?? 0,
  onFirstHit: (block) => void goToSearchHit(block),
})
function openSearch() {
  searchVisible.value = true
}
function closeSearch() {
  searchVisible.value = false
  search.clear()
  scrollerElement.value?.focus({ preventScroll: true })
}
/** Lands on a hit block and marks its first match as the current one. */
async function goToSearchHit(block: number | undefined) {
  if (block == null) return
  await windowedScroller.value?.scrollToIndex(block)
  const row = windowedScroller.value?.rowElement(block) as HTMLElement | null | undefined
  scrollerElement.value?.querySelectorAll('mark.search-match.current').forEach((mark) => mark.classList.remove('current'))
  const mark = row?.querySelector('mark.search-match')
  if (mark) {
    mark.classList.add('current')
    mark.scrollIntoView({ block: 'nearest' })
  }
}
/** A block's HTML with the search query's matches marked (only while searching, only rendered blocks). */
function blockHtml(index: number): string {
  const html = fastDocument.blockAt(index)?.html ?? ''
  return search.query.value.trim() && searchVisible.value ? highlightSearchMatches(html, search.query.value, { ignoreCase: true }) : html
}

onMounted(async () => {
  scrollerElement.value = windowedScroller.value?.scrollerElement ?? null
  const saved = await tabStore.getTabViewState(tabId)
  if (saved?.fastDocViewZoom != null) zoom.value = saved.fastDocViewZoom
  if (saved?.fastDocViewNotesVisible === false) notesVisible.value = false
  if (saved?.fastDocViewNotesFraction != null) notesFraction.value = saved.fastDocViewNotesFraction
  initialIndex.value = saved?.fastDocViewBlockIndex ?? 0
  initialOffsetPx.value = saved?.fastDocViewBlockOffset ?? 0
  stateReady.value = true
  if (filePath) await fastDocument.open(filePath, { reachIndex: initialIndex.value })
})

function onScrollerMounted(instance: unknown) {
  windowedScroller.value = instance as InstanceType<typeof WindowedScroller> | null
  scrollerElement.value = windowedScroller.value?.scrollerElement ?? null
}

// ── The library's stylesheet, scoped to this document's content root ───────
const contentRootId = `fast-doc-view-${tabId}`
const scopedStylesheet = computed(() => {
  const css = fastDocument.summary.value?.stylesheet
  return css ? `@scope ([data-fast-doc-view-root="${contentRootId}"]) {\n${css}\n}` : ''
})
useStyleTag(scopedStylesheet)

const isRightToLeft = computed(() => fastDocument.summary.value?.isRightToLeft ?? true)

// ── Side panel (TOC) and notes pane ──────────────────────────────────────────
const pageElement = ref<HTMLElement | null>(null)
// The TOC panel: inline with a sash on a wide page (0.1..0.5 of the row), overlay on a narrow one - as in the book view.
const sidePanelResizeArea = ref<HTMLElement | null>(null)
const {
  isOverlay: sidePanelIsOverlay,
  panelStyle: sidePanelStyle,
  onDividerPointerDown: onSidePanelDividerPointerDown,
  onResizePointerMove: onSidePanelResizePointerMove,
  onResizePointerUp: onSidePanelResizePointerUp,
} = useSidePanelLayout({ pageElement, resizeArea: sidePanelResizeArea })
const tocVisible = ref(false)
const tocToggleButton = ref<HTMLElement | null>(null)
const outline = computed(() => buildFastDocViewOutline(fastDocument.headings.value))
/** The block at the top of the view, followed while scrolling (throttled) - the TOC's current heading. */
const firstVisibleIndex = ref(0)
const trackFirstVisible = useThrottleFn(() => {
  firstVisibleIndex.value = windowedScroller.value?.readFirstVisible(0)?.index ?? firstVisibleIndex.value
}, 150, true)
function onScrollerScroll() {
  void savePosition()
  void trackFirstVisible()
}
/** The last heading at or above the reader's position (headings are in block order). */
const activeOutlineNodeId = computed(() => {
  const nodes = outline.value
  let low = 0
  let high = nodes.length - 1
  let found: number | undefined
  while (low <= high) {
    const middle = (low + high) >> 1
    if (nodes[middle]!.blockIndex <= firstVisibleIndex.value) { found = nodes[middle]!.id; low = middle + 1 } else high = middle - 1
  }
  return found
})
function toggleToc(toggleButton: HTMLElement) {
  tocToggleButton.value = toggleButton
  tocVisible.value = !tocVisible.value
}
function onOutlineSelect(node: FastDocViewOutlineNode) {
  void windowedScroller.value?.scrollToIndex(node.blockIndex)
  if (sidePanelIsOverlay.value) tocVisible.value = false
}

const hasNotes = computed(() => {
  const summary = fastDocument.summary.value
  return !!summary && summary.footnoteCount + summary.endnoteCount > 0
})
const notesVisible = ref(true)
/** The text pane's share of the split above the notes pane (SplitPane's top fraction). */
const notesFraction = ref<number | undefined>(undefined)
/** The notes pane's text size: the commentary's (commentary font setting at the page zoom), as in the book view. */
const notesFontPx = computed(() => (zoom.value / 100) * (settingsStore.commentaryFontSize / 100) * 15)
async function saveNotesLayout() {
  await patchTabState({
    fastDocViewNotesVisible: notesVisible.value ? undefined : false,
    fastDocViewNotesFraction: notesFraction.value,
  })
}
// Debounced: dragging the split updates the fraction on every pointer move.
const saveNotesLayoutSoon = useDebounceFn(saveNotesLayout, 300)
watch([notesVisible, notesFraction], () => void saveNotesLayoutSoon())
// "Open as PDF": the same file through the existing Word -> PDF conversion (an explicit
// choice; FastDocView is the default). The tab becomes a PDF (or HTML fallback) tab.
const { t } = useI18n()
/** idle -> busy (converting; the button cancels) -> done (a check; the click opens the result). */
const pdfState = ref<ConversionActionState>('idle')
let convertedPdf: { url: string; kind?: 'pdf' | 'html' } | null = null
/** Only the Word family converts to PDF (Word itself does the conversion); not .txt. */
const canOpenAsPdf = !!filePath && !filePath.toLowerCase().endsWith('.txt')
/** Bumped by a cancel: the conversion that was running no longer opens or reports anything. */
let openAsPdfGeneration = 0
async function openAsPdf() {
  if (!filePath || pdfState.value !== 'idle') return
  pdfState.value = 'busy'
  showToast(t('fastDocView.convertingToast'))
  const generation = ++openAsPdfGeneration
  const restored = await restoreLocalFile(filePath)
  if (generation !== openAsPdfGeneration) return
  if (!restored?.url) {
    pdfState.value = 'idle'
    showToast(t('fastDocView.openAsPdfFailed'), { variant: 'error', duration: 6000 })
    return
  }
  convertedPdf = { url: restored.url, kind: restored.kind }
  pdfState.value = 'done'
}
/** Opens the converted PDF in this tab: a navigation, so Back returns here at the same block. */
async function openConvertedPdf() {
  if (!filePath || !convertedPdf) return
  const fileName = filePath.substring(Math.max(filePath.lastIndexOf('\\'), filePath.lastIndexOf('/')) + 1)
  await savePositionNow()
  tabStore.updateTab(tabId, {
    route: convertedPdf.kind === 'html' ? '/html-view' : '/pdf-view',
    localFileName: fileName,
    localFilePath: filePath,
    localFileVirtualUrl: convertedPdf.url,
    isOtzariaAddin: false,
  })
}
/** Shell-opens the file with the program registered for its type (hosted: in the app's own process). */
async function openInDefaultApp() {
  if (!filePath) return
  if (!(await openFileInDefaultApp(filePath))) showToast(t('fastDocView.openInDefaultAppFailed'), { variant: 'error', duration: 6000 })
}
// The file changed on disk (the document reopened): a PDF converted from the old file is stale.
watch(() => fastDocument.contentVersion.value, () => {
  if (pdfState.value !== 'done') return
  convertedPdf = null
  pdfState.value = 'idle'
})
function cancelOpenAsPdf() {
  if (!filePath || pdfState.value !== 'busy') return
  openAsPdfGeneration++
  pdfState.value = 'idle'
  cancelLocalFileConversion(filePath)
}

// Dev-only test hook (stripped from production builds, like devTestHook.ts): plain data for
// Playwright checks, and the two actions they drive. The last mounted page owns it.
if (import.meta.env.DEV) {
  const hook = {
    state: () => {
      const summary = fastDocument.summary.value
      const first = windowedScroller.value?.readFirstVisible(0)
      return {
        documentId: summary?.documentId ?? null,
        blockCount: fastDocument.blockCount.value,
        knownBlockCount: summary?.knownBlockCount ?? 0,
        isIndexComplete: fastDocument.isIndexComplete.value,
        footnoteCount: summary?.footnoteCount ?? 0,
        endnoteCount: summary?.endnoteCount ?? 0,
        headingCount: fastDocument.headings.value.length,
        hasWeights: fastDocument.rowWeights.value != null,
        firstVisible: first ? { index: first.index, offsetWithinRow: first.offsetWithinRow } : null,
        renderedRange: { start: renderedRange.value.start, end: renderedRange.value.end },
        cachedBlocks: fastDocument.cachedBlockCount(),
        zoom: zoom.value,
        notesPaneOpen: notesVisible.value && hasNotes.value,
      }
    },
    scrollToBlock: (index: number) => windowedScroller.value?.scrollToIndex(index),
    reopen: () => fastDocument.reopen(),
  }
  const target = window as unknown as { __fastDocViewTest?: typeof hook }
  onMounted(() => { target.__fastDocViewTest = hook })
  onBeforeUnmount(() => { if (target.__fastDocViewTest === hook) delete target.__fastDocViewTest })
}

const visibleNotes = computed(() => fastDocument.notesInRange(renderedRange.value.start, renderedRange.value.end))

// Footnotes linked both ways. A reference in the text (<sup class="note-reference"
// data-note-kind data-note-number>, rendered by the library) opens the notes pane on its note;
// a note's label jumps the text to the block holding its reference and flashes it.
const activeNoteKey = ref<string | null>(null)
function onContentClick(event: MouseEvent) {
  if (onLinkClick(event)) return
  const reference = (event.target as HTMLElement | null)?.closest<HTMLElement>('.note-reference[data-note-kind]')
  if (!reference) return
  activeNoteKey.value = fastDocViewNoteKey(reference.dataset.noteKind ?? '', Number(reference.dataset.noteNumber ?? 0), (reference.textContent ?? '').trim())
  notesVisible.value = true
}
const REFERENCE_FLASH_MS = 2000

/** Briefly marks an element the reader was jumped to (a note reference, a bookmark target). */
function flash(element: Element | null | undefined) {
  if (!element) return
  element.scrollIntoView({ block: 'nearest' })
  element.classList.add('note-reference--current')
  setTimeout(() => element.classList.remove('note-reference--current'), REFERENCE_FLASH_MS)
}

// Links in the document. Internal ones (a TOC field entry, a cross-reference, w:hyperlink
// w:anchor) point at "#bookmark-<name>": the block is asked of the index and reached by a
// windowed jump - the browser never follows the # (the target may not be mounted). Book
// links (otzaria:// zayit:// kitveihakodeshapp://) open like a host deep link. Web and mail
// links keep their default (they open outside the app).
const hostSearchStore = useHostSearchStore()
function onLinkClick(event: MouseEvent): boolean {
  const anchor = (event.target as HTMLElement | null)?.closest<HTMLAnchorElement>('a[href]')
  const href = anchor?.getAttribute('href') ?? ''
  if (!anchor || !href) return false
  if (href.startsWith('#bookmark-')) {
    event.preventDefault()
    void goToBookmark(href)
    return true
  }
  const bookLink = parseBookLink(href)
  if (bookLink) {
    event.preventDefault()
    void hostSearchStore.openBookLink(bookLink)
    return true
  }
  return false
}
async function goToBookmark(href: string) {
  const documentId = fastDocument.summary.value?.documentId
  if (!documentId) return
  let name: string
  try { name = decodeURIComponent(href.slice('#bookmark-'.length)) } catch { name = href.slice('#bookmark-'.length) }
  const blockIndex = await fastDocViewApi.bookmark(documentId, name).catch(() => null)
  if (blockIndex == null) {
    showToast(t('fastDocView.bookmarkNotFound'))
    return
  }
  // An index still building found it past the blocks the scroller knows: wait for the count to reach it.
  if (blockIndex >= fastDocument.blockCount.value) {
    await new Promise<void>((resolve) => {
      const timer = setTimeout(() => { stop(); resolve() }, 15000)
      const stop = watch(fastDocument.blockCount, (count) => {
        if (count > blockIndex) { clearTimeout(timer); stop(); resolve() }
      })
    })
  }
  await windowedScroller.value?.scrollToIndex(blockIndex)
  const row = windowedScroller.value?.rowElement(blockIndex) as HTMLElement | null | undefined
  flash([...(row?.querySelectorAll<HTMLElement>('.bookmark[id]') ?? [])].find((element) => element.id === href.slice(1)) ?? row)
}
async function goToNoteReference(note: FastDocViewPlacedNote) {
  activeNoteKey.value = fastDocViewNoteKey(note.kind, note.number, note.label)
  await windowedScroller.value?.scrollToIndex(note.blockIndex)
  const row = windowedScroller.value?.rowElement(note.blockIndex) as HTMLElement | null | undefined
  const references = [...(row?.querySelectorAll<HTMLElement>(`.note-reference[data-note-kind="${note.kind}"]`) ?? [])]
  const reference = references.find((element) => Number(element.dataset.noteNumber) === note.number && (element.textContent ?? '').trim() === note.label) ?? references[0]
  flash(reference)
}
</script>

<template>
  <div ref="pageElement" class="fast-doc-view">
    <FastDocViewToolbar
      :toc-visible="tocVisible"
      :notes-visible="notesVisible && hasNotes"
      :has-headings="outline.length > 0"
      :has-notes="hasNotes"
      :zoom="zoom"
      @toggle-toc="toggleToc"
      @toggle-notes="notesVisible = !notesVisible"
      @zoom-in="setZoom(zoomIn(zoom))"
      @zoom-out="setZoom(zoomOut(zoom))"
      :pdf-state="pdfState"
      :can-open-as-pdf="canOpenAsPdf"
      @open-as-pdf="openAsPdf"
      @cancel-open-as-pdf="cancelOpenAsPdf"
      @open-converted-pdf="openConvertedPdf"
      @open-in-default-app="openInDefaultApp"
      :search-visible="searchVisible"
      @toggle-search="searchVisible ? closeSearch() : openSearch()"
    />
    <div
      ref="sidePanelResizeArea"
      class="fast-doc-view-body"
      @pointermove="onSidePanelResizePointerMove"
      @pointerup="onSidePanelResizePointerUp"
    >
      <SidePanel
        v-if="tocVisible"
        :toggle-button-el="tocToggleButton"
        :is-overlay="sidePanelIsOverlay"
        class="fast-doc-view-toc"
        :style="sidePanelIsOverlay ? undefined : sidePanelStyle"
        @close="tocVisible = false"
      >
        <FastDocViewToc :outline="outline" :active-node-id="activeOutlineNodeId" :is-right-to-left="isRightToLeft" @select="onOutlineSelect" />
      </SidePanel>
      <div v-if="tocVisible && !sidePanelIsOverlay" class="sash sash-v" @pointerdown="onSidePanelDividerPointerDown" />
      <div class="fast-doc-view-main">
        <FastDocViewSearchBar
          v-if="searchVisible"
          :initial-query="search.query.value"
          :current-hit="search.currentHit.value"
          :hit-count="search.hitCount.value"
          :is-searching="search.isSearching.value"
          @query="search.run"
          @next="goToSearchHit(search.step(1))"
          @prev="goToSearchHit(search.step(-1))"
          @close="closeSearch"
        />
        <MessageBanner
          v-if="fastDocument.indexBuildFailed.value"
          severity="warning"
          :message="$t('fastDocView.indexBuildFailed')"
          :dismissible="true"
        />
        <MessageBanner
          v-if="!filePath"
          severity="info"
          :message="$t('fastDocView.noFileOpen')"
          :dismissible="false"
        />
        <MessageBanner
          v-else-if="fastDocument.error.value"
          severity="error"
          :message="$t('fastDocView.loadError')"
          :details="String(fastDocument.error.value)"
          :dismissible="false"
        />
        <LoadingAnimation v-else-if="fastDocument.isLoading.value || !stateReady" />
        <SplitPane v-else-if="fastDocument.summary.value" v-model="notesFraction" :bottom-visible="notesVisible && hasNotes">
          <template #top>
            <div
              class="fast-doc-view-content"
              :dir="isRightToLeft ? 'rtl' : 'ltr'"
              :data-fast-doc-view-root="contentRootId"
              @click="onContentClick"
            >
              <WindowedScroller
                :ref="onScrollerMounted"
                class="fast-doc-view-scroller"
                :count="fastDocument.blockCount.value"
                :initial-index="initialIndex"
                :initial-offset-px="initialOffsetPx"
                :ensure-rows="fastDocument.ensureBlocks"
                :row-source-version="fastDocument.contentVersion.value"
                :pinned-indexes="pinnedRowIndexes"
                :landmark-tiers="fastDocument.landmarkTiers.value"
                :row-weights="fastDocument.rowWeights.value"
                :style="{ fontSize: fontPx + 'px' }"
                tabindex="0"
                @scroll="onScrollerScroll"
              >
                <template #row="{ index }">
                  <div
                    v-if="fastDocument.blockAt(index)"
                    class="reading-column-line fast-doc-view-block"
                    :style="blockColumnStyle(index)"
                    v-html="blockHtml(index)"
                  />
                </template>
              </WindowedScroller>
            </div>
          </template>
          <template #bottom>
            <FastDocViewNotesPane
              :notes="visibleNotes"
              :is-right-to-left="isRightToLeft"
              :active-key="activeNoteKey"
              :font-px="notesFontPx"
              @close="notesVisible = false"
              @go-to-reference="goToNoteReference"
            />
          </template>
        </SplitPane>
      </div>
    </div>
  </div>
</template>

<style scoped>
.fast-doc-view {
  position: relative;
  display: flex;
  flex-direction: column;
  height: 100%;
  background: var(--bg-primary);
}
.fast-doc-view-body {
  position: relative;
  display: flex;
  flex: 1;
  min-height: 0;
}
.fast-doc-view-toc {
  min-width: 0;
}
.fast-doc-view-main {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
}
.fast-doc-view-content {
  height: 100%;
  position: relative;
  /* the reading column's side padding (readingColumn.css), added to the page text width */
  --fast-doc-view-column-padding: 12px;
}
@container app-shell (min-width: 600px) {
  .fast-doc-view-content {
    --fast-doc-view-column-padding: 22px;
  }
}
.fast-doc-view-scroller :deep(.windowed-scroller-content) {
  padding-block-end: 24px;
}
/* An empty paragraph keeps one line, as in Word (and an empty block is one line too): a
   zero-height row could never be scrolled into view, so the document's last rows - often
   empty - looked cut off at the end. */
/* note references in the text: clickable (they open their note in the notes pane) */
.fast-doc-view-block :deep(.note-reference) {
  cursor: pointer;
  color: var(--accent-color);
}
.fast-doc-view-content :deep(.note-reference--current) {
  background: color-mix(in srgb, var(--accent-color) 25%, transparent);
  border-radius: 2px;
}
.fast-doc-view-block:empty::before,
.fast-doc-view-block :deep(:is(p, h1, h2, h3, h4, h5, h6):empty::before) {
  content: '\200b';
}
/* A text file's line can be an empty run (<div class="text-line"><b></b></div>), which
   :empty does not catch: a zero-width space at its end gives it a line's height and adds
   nothing to a line that has text. */
.fast-doc-view-block :deep(.text-line::after) {
  content: '\200b';
}
</style>
