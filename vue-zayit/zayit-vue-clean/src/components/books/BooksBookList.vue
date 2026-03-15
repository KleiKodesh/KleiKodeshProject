<script setup lang="ts">
import { RecycleScroller } from 'vue-virtual-scroller'
import 'vue-virtual-scroller/dist/vue-virtual-scroller.css'
import { IconBook20Regular, IconFolder20Regular } from '@iconify-prerendered/vue-fluent'
import type { FsItem } from './useBooksFs'
import type { CategoryNode, BookRow } from './booksFsTree'

defineProps<{ items: FsItem[] }>()
defineEmits<{ selectBook: [book: BookRow]; enterFolder: [node: CategoryNode] }>()
</script>

<template>
  <p v-if="!items.length" class="empty">אין תוצאות</p>
  <RecycleScroller v-else class="scroller" :items="items" :item-size="57" key-field="uid">
    <template #default="{ item }">
      <div v-if="item.kind === 'folder'" class="fs-item" @click="$emit('enterFolder', item.node)">
        <span class="icon folder-icon"><IconFolder20Regular /></span>
        <span class="title">{{ item.node.title }}</span>
      </div>
      <div v-else class="fs-item" @click="$emit('selectBook', item.book)">
        <span class="icon book-icon"><IconBook20Regular /></span>
        <span class="title">{{ item.book.title }}</span>
      </div>
    </template>
  </RecycleScroller>
</template>

<style scoped>
.empty { padding: 24px 16px; color: var(--text-secondary); font-size: 14px; text-align: center; }
.scroller { height: 100%; }

.fs-item {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 10px 16px;
  height: 57px;
  border-bottom: 1px solid var(--border-color);
  cursor: pointer;
  box-sizing: border-box;
  transition: background 0.1s;
}
.fs-item:hover { background: var(--hover-bg); }
.fs-item:active { background: var(--active-bg); }

.icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 36px;
  height: 36px;
  border-radius: 8px;
  flex-shrink: 0;
}
.folder-icon { background: color-mix(in srgb, var(--accent-color) 12%, transparent); color: var(--accent-color); }
.book-icon { background: color-mix(in srgb, #e8622a 12%, transparent); color: #e8622a; }

.title { font-size: 14px; color: var(--text-primary); }
</style>
