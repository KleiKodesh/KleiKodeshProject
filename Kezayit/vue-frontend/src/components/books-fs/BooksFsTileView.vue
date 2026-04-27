<script setup lang="ts">
import { ref } from 'vue'
import { IconBook20Filled, IconFolder20Filled } from '@iconify-prerendered/vue-fluent'
import type { FsItem } from './useBooksFs'
import type { CategoryNode, BookRow } from '@/utils/booksCategoryTree'
import { useTilesKeys } from '@/composables/useTileGridKeys'

const props = defineProps<{ items: FsItem[] }>()
const emit = defineEmits<{ selectBook: [BookRow]; enterFolder: [CategoryNode] }>()

const tilesEl = ref<HTMLElement | null>(null)

function activateIndex(index: number) {
  const item = props.items[index]
  if (!item) return
  item.kind === 'folder' ? emit('enterFolder', item.node) : emit('selectBook', item.book)
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

function selectItem(i: number) {
  focusedIndex.value = i
  activateIndex(i)
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
      @click="selectItem(i)"
    >
      <div class="tile-icon" :class="item.kind === 'folder' ? 'folder-icon' : 'book-icon'">
        <IconFolder20Filled v-if="item.kind === 'folder'" /><IconBook20Filled v-else />
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
  color: #f0a500;
}
.tile .book-icon svg {
  color: #c1440e;
  transform: scaleX(-1);
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
  -webkit-line-clamp: 4;
  -webkit-box-orient: vertical;
}
</style>
