<script setup lang="ts">
import { ref, watch } from 'vue'
import WindowedScroller from '@/components/WindowedScroller.vue'
import type { WindowedScrollerApi } from '@/components/windowedScrollerTypes'
import {
  IconDocument20Filled,
  IconDocumentPdf20Filled,
  IconDocumentText20Filled,
  IconDocumentGlobe20Filled,
  IconWindowArrowUp20Regular,
  IconPuzzlePiece20Regular,
  IconFolderOpen20Regular,
  IconCopy20Regular,
} from '@iconify-prerendered/vue-fluent'
import { useI18n } from 'vue-i18n'
import { showToast } from '@/composables/useToast'
import { useInputListNavigation } from '@/composables/useInputListNavigation'
import { wantsNewTab, withNewTabHint } from '@/composables/useOpenInNewTab'
import { openFileInDefaultApp, revealFileInFolder } from '@/webview-host/bridge'
import type { LocalFileSearchResult } from './useLocalFileSearch'

const props = defineProps<{
  items: LocalFileSearchResult[]
  searching: boolean
}>()

const emit = defineEmits<{ openFile: [LocalFileSearchResult, boolean?] }>()

const windowedScroller = ref<WindowedScrollerApi | null>(null)
// New results are a new list: re-keying the scroller starts it back at the top.
const generation = ref(0)

// Combobox model: focus stays in the page's search input; the page forwards its
// keydown events here (onSearchInputKeydown) and the highlight moves through
// the results.
const { activeIndex, onKeydown: onSearchInputKeydown } = useInputListNavigation({
  getCount: () => props.items.length,
  onActivate: (index, openInNewTab) => onSelect(props.items[index]!, openInNewTab),
  // Windowed: most rows have no element, so the scroller reveals the highlighted one.
  scrollIntoView: (index) => windowedScroller.value?.revealIndex(index),
})

// New results make the old highlight point at a different item — drop it.
watch(
  () => props.items,
  () => {
    activeIndex.value = -1
    generation.value++
  },
)

function onSelect(item: LocalFileSearchResult, openInNewTab = false) {
  emit('openFile', item, openInNewTab)
}

function selectItem(index: number, event?: MouseEvent) {
  activeIndex.value = index
  onSelect(props.items[index]!, wantsNewTab(event))
}

// Hand the file off to the OS's default program (Word for .docx, Acrobat for .pdf, …).
// Guarded so the row's open-in-viewer click never also fires.
function openInDefaultApp(item: LocalFileSearchResult, event: MouseEvent) {
  event.stopPropagation()
  void openFileInDefaultApp(item.fullPath)
}

const { t } = useI18n()

// Explorer window with the file selected.
function openFileLocation(item: LocalFileSearchResult, event: MouseEvent) {
  event.stopPropagation()
  void revealFileInFolder(item.fullPath)
}

async function copyPath(item: LocalFileSearchResult, event: MouseEvent) {
  event.stopPropagation()
  try {
    await navigator.clipboard.writeText(item.fullPath)
    showToast(t('localFileSearch.pathCopied'))
  } catch {
    showToast(t('localFileSearch.copyPathFailed'), { variant: 'error' })
  }
}

type FileIconInfo = { component: unknown; color: string }

function getFileIcon(item: LocalFileSearchResult): FileIconInfo {
  if (item.addinName) return { component: IconPuzzlePiece20Regular, color: '#7b5ea7' }
  const extension = item.fileName.toLowerCase().split('.').pop()
  switch (extension) {
    case 'pdf':
      return { component: IconDocumentPdf20Filled, color: '#F40F02' }
    case 'html':
    case 'htm':
    case 'mht':
    case 'mhtml':
      return { component: IconDocumentGlobe20Filled, color: '#0097fb' }
    case 'txt':
      return { component: IconDocumentText20Filled, color: '#9e9e9e' }
    default:
      return { component: IconDocument20Filled, color: '#3478f6' }
  }
}

// One formatter for every row: toLocaleDateString builds a new Intl formatter per call,
// which was the dominant cost of re-rendering the mounted rows on each highlight change.
const dateFormatter = new Intl.DateTimeFormat('he-IL', { day: '2-digit', month: '2-digit', year: '2-digit' })

function formatDate(unixMs: number): string {
  if (!unixMs) return ''
  return dateFormatter.format(new Date(unixMs))
}

function getTooltip(item: LocalFileSearchResult): string {
  return withNewTabHint(
    [item.fileName, item.path, formatDate(item.modifiedDate)].filter(Boolean).join('\n'),
  )
}

defineExpose({
  onSearchInputKeydown: (event: KeyboardEvent): boolean => onSearchInputKeydown(event),
})
</script>

<template>
  <div dir="rtl" class="scroller">
    <!-- Windowed: only the rows near the viewport exist (5000 results = ~36 rows, not 120k nodes). -->
    <WindowedScroller ref="windowedScroller" :key="generation" :count="items.length" class="windowed">
      <template #row="{ index }">
        <div
          v-if="items[index]"
          v-memo="[items[index], activeIndex === index]"
          class="file-item"
          data-nav-item
          :class="{ 'is-focused': activeIndex === index }"
          :title="getTooltip(items[index]!)"
          @click="selectItem(index, $event)"
          @auxclick.middle="selectItem(index, $event)"
          @mousedown.prevent
        >
          <span class="icon" :style="{ color: getFileIcon(items[index]!).color }">
            <component :is="getFileIcon(items[index]!).component" />
          </span>
          <span class="item-text">
            <span class="item-title-row">
              <span class="item-title">{{
                items[index]!.addinName || items[index]!.displayName || items[index]!.fileName
              }}</span>
            </span>
          </span>
          <span class="item-side">
            <span class="item-meta-row">
              <span class="item-actions">
                <button
                  class="open-in-app-button"
                  type="button"
                  :title="$t('localFileSearch.copyPath')"
                  @click="copyPath(items[index]!, $event)"
                  @auxclick.stop
                  @mousedown.prevent.stop
                >
                  <IconCopy20Regular />
                </button>
                <button
                  class="open-in-app-button"
                  type="button"
                  :title="$t('localFileSearch.openFileLocation')"
                  @click="openFileLocation(items[index]!, $event)"
                  @auxclick.stop
                  @mousedown.prevent.stop
                >
                  <IconFolderOpen20Regular />
                </button>
                <button
                  class="open-in-app-button"
                  type="button"
                  :title="$t('localFileSearch.openInDefaultApp')"
                  @click="openInDefaultApp(items[index]!, $event)"
                  @auxclick.stop
                  @mousedown.prevent.stop
                >
                  <IconWindowArrowUp20Regular />
                </button>
              </span>
              <span
                v-if="items[index]!.modifiedDate"
                class="item-date-pill"
              >{{ formatDate(items[index]!.modifiedDate) }}</span>
            </span>
            <span class="item-path" dir="ltr">{{ items[index]!.path }}</span>
          </span>
        </div>
      </template>
    </WindowedScroller>
  </div>
</template>

<style scoped>
.scroller {
  height: 100%;
}
.scroller > :deep(.windowed) {
  height: 100%;
}
.file-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 0 12px;
  min-height: 44px;
  cursor: pointer;
  box-sizing: border-box;
}
.file-item:hover {
  background: var(--hover-bg);
}
.file-item:active {
  background: var(--active-bg);
}
.file-item.is-focused {
  background: var(--hover-bg);
}
.icon {
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  font-size: 20px;
}
.icon svg {
  width: 20px;
  height: 20px;
  color: inherit !important;
}
.item-text {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
  flex: 1;
}
.item-title-row {
  display: flex;
  align-items: center;
  gap: 6px;
  overflow: hidden;
}
.item-title {
  font-size: 14px;
  color: var(--text-primary);
  line-height: 1.3;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  min-width: 0;
  flex-shrink: 1;
}
/* Date pill over the full path, one block at the far inline-end; the title block beside it
   is centered vertically in the row by .file-item's align-items:center. */
.item-side {
  display: flex;
  flex-direction: column;
  align-items: flex-end; /* physical left in RTL */
  gap: 2px;
  min-width: 0;
  max-width: 45%;
  flex-shrink: 1;
}
.item-meta-row {
  display: flex;
  align-items: center;
  gap: 6px;
}
.item-path {
  display: block;
  max-width: 100%;
  font-size: 11px;
  color: var(--text-secondary);
  line-height: 1.3;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  direction: ltr;
  text-align: start;
}
/* Date pill: last in the title row (far physical left in RTL), directly above the path's
   end, and outside the hover-only row actions so it never moves when they appear. */
.item-date-pill {
  flex-shrink: 0;
  font-size: 10px;
  color: var(--text-secondary);
  background: color-mix(in srgb, var(--bg-secondary) 90%, transparent);
  border: 1px solid var(--border-color);
  border-radius: 999px;
  padding: 0 6px;
  line-height: 1.5;
  white-space: nowrap;
}
/* Row actions live INSIDE the text block, at the end of the title row after the date pill.
   Compact (22px) so they don't make the two-line row taller. */
.item-actions {
  display: flex;
  align-items: center;
  flex-shrink: 0;
  align-self: center;
  /* Hidden until the row is hovered or focused; opacity (not display) keeps the row's layout steady. */
  opacity: 0;
}
.file-item:hover .item-actions,
.file-item.is-focused .item-actions,
.file-item:focus-within .item-actions {
  opacity: 1;
}
.open-in-app-button {
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  width: 22px;
  height: 22px;
  padding: 0;
  border-radius: 4px;
  color: var(--text-secondary);
  transition: background 0.1s, color 0.1s;
}
.open-in-app-button svg {
  width: 16px;
  height: 16px;
  color: inherit;
}
.open-in-app-button:hover {
  background: color-mix(in srgb, var(--text-primary) 10%, transparent);
  color: var(--accent-color, #0078d4);
}
</style>
