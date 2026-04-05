<script setup lang="ts">
import { ref, computed } from 'vue'
import { useVirtualizer } from '@tanstack/vue-virtual'
import { IconBook20Filled } from '@iconify-prerendered/vue-fluent'
import type { SearchFsItem, TocFsItem } from './useBooksFsSearch'
import type { BookRow } from './booksCategoryTree'
import { useVirtualListKeys } from '@/composables/useVirtualListKeyNav'
import { useTilesKeys } from '@/composables/useTileGridKeys'

const props = defineProps<{ items: SearchFsItem[]; view: 'list' | 'tiles' | 'tree' }>()
const emit = defineEmits<{ selectBook: [BookRow]; selectToc: [TocFsItem] }>()

const scrollEl = ref<HTMLElement | null>(null)
const tilesEl = ref<HTMLElement | null>(null)

const virtualizer = useVirtualizer(
  computed(() => ({
    count: props.view !== 'tiles' ? props.items.length : 0,
    getScrollElement: () => scrollEl.value,
    estimateSize: () => 44,
    overscan: 10,
    measureElement: (el: Element) => el.getBoundingClientRect().height,
  })),
)

const { focusedIndex: listFocused, containerFocused: listContainerFocused } = useVirtualListKeys(
  scrollEl,
  () =>
    virtualizer.value as unknown as import('@tanstack/vue-virtual').Virtualizer<Element, Element>,
  () => (props.view !== 'tiles' ? props.items.length : 0),
  (i) => onSelect(props.items[i]!),
)

const { focusedIndex: tilesFocused, containerFocused: tilesContainerFocused } = useTilesKeys(
  tilesEl,
  () => (props.view === 'tiles' ? props.items.length : 0),
  (i) => onSelect(props.items[i]!),
)

const itemTitle = (item: SearchFsItem) =>
  item.kind === 'toc' ? `${item.book.title} ${item.tocPath}` : item.book.title

function onSelect(item: SearchFsItem) {
  item.kind === 'toc' ? emit('selectToc', item) : emit('selectBook', item.book)
}

defineExpose({
  focusContainer: () => {
    const el = props.view === 'tiles' ? tilesEl.value : scrollEl.value
    el?.focus()
  },
})

function selectListItem(i: number) {
  listFocused.value = i
  onSelect(props.items[i]!)
}

function selectTileItem(i: number) {
  tilesFocused.value = i
  onSelect(props.items[i]!)
}
</script>

<template>
  <p v-if="!items.length" class="empty">אין תוצאות</p>
  <div v-else-if="view !== 'tiles'" ref="scrollEl" class="scroller" tabindex="0">
    <div :style="{ height: `${virtualizer.getTotalSize()}px`, position: 'relative' }">
      <div
        v-for="vRow in virtualizer.getVirtualItems()"
        :key="String(vRow.key)"
        :ref="(el) => el && virtualizer.measureElement(el as Element)"
        :data-index="vRow.index"
        :style="{
          position: 'absolute',
          top: 0,
          left: 0,
          right: 0,
          transform: `translateY(${vRow.start}px)`,
        }"
      >
        <div
          class="fs-item"
          data-nav-item
          :class="{
            'no-icon': view === 'tree',
            'is-focused': listContainerFocused && listFocused === vRow.index,
          }"
          @click="selectListItem(vRow.index)"
        >
          <span v-if="view !== 'tree'" class="icon"><IconBook20Filled /></span>
          <span class="item-text">
            <span class="item-title-row">
              <span class="item-title">{{ itemTitle(items[vRow.index]!) }}</span>
              <span v-if="items[vRow.index]!.book.authors" class="item-author-tag">{{
                items[vRow.index]!.book.authors
              }}</span>
            </span>
            <span v-if="items[vRow.index]!.book.fullPath" class="item-path">{{
              items[vRow.index]!.book.fullPath!.split(' / ').slice(0, -1).join(' / ')
            }}</span>
          </span>
        </div>
      </div>
    </div>
  </div>
  <div v-else ref="tilesEl" class="tiles-grid" tabindex="0">
    <div
      v-for="(item, i) in items"
      :key="item.uid"
      class="tile"
      data-nav-item
      :class="{ 'is-focused': tilesContainerFocused && tilesFocused === i }"
      :title="itemTitle(item)"
      @click="selectTileItem(i)"
    >
      <div class="tile-icon"><IconBook20Filled /></div>
      <span class="tile-label">{{ itemTitle(item) }}</span>
    </div>
  </div>
</template>

<style scoped>
.empty {
  color: var(--text-secondary);
  font-size: 14px;
  text-align: center;
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  margin: 0;
}
.scroller {
  height: 100%;
  overflow-y: auto;
}
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
.fs-item:hover {
  background: var(--hover-bg);
}
.fs-item:active {
  background: var(--active-bg);
}
.icon {
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  font-size: 20px;
}
.icon svg {
  color: #c1440e;
}
.fs-item.no-icon {
  padding-inline-start: 14px;
}
.item-text {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}
.item-title {
  font-size: 14px;
  color: var(--text-primary);
  line-height: 1.3;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  min-width: 0;
}
.item-title-row {
  display: flex;
  align-items: baseline;
  gap: 6px;
  overflow: hidden;
}
.item-author-tag {
  font-size: 10px;
  color: var(--text-secondary);
  background: color-mix(in srgb, var(--text-secondary) 12%, transparent);
  border-radius: 4px;
  padding: 1px 5px;
  white-space: nowrap;
  flex-shrink: 0;
}
.item-path {
  font-size: 11px;
  color: var(--text-secondary);
  line-height: 1.3;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
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
}
.tile:hover .tile-icon {
  transform: scale(1.08);
}
.tile:active .tile-icon {
  transform: scale(0.95);
}
.tile-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 40px;
  height: 40px;
  border-radius: 10px;
  background: var(--bg-secondary);
  transition: transform 0.15s;
  font-size: 22px;
}
.tile-icon svg {
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
}
</style>
