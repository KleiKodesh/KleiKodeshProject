<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import {
  IconDocument20Filled,
  IconDocumentPdf20Filled,
  IconDocumentText20Filled,
  IconDocumentGlobe20Filled,
  IconWindowArrowUp20Regular,
  IconPuzzlePiece20Regular,
} from '@iconify-prerendered/vue-fluent'
import { useInputListNavigation } from '@/composables/useInputListNavigation'
import { wantsNewTab, withNewTabHint } from '@/composables/useOpenInNewTab'
import { openFileInDefaultApp } from '@/webview-host/bridge'
import type { LocalFileSearchResult } from './useLocalFileSearch'

const props = defineProps<{
  items: LocalFileSearchResult[]
  searching: boolean
}>()

const emit = defineEmits<{ openFile: [LocalFileSearchResult, boolean?] }>()

const scrollElement = ref<HTMLElement | null>(null)

// Combobox model: focus stays in the page's search input; the page forwards its
// keydown events here (onSearchInputKeydown) and the highlight moves through
// the results.
const { activeIndex, onKeydown: onSearchInputKeydown } = useInputListNavigation({
  getCount: () => props.items.length,
  onActivate: (index, openInNewTab) => onSelect(props.items[index]!, openInNewTab),
  containerElement: scrollElement,
})

// New results make the old highlight point at a different item — drop it.
watch(
  () => props.items,
  () => {
    activeIndex.value = -1
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

function formatDate(unixMs: number): string {
  if (!unixMs) return ''
  const date = new Date(unixMs)
  return date.toLocaleDateString('he-IL', { day: '2-digit', month: '2-digit', year: '2-digit' })
}

function getTooltip(item: LocalFileSearchResult): string {
  return withNewTabHint(
    [item.fileName, item.path, formatDate(item.modifiedDate)].filter(Boolean).join('\n'),
  )
}

defineExpose({
  onSearchInputKeydown,
})
</script>

<template>
  <div dir="rtl" ref="scrollElement" class="scroller">
    <!-- A plain list: rows off screen are neither laid out nor painted (.file-item). -->
    <div
      v-for="(item, index) in items"
      :key="item.path + index"
      class="file-item"
      data-nav-item
      :class="{ 'is-focused': activeIndex === index }"
      :title="getTooltip(item)"
      @click="selectItem(index, $event)"
      @auxclick.middle="selectItem(index, $event)"
    >
      <span class="icon" :style="{ color: getFileIcon(item).color }">
        <component :is="getFileIcon(item).component" />
      </span>
      <span class="item-text">
        <span class="item-title-row">
          <span class="item-title">{{
            item.addinName || item.fileName
          }}</span>
          <span
            v-if="item.modifiedDate"
            class="item-date-pill"
          >{{ formatDate(item.modifiedDate) }}</span>
        </span>
        <span class="item-path" dir="ltr">{{ item.path }}</span>
      </span>
      <button
        class="open-in-app-button"
        type="button"
        :title="$t('localFileSearch.openInDefaultApp')"
        @click="openInDefaultApp(item, $event)"
        @auxclick.stop
        @mousedown.stop
      >
        <IconWindowArrowUp20Regular />
      </button>
    </div>
  </div>
</template>

<style scoped>
.scroller {
  height: 100%;
  overflow-y: auto;
}
.file-item {
  content-visibility: auto;
  contain-intrinsic-size: auto 44px;
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 0 12px;
  min-height: 44px;
  cursor: pointer;
  box-sizing: border-box;
  transition: background 0.1s;
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
  align-items: baseline;
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
.item-path {
  display: block;
  font-size: 11px;
  color: var(--text-secondary);
  line-height: 1.3;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  direction: ltr;
  text-align: start;
}
/* Date pill pushed to the far inline-end of the title row (physical left in RTL) so its
   left edge lines up vertically with the path row's left edge below it — the two-line item
   then reads as a tidy left-aligned column (pill over path), with the title on the right and
   the open-in-app button just outside to the far physical-left. */
.item-date-pill {
  margin-inline-start: auto;
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
/* "Open in default program" — a direct child of .file-item at the far inline end (physical
   left in RTL), vertically CENTERED against the whole row by the row's own align-items:center —
   mirroring the file-type icon on the opposite (physical-right) edge. It sits OUTSIDE the
   title/path text block on purpose: inside the baseline-aligned title row the button rode high
   and read as disconnected. .item-text's flex:1 consumes the middle and lands the button at the
   edge, so no auto-margin (and no interaction with the date pill) is needed. */
.open-in-app-button {
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  padding: 0;
  border-radius: 4px;
  color: var(--text-secondary);
  transition: background 0.1s, color 0.1s;
}
.open-in-app-button svg {
  width: 18px;
  height: 18px;
  color: inherit;
}
.open-in-app-button:hover {
  background: color-mix(in srgb, var(--text-primary) 10%, transparent);
  color: var(--accent-color, #0078d4);
}
</style>
