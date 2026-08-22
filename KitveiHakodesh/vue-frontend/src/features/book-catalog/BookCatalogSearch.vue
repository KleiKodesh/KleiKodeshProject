<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { useVirtualizer } from '@tanstack/vue-virtual'
import IconBookRtl20 from '@/components/IconBookRtl20.vue'
import type { SearchFsItem, TocFsItem } from './useBookCatalogSearch'
import type { BookRow } from '@/webview-host/queries.types'
import { useInputListNavigation } from '@/composables/useInputListNavigation'
import { wantsNewTab, withNewTabHint } from '@/composables/useOpenInNewTab'

const props = defineProps<{
  items: SearchFsItem[]
  searching: boolean
}>()
const emit = defineEmits<{
  selectBook: [BookRow, boolean?]
  selectToc: [TocFsItem, boolean?]
}>()

const scrollEl = ref<HTMLElement | null>(null)

const virtualizer = useVirtualizer(
  computed(() => ({
    count: props.items.length,
    getScrollElement: () => scrollEl.value,
    estimateSize: () => 44,
    overscan: 10,
    measureElement: (el: Element) => el.getBoundingClientRect().height,
  })),
)

// Combobox model: focus stays in the page's search input; the page forwards its
// keydown events here (onSearchInputKeydown) and the highlight moves through the
// results.
const { activeIndex: listActiveIndex, onKeydown: onListKeydown } = useInputListNavigation({
  getCount: () => props.items.length,
  onActivate: (i, openInNewTab) => onSelect(props.items[i]!, openInNewTab),
  getVirtualizer: () =>
    virtualizer.value as unknown as import('@tanstack/vue-virtual').Virtualizer<Element, Element>,
})

// New results make the old highlight point at a different item — drop it.
watch(
  () => props.items,
  () => {
    listActiveIndex.value = -1
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
  onSearchInputKeydown: (event: KeyboardEvent) => onListKeydown(event),
})

function selectListItem(i: number, event?: MouseEvent) {
  listActiveIndex.value = i
  onSelect(props.items[i]!, wantsNewTab(event))
}
</script>

<template>
  <p v-if="!items.length && !searching" class="empty">לא נמצאו תוצאות</p>
  <div v-else ref="scrollEl" class="scroller">
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
            'is-focused': listActiveIndex === vRow.index,
          }"
          :title="itemTooltip(items[vRow.index]!)"
          @click="selectListItem(vRow.index, $event)"
          @auxclick.middle="selectListItem(vRow.index, $event)"
        >
          <span class="icon"><IconBookRtl20 /></span>
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
</style>
