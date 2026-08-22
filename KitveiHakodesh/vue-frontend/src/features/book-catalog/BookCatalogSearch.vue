<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { useVirtualizer } from '@tanstack/vue-virtual'
import IconBookRtl20 from '@/components/IconBookRtl20.vue'
import type { SearchFsItem, TocFsItem } from './useBookCatalogSearch'
import type { BookRow } from '@/webview-host/queries.types'
import { useInputListNavigation, countGridColumns } from '@/composables/useInputListNavigation'
import { wantsNewTab, withNewTabHint } from '@/composables/useOpenInNewTab'

const props = defineProps<{
  items: SearchFsItem[]
  view: 'list' | 'tiles' | 'tree'
  searching: boolean
}>()
const emit = defineEmits<{
  selectBook: [BookRow, boolean?]
  selectToc: [TocFsItem, boolean?]
}>()

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

// Combobox model: focus stays in the page's search input; the page forwards its
// keydown events here (onSearchInputKeydown) and the highlight moves through
// whichever view is active.
const { activeIndex: listActiveIndex, onKeydown: onListKeydown } = useInputListNavigation({
  getCount: () => (props.view !== 'tiles' ? props.items.length : 0),
  onActivate: (i, openInNewTab) => onSelect(props.items[i]!, openInNewTab),
  getVirtualizer: () =>
    virtualizer.value as unknown as import('@tanstack/vue-virtual').Virtualizer<Element, Element>,
})


const { activeIndex: tilesActiveIndex, onKeydown: onTilesKeydown } = useInputListNavigation({
  getCount: () => (props.view === 'tiles' ? props.items.length : 0),
  onActivate: (i, openInNewTab) => onSelect(props.items[i]!, openInNewTab),
  containerElement: tilesEl,
  getColumnsPerRow: () => countGridColumns(tilesEl.value),
})

// New results make the old highlight point at a different item — drop it.
watch(
  () => props.items,
  () => {
    listActiveIndex.value = -1
    tilesActiveIndex.value = -1
  },
)

const itemTitle = (item: SearchFsItem) =>
  item.kind === 'toc' ? `${item.book.title} ${item.tocPath}` : item.book.title

function itemTooltip(item: SearchFsItem): string {
  const title = item.kind === 'toc' ? `${item.book.title} — ${item.tocPath}` : item.book.title
  const parts = [title]
  if (item.book.authors) parts.push(item.book.authors)
  if (item.book.parentPath) parts.push(item.book.parentPath)
  return withNewTabHint(parts.join('\n'))
}

function onSelect(item: SearchFsItem, openInNewTab = false) {
  item.kind === 'toc'
    ? emit('selectToc', item, openInNewTab)
    : emit('selectBook', item.book, openInNewTab)
}

defineExpose({
  onSearchInputKeydown: (event: KeyboardEvent) =>
    props.view === 'tiles' ? onTilesKeydown(event) : onListKeydown(event),
})

function selectListItem(i: number, event?: MouseEvent) {
  listActiveIndex.value = i
  onSelect(props.items[i]!, wantsNewTab(event))
}

function selectTileItem(i: number, event?: MouseEvent) {
  tilesActiveIndex.value = i
  onSelect(props.items[i]!, wantsNewTab(event))
}
</script>

<template>
  <p v-if="!items.length && !searching" class="empty">לא נמצאו תוצאות</p>
  <div v-else-if="view !== 'tiles'" ref="scrollEl" class="scroller">
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
            'is-focused': listActiveIndex === vRow.index,
          }"
          :title="itemTooltip(items[vRow.index]!)"
          @click="selectListItem(vRow.index, $event)"
          @auxclick.middle="selectListItem(vRow.index, $event)"
        >
          <span v-if="view !== 'tree'" class="icon"><IconBookRtl20 /></span>
          <span class="item-text">
            <span class="item-title-row">
              <span class="item-title">{{ itemTitle(items[vRow.index]!) }}</span>
              <span v-if="items[vRow.index]!.book.authors" class="item-author-tag">{{
                items[vRow.index]!.book.authors
              }}</span>
            </span>
            <span v-if="items[vRow.index]!.book.parentPath" class="item-path">{{
              items[vRow.index]!.book.parentPath
            }}</span>
          </span>
        </div>
      </div>
    </div>
  </div>
  <div v-else ref="tilesEl" class="tiles-grid">
    <!-- A <button>, like the browse grid's and the home page's: it is a control,
         and unlike a div it does not stretch to fill its grid cell, which is what
         keeps it the same 72px as theirs. -->
    <button
      v-for="(item, i) in items"
      :key="item.uid"
      type="button"
      class="tile"
      data-nav-item
      tabindex="-1"
      :class="{ 'is-focused': tilesActiveIndex === i }"
      :title="itemTooltip(item)"
      @click="selectTileItem(i, $event)"
      @auxclick.middle="selectTileItem(i, $event)"
    >
      <div class="tile-icon"><IconBookRtl20 /></div>
      <span class="tile-label">{{ itemTitle(item) }}</span>
    </button>
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
  flex-shrink: 1;
}
.item-title-row {
  display: flex;
  align-items: baseline;
  gap: 6px;
  overflow: hidden;
  justify-content: space-between;
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
/* The same tile as the browse grid's (BookCatalogView.Tiles.vue), which is the
   home page's: one catalog, one tile. Only the icon differs — these are always
   books, never folders. */
.tile {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 5px;
  width: 72px;
  padding: 6px 4px;
  background: none;
  border: none;
  border-radius: 6px;
  cursor: pointer;
  -webkit-tap-highlight-color: transparent;
}
.tile:hover .tile-icon {
  transform: scale(1.15);
}
.tile:active .tile-icon {
  transform: scale(0.95);
}
/* Keyboard focus reads as the grown icon rather than the global
   `[data-nav-item].is-focused` fill, matching the browse grid: DOM focus stays in
   the search field, so the class is the only signal, and a filled square under a
   grown icon is the same thing said twice. */
.tile.is-focused {
  background: none;
}
.tile.is-focused .tile-icon {
  transform: scale(1.25);
}
.tile-icon {
  position: relative;
  display: flex;
  align-items: center;
  justify-content: center;
  width: 48px;
  height: 48px;
  border-radius: 6px;
  background: none;
  font-size: 28px;
  transition:
    transform 0.15s ease,
    opacity 0.12s ease;
}
.tile-icon svg {
  width: 1em;
  height: 1em;
  color: #c1440e;
}
.tile-label {
  font-size: 11px;
  color: var(--text-primary);
  text-align: center;
  line-height: 1.3;
  max-width: 68px;
  overflow: hidden;
  white-space: normal;
  word-break: break-word;
  display: -webkit-box;
  -webkit-line-clamp: 3;
  -webkit-box-orient: vertical;
}
</style>
