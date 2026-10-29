<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useDebounceFn, useElementSize, useStyleTag } from '@vueuse/core'
import WindowedScroller from '@/components/WindowedScroller.vue'
import SplitPane from '@/components/SplitPane.vue'
import SidePanel from '@/components/SidePanel.vue'
import TreeView from '@/components/TreeView.vue'
import MessageBanner from '@/components/MessageBanner.vue'
import LoadingAnimation from '@/components/LoadingAnimation.vue'
import type { TreeNodeItem } from '@/components/treeTypes'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { useScopedKeys } from '@/composables/useTextSelectionKeys'
import { useVirtualizedTextSelection } from '@/composables/useVirtualizedTextSelection'
import { useZoomHandler, zoomIn, zoomOut, ZOOM_CONFIG } from '@/composables/useZoom'
import { useSettingsStore } from '@/stores/settingsStore'
import { useTabStore } from '@/stores/tabStore'
import { fastDocViewApi } from '@/webview-host/fastDocViewApi'
import { restoreLocalFile } from '@/webview-host/bridge'
import { useI18n } from 'vue-i18n'
import { useFastDocViewDocument } from './useFastDocViewDocument'
import { buildFastDocViewOutline, type FastDocViewOutlineNode } from './fastDocViewOutline'
import FastDocViewToolbar from './FastDocViewToolbar.vue'
import FastDocViewNotesPane from './FastDocViewNotesPane.vue'

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

const savePosition = useDebounceFn(async () => {
  const first = windowedScroller.value?.readFirstVisible(0)
  if (!first) return
  const existing = (await tabStore.getTabViewState(tabId)) ?? {}
  tabStore.setTabViewState(tabId, {
    ...existing,
    fastDocViewBlockIndex: first.index,
    fastDocViewBlockOffset: Math.round(first.offsetWithinRow),
  })
}, 300)

async function saveZoom() {
  const existing = (await tabStore.getTabViewState(tabId)) ?? {}
  tabStore.setTabViewState(tabId, {
    ...existing,
    fastDocViewZoom: zoom.value !== ZOOM_CONFIG.DEFAULT ? zoom.value : undefined,
  })
}
function setZoom(value: number) {
  zoom.value = value
  void saveZoom()
}
const zoomModel = computed({ get: () => zoom.value, set: setZoom })
useZoomHandler({ zoom: zoomModel, target: scrollerElement, keyboard: true })
const fontPx = computed(() => (zoom.value / 100) * (settingsStore.fontSize / 100) * 15)

useScopedKeys(scrollerElement)

onMounted(async () => {
  scrollerElement.value = windowedScroller.value?.scrollerElement ?? null
  const saved = await tabStore.getTabViewState(tabId)
  if (saved?.fastDocViewZoom != null) zoom.value = saved.fastDocViewZoom
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
const { width: pageWidth } = useElementSize(pageElement)
const sidePanelIsOverlay = computed(() => pageWidth.value < 520)
const tocVisible = ref(false)
const tocToggleButton = ref<HTMLElement | null>(null)
const outline = computed(() => buildFastDocViewOutline(fastDocument.headings.value))
function toggleToc(toggleButton: HTMLElement) {
  tocToggleButton.value = toggleButton
  tocVisible.value = !tocVisible.value
}
function onOutlineSelect(node: TreeNodeItem) {
  void windowedScroller.value?.scrollToIndex((node as FastDocViewOutlineNode).blockIndex)
  if (sidePanelIsOverlay.value) tocVisible.value = false
}

const hasNotes = computed(() => {
  const summary = fastDocument.summary.value
  return !!summary && summary.footnoteCount + summary.endnoteCount > 0
})
const notesVisible = ref(true)
// "Open as PDF": the same file through the existing Word -> PDF conversion (an explicit
// choice; FastDocView is the default). The tab becomes a PDF (or HTML fallback) tab.
const { t } = useI18n()
const openingAsPdf = ref(false)
/** Only the Word family converts to PDF (Word itself does the conversion); not .txt. */
const canOpenAsPdf = !!filePath && !filePath.toLowerCase().endsWith('.txt')
async function openAsPdf() {
  if (!filePath || openingAsPdf.value) return
  openingAsPdf.value = true
  try {
    const restored = await restoreLocalFile(filePath)
    if (!restored?.url) {
      window.alert(t('fastDocView.openAsPdfFailed'))
      return
    }
    const fileName = filePath.substring(Math.max(filePath.lastIndexOf('\\'),filePath.lastIndexOf('/')) + 1)
    tabStore.updateTab(tabId, {
      route: restored.kind === 'html' ? '/html-view' : '/pdf-view',
      localFileName: fileName,
      localFilePath: filePath,
      localFileVirtualUrl: restored.url,
      isOtzariaAddin: false,
    })
  } finally {
    openingAsPdf.value = false
  }
}

const visibleNotes = computed(() => fastDocument.notesInRange(renderedRange.value.start, renderedRange.value.end))
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
      :opening-as-pdf="openingAsPdf"
      :can-open-as-pdf="canOpenAsPdf"
      @open-as-pdf="openAsPdf"
    />
    <div class="fast-doc-view-body">
      <SidePanel
        v-if="tocVisible"
        :toggle-button-el="tocToggleButton"
        :is-overlay="sidePanelIsOverlay"
        class="fast-doc-view-toc"
        @close="tocVisible = false"
      >
        <TreeView :dir="isRightToLeft ? 'rtl' : 'ltr'" :nodes="outline" @select="onOutlineSelect" />
      </SidePanel>
      <div class="fast-doc-view-main">
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
        <SplitPane v-else-if="fastDocument.summary.value" :bottom-visible="notesVisible && hasNotes">
          <template #top>
            <div
              class="fast-doc-view-content"
              :dir="isRightToLeft ? 'rtl' : 'ltr'"
              :data-fast-doc-view-root="contentRootId"
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
                @scroll="savePosition"
              >
                <template #row="{ index }">
                  <div
                    v-if="fastDocument.blockAt(index)"
                    class="reading-column-line fast-doc-view-block"
                    v-html="fastDocument.blockAt(index)!.html"
                  />
                </template>
              </WindowedScroller>
            </div>
          </template>
          <template #bottom>
            <FastDocViewNotesPane :notes="visibleNotes" :is-right-to-left="isRightToLeft" />
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
  min-width: 200px;
  max-width: 40%;
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
}
.fast-doc-view-scroller :deep(.windowed-scroller-content) {
  padding-block-end: 24px;
}
</style>
