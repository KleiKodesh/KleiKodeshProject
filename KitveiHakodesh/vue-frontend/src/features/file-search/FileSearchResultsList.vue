<script setup lang="ts">
import { ref, computed } from 'vue'
import { useVirtualizer } from '@tanstack/vue-virtual'
import {
  IconDocument20Filled,
  IconDocumentPdf20Filled,
  IconDocumentText20Filled,
  IconDocumentGlobe20Filled,
  IconCode20Filled,
} from '@iconify-prerendered/vue-fluent'
import { useVirtualListKeys } from '@/composables/useVirtualListKeyNav'
import type { FileSearchResult } from './useFileSearch'

const props = defineProps<{
  items: FileSearchResult[]
  searching: boolean
  isIndexing: boolean
}>()

const emit = defineEmits<{ openFile: [FileSearchResult] }>()

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
  (index) => onSelect(props.items[index]!),
)

function onSelect(item: FileSearchResult) {
  emit('openFile', item)
}

function selectItem(index: number) {
  focusedIndex.value = index
  onSelect(props.items[index]!)
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
      return { component: IconDocumentGlobe20Filled, color: '#e44d26' }
    case 'xml':
      return { component: IconCode20Filled, color: '#f0a500' }
    case 'txt':
      return { component: IconDocumentText20Filled, color: '#9e9e9e' }
    default:
      return { component: IconDocument20Filled, color: '#3478f6' }
  }
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
          :title="items[virtualRow.index]!.fullPath"
          @click="selectItem(virtualRow.index)"
        >
          <span class="icon" :style="{ color: getFileIcon(items[virtualRow.index]!.fileName).color }">
            <component :is="getFileIcon(items[virtualRow.index]!.fileName).component" />
          </span>
          <span class="item-text">
            <span class="item-title-row">
              <span class="item-title">{{ items[virtualRow.index]!.fileName }}</span>
            </span>
            <span class="item-path" dir="ltr">{{ items[virtualRow.index]!.path }}</span>
          </span>
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
  font-size: 11px;
  color: var(--text-secondary);
  line-height: 1.3;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  direction: ltr;
  text-align: right;
}
</style>
