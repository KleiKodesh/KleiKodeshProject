<script setup lang="ts">
import { ref, computed } from 'vue'
import { useVirtualizer } from '@tanstack/vue-virtual'
import {
  IconDocument20Filled,
  IconDocumentPdf20Filled,
  IconDocumentText20Filled,
  IconDocumentGlobe20Filled,
  IconWindowArrowUp20Regular,
} from '@iconify-prerendered/vue-fluent'
import { useVirtualListKeys } from '@/composables/useVirtualListKeyNav'
import { wantsNewTab, withNewTabHint } from '@/composables/useOpenInNewTab'
import { openFileInDefaultApp } from '@/webview-host/bridge'
import type { LocalFileSearchResult } from './useLocalFileSearch'

const props = defineProps<{
  items: LocalFileSearchResult[]
  searching: boolean
}>()

const emit = defineEmits<{ openFile: [LocalFileSearchResult, boolean?] }>()

const scrollElement = ref<HTMLElement | null>(null)

const virtualizer = useVirtualizer(
  computed(() => ({
    count: props.items.length,
    getScrollElement: () => scrollElement.value,
    estimateSize: () => 44,
    overscan: 10,
    measureElement: (element: Element) => element.getBoundingClientRect().height,
  })),
)

const { focusedIndex, containerFocused } = useVirtualListKeys(
  scrollElement,
  () => virtualizer.value as unknown as import('@tanstack/vue-virtual').Virtualizer<Element, Element>,
  () => props.items.length,
  (index, openInNewTab) => onSelect(props.items[index]!, openInNewTab),
)

function onSelect(item: LocalFileSearchResult, openInNewTab = false) {
  emit('openFile', item, openInNewTab)
}

function selectItem(index: number, event?: MouseEvent) {
  focusedIndex.value = index
  onSelect(props.items[index]!, wantsNewTab(event))
}

// Hand the file off to the OS's default program (Word for .docx, Acrobat for .pdf, …).
// Guarded so the row's open-in-viewer click never also fires.
function openInDefaultApp(item: LocalFileSearchResult, event: MouseEvent) {
  event.stopPropagation()
  void openFileInDefaultApp(item.fullPath)
}

type FileIconInfo = { component: unknown; color: string }

function getFileIcon(fileName: string): FileIconInfo {
  const extension = fileName.toLowerCase().split('.').pop()
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
  focusContainer: () => scrollElement.value?.focus(),
})
</script>

<template>
  <div ref="scrollElement" class="scroller" tabindex="0">
    <div :style="{ height: `${virtualizer.getTotalSize()}px`, position: 'relative' }">
      <div
        v-for="virtualRow in virtualizer.getVirtualItems()"
        :key="String(virtualRow.key)"
        :ref="(element) => element && virtualizer.measureElement(element as Element)"
        :data-index="virtualRow.index"
        :style="{
          position: 'absolute',
          top: 0,
          left: 0,
          right: 0,
          transform: `translateY(${virtualRow.start}px)`,
        }"
      >
        <div
          class="file-item"
          data-nav-item
          :class="{ 'is-focused': containerFocused && focusedIndex === virtualRow.index }"
          :title="getTooltip(items[virtualRow.index]!)"
          @click="selectItem(virtualRow.index, $event)"
          @auxclick.middle="selectItem(virtualRow.index, $event)"
        >
          <span class="icon" :style="{ color: getFileIcon(items[virtualRow.index]!.fileName).color }">
            <component :is="getFileIcon(items[virtualRow.index]!.fileName).component" />
          </span>
          <span class="item-text">
            <span class="item-title-row">
              <span class="item-title">{{ items[virtualRow.index]!.fileName }}</span>
              <span
                v-if="items[virtualRow.index]!.modifiedDate"
                class="item-date-pill"
              >{{ formatDate(items[virtualRow.index]!.modifiedDate) }}</span>
            </span>
            <span class="item-path" dir="ltr">{{ items[virtualRow.index]!.path }}</span>
          </span>
          <button
            class="open-in-app-button"
            type="button"
            title="פתח בתוכנת ברירת המחדל"
            @click="openInDefaultApp(items[virtualRow.index]!, $event)"
            @auxclick.stop
            @mousedown.stop
          >
            <IconWindowArrowUp20Regular />
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.scroller {
  height: 100%;
  overflow-y: auto;
}
.file-item {
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
  text-align: right;
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
