<script setup lang="ts">
import { ref } from 'vue'
import { IconFolder20Filled } from '@iconify-prerendered/vue-fluent'
import IconBookRtl20 from '@/components/IconBookRtl20.vue'
import type { FsItem } from './useBookCatalog'
import type { CategoryNode, BookRow } from '@/features/book-catalog/bookCatalogTree'
import { useTilesKeys } from '@/composables/useTileGridKeys'
import { wantsNewTab } from '@/composables/useOpenInNewTab'

const props = defineProps<{ items: FsItem[] }>()
const emit = defineEmits<{ selectBook: [BookRow, boolean?]; enterFolder: [CategoryNode] }>()

const tilesEl = ref<HTMLElement | null>(null)

function activateIndex(index: number, openInNewTab = false) {
  const item = props.items[index]
  if (!item) return
  item.kind === 'folder'
    ? emit('enterFolder', item.node)
    : emit('selectBook', item.book, openInNewTab)
}

function getTitle(item: FsItem) {
  return item.kind === 'folder' ? item.node.title : item.book.title
}

const { focusedIndex, containerFocused } = useTilesKeys(
  tilesEl,
  () => props.items.length,
  activateIndex,
)

defineExpose({
  focusContainer: () => tilesEl.value?.focus(),
})

function selectItem(i: number, event?: MouseEvent) {
  focusedIndex.value = i
  activateIndex(i, wantsNewTab(event))
}
</script>

<template>
  <p v-if="!items.length" class="empty">׳׳™׳ ׳₪׳¨׳™׳˜׳™׳</p>
  <div v-else ref="tilesEl" class="tiles-grid" tabindex="0">
    <div
      v-for="(item, i) in items"
      :key="item.uid"
      class="tile"
      data-nav-item
      :class="{ 'is-focused': containerFocused && focusedIndex === i }"
      :title="getTitle(item)"
      @click="selectItem(i, $event)"
      @auxclick.middle="selectItem(i, $event)"
    >
      <div class="tile-icon" :class="item.kind === 'folder' ? 'folder-icon' : 'book-icon'">
        <IconFolder20Filled v-if="item.kind === 'folder'" /><IconBookRtl20 v-else />
      </div>
      <span class="tile-label">{{ getTitle(item) }}</span>
    </div>
  </div>
</template>

<style scoped>
.empty {
  padding: 24px 16px;
  color: var(--text-secondary);
  font-size: 14px;
  text-align: center;
}
.tiles-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(72px, 1fr));
  gap: 6px;
  padding: 12px;
  overflow-x: hidden;
  overflow-y: auto;
  height: 100%;
  box-sizing: border-box;
  align-content: flex-start;
}
.tile {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 5px;
  width: 72px;
  cursor: pointer;
  -webkit-tap-highlight-color: transparent;
  padding-block: 4px;
  padding-inline: 3px;
  border-radius: 6px;
  position: relative;
}
.tile:hover .tile-icon {
  transform: scale(1.08);
}
.tile:active .tile-icon {
  transform: scale(0.95);
}
.tile .tile-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 40px;
  height: 40px;
  border-radius: 10px;
  background: var(--bg-secondary);
  transition: transform 0.15s;
}
.tile .tile-icon svg {
  width: 22px;
  height: 22px;
}
.tile .folder-icon svg {
  color: var(--status-warning);
}
.tile .book-icon svg {
  color: #c1440e;
}
.tile-label {
  font-size: 11px;
  color: var(--text-primary);
  text-align: center;
  line-height: 1.3;
  width: 100%;
  overflow: hidden;
  white-space: normal;
  word-break: break-word;
  display: -webkit-box;
  -webkit-line-clamp: 3;
  -webkit-box-orient: vertical;
}
</style>
