<script setup lang="ts">
import { IconBook20Filled } from '@iconify-prerendered/vue-fluent'
import type { BookFsItem } from './useBooksFs'
import type { BookRow } from './booksFsTree'

defineProps<{ items: BookFsItem[]; view: 'list' | 'tiles' | 'tree' }>()
defineEmits<{ selectBook: [book: BookRow] }>()
</script>

<template>
  <p v-if="!items.length" class="empty">אין תוצאות</p>

  <!-- Tree mode: plain rows, no icons -->
  <div v-else-if="view === 'tree'" class="results-list">
    <div v-for="item in items" :key="item.uid"
      class="fs-item no-icon"
      :title="item.book.title"
      @click="$emit('selectBook', item.book)"
    >
      <span class="item-text">
        <span class="item-title">{{ item.book.title }}</span>
        <span v-if="item.book.fullPath" class="item-path">{{ item.book.fullPath.split(' / ').slice(0, -1).join(' / ') }}</span>
      </span>
    </div>
  </div>

  <!-- List view -->
  <div v-else-if="view === 'list'" class="results-list">
    <div v-for="item in items" :key="item.uid"
      class="fs-item"
      :title="item.book.title"
      @click="$emit('selectBook', item.book)"
    >
      <span class="icon"><IconBook20Filled /></span>
      <span class="item-text">
        <span class="item-title">{{ item.book.title }}</span>
        <span v-if="item.book.fullPath" class="item-path">{{ item.book.fullPath.split(' / ').slice(0, -1).join(' / ') }}</span>
      </span>
    </div>
  </div>

  <!-- Tiles view -->
  <div v-else class="tiles-grid">
    <div v-for="item in items" :key="item.uid"
      class="tile"
      :title="item.book.fullPath"
      @click="$emit('selectBook', item.book)"
    >
      <div class="tile-icon">
        <IconBook20Filled />
      </div>
      <span class="tile-label">{{ item.book.title }}</span>
    </div>
  </div>
</template>

<style scoped>
.empty { padding: 24px 16px; color: var(--text-secondary); font-size: 14px; text-align: center; }

/* List */
.results-list { height: 100%; overflow-y: auto; }

.fs-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 0 12px;
  min-height: 44px;
  cursor: pointer;
  box-sizing: border-box;
  transition: background 0.1s;
}
.fs-item:hover { background: var(--hover-bg); }
.fs-item:active { background: var(--active-bg); }

.icon { display: flex; align-items: center; justify-content: center; flex-shrink: 0; }
.icon svg { width: 20px; height: 20px; color: #C1440E; }
.fs-item.no-icon { padding-inline-start: 14px; }

.item-text { display: flex; flex-direction: column; gap: 2px; min-width: 0; }
.item-title { font-size: 14px; color: var(--text-primary); line-height: 1.3; }
.item-path { font-size: 11px; color: var(--text-secondary); line-height: 1.3; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }

/* Tiles */
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

.tile-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 48px;
  height: 48px;
  border-radius: 12px;
  background: var(--bg-secondary);
  transition: transform 0.15s;
}
.tile-icon svg { color: #C1440E; }

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
