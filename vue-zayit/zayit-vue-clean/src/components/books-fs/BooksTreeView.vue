<script setup lang="ts">
import { RecycleScroller } from 'vue-virtual-scroller'
import 'vue-virtual-scroller/dist/vue-virtual-scroller.css'
import { IconBook20Filled, IconFolderOpen20Filled } from '@iconify-prerendered/vue-fluent'
import type { FsItem } from './useBooksFs'
import type { CategoryNode, BookRow } from './booksFsTree'

defineProps<{ items: FsItem[]; view: 'list' | 'tiles' }>()
defineEmits<{ selectBook: [book: BookRow]; enterFolder: [node: CategoryNode] }>()

const asFsItem = (item: unknown) => item as FsItem
const asFolder = (item: unknown) => item as Extract<FsItem, { kind: 'folder' }>
const asBook = (item: unknown) => item as Extract<FsItem, { kind: 'book' }>
</script>

<template>
  <p v-if="!items.length" class="empty">אין פריטים</p>

  <!-- List view -->
  <RecycleScroller v-else-if="view === 'list'" class="scroller" :items="items" :item-size="36" key-field="uid">
    <template #default="{ item }">
      <div v-if="asFsItem(item).kind === 'folder'" class="fs-item" @click="$emit('enterFolder', asFolder(item).node)">
        <span class="icon folder-icon"><IconFolderOpen20Filled /></span>
        <span class="title">{{ asFolder(item).node.title }}</span>
      </div>
      <div v-else class="fs-item" @click="$emit('selectBook', asBook(item).book)">
        <span class="icon book-icon"><IconBook20Filled /></span>
        <span class="title">{{ asBook(item).book.title }}</span>
      </div>
    </template>
  </RecycleScroller>

  <!-- Tiles view -->
  <div v-else class="tiles-grid">
    <div v-for="item in items" :key="item.uid"
      class="tile"
      :title="item.kind === 'book' ? item.book.title : undefined"
      @click="item.kind === 'folder' ? $emit('enterFolder', item.node) : $emit('selectBook', item.book)"
    >
      <div class="tile-icon" :class="item.kind === 'folder' ? 'folder-icon' : 'book-icon'">
        <IconFolderOpen20Filled v-if="item.kind === 'folder'" />
        <IconBook20Filled v-else />
      </div>
      <span class="tile-label">{{ item.kind === 'folder' ? item.node.title : item.book.title }}</span>
    </div>
  </div>
</template>

<style scoped>
.empty { padding: 24px 16px; color: var(--text-secondary); font-size: 14px; text-align: center; }
.scroller { height: 100%; }

.fs-item {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 0 10px;
  height: 36px;
  cursor: pointer;
  box-sizing: border-box;
  transition: background 0.1s;
}
.fs-item:hover { background: var(--hover-bg); }
.fs-item:active { background: var(--active-bg); }

.icon { display: flex; align-items: center; justify-content: center; flex-shrink: 0; }
.folder-icon svg { color: #f0a500; }
.book-icon svg { color: #C1440E; }
.title { font-size: 14px; color: var(--text-primary); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }

.tiles-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(72px, 1fr));
  gap: 12px;
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
}
.tile:hover .tile-icon { transform: scale(1.08); }
.tile:active .tile-icon { transform: scale(0.95); }

.tile .tile-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 48px;
  height: 48px;
  border-radius: 12px;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  transition: transform 0.15s;
}
.tile .folder-icon svg { color: #f0a500; }
.tile .book-icon svg { color: #C1440E; }

.tile-label {
  font-size: 11px;
  color: var(--text-primary);
  text-align: center;
  line-height: 1.3;
  width: 100%;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
</style>
