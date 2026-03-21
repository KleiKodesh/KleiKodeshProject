<script setup lang="ts">
import { ref, computed } from 'vue'
import { useVirtualizer } from '@tanstack/vue-virtual'
import { IconBook20Filled } from '@iconify-prerendered/vue-fluent'
import type { SearchFsItem, TocFsItem } from './useBooksFsSearch'
import type { BookRow } from './booksFsTree'

const props = defineProps<{ items: SearchFsItem[]; view: 'list' | 'tiles' | 'tree' }>()
const emit = defineEmits<{ selectBook: [BookRow]; selectToc: [TocFsItem] }>()

const scrollEl = ref<HTMLElement | null>(null)
const virtualizer = useVirtualizer(computed(() => ({
  count: props.view !== 'tiles' ? props.items.length : 0,
  getScrollElement: () => scrollEl.value,
  estimateSize: () => props.view === 'tree' ? 44 : 56,
  overscan: 10,
})))

const itemTitle = (item: SearchFsItem) =>
  item.kind === 'toc' ? `${item.book.title} / ${item.tocPath}` : item.book.title

function onSelect(item: SearchFsItem) {
  item.kind === 'toc' ? emit('selectToc', item) : emit('selectBook', item.book)
}
</script>

<template>
  <p v-if="!items.length" class="empty">אין תוצאות</p>
  <div v-else-if="view !== 'tiles'" ref="scrollEl" class="scroller">
    <div :style="{ height: `${virtualizer.getTotalSize()}px`, position: 'relative' }">
      <div v-for="vRow in virtualizer.getVirtualItems()" :key="String(vRow.key)"
        :style="{ position: 'absolute', top: 0, left: 0, right: 0, transform: `translateY(${vRow.start}px)` }">
        <div class="fs-item" :class="{ 'no-icon': view === 'tree' }" @click="onSelect(items[vRow.index]!)">
          <span v-if="view !== 'tree'" class="icon"><IconBook20Filled /></span>
          <span class="item-text">
            <span class="item-title">{{ itemTitle(items[vRow.index]!) }}</span>
            <span v-if="items[vRow.index]!.book.fullPath" class="item-path">{{ items[vRow.index]!.book.fullPath!.split(' / ').slice(0, -1).join(' / ') }}</span>
          </span>
        </div>
      </div>
    </div>
  </div>
  <div v-else class="tiles-grid">
    <div v-for="item in items" :key="item.uid" class="tile" :title="itemTitle(item)" @click="onSelect(item)">
      <div class="tile-icon"><IconBook20Filled /></div>
      <span class="tile-label">{{ itemTitle(item) }}</span>
    </div>
  </div>
</template>

<style scoped>
.empty { color: var(--text-secondary); font-size: 14px; text-align: center; position: absolute; inset: 0; display: flex; align-items: center; justify-content: center; margin: 0; }
.scroller { height: 100%; overflow-y: auto; }
.fs-item { display: flex; align-items: center; gap: 10px; padding: 0 12px; min-height: 44px; cursor: pointer; box-sizing: border-box; transition: background 0.1s; }
.fs-item:hover { background: var(--hover-bg); }
.fs-item:active { background: var(--active-bg); }
.icon { display: flex; align-items: center; justify-content: center; flex-shrink: 0; font-size: 20px; }
.icon svg { color: #C1440E; }
.fs-item.no-icon { padding-inline-start: 14px; }
.item-text { display: flex; flex-direction: column; gap: 2px; min-width: 0; }
.item-title { font-size: 14px; color: var(--text-primary); line-height: 1.3; }
.item-path { font-size: 11px; color: var(--text-secondary); line-height: 1.3; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.tiles-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(72px, 1fr)); gap: 12px; padding: 12px; overflow-x: hidden; overflow-y: auto; height: 100%; box-sizing: border-box; align-content: flex-start; }
.tile { display: flex; flex-direction: column; align-items: center; gap: 5px; width: 72px; cursor: pointer; -webkit-tap-highlight-color: transparent; }
.tile:hover .tile-icon { transform: scale(1.08); }
.tile:active .tile-icon { transform: scale(0.95); }
.tile-icon { display: flex; align-items: center; justify-content: center; width: 40px; height: 40px; border-radius: 10px; background: var(--bg-secondary); transition: transform 0.15s; font-size: 22px; }
.tile-icon svg { color: #C1440E; }
.tile-label { font-size: 11px; color: var(--text-primary); text-align: center; line-height: 1.3; width: 100%; overflow: hidden; white-space: normal; word-break: break-word; }
</style>
