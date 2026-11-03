<script setup lang="ts">
import { ref, computed, watch, nextTick } from 'vue'
import IconBookRtl20 from '@/components/IconBookRtl20.vue'
import WindowedScroller from '@/components/WindowedScroller.vue'
import type { WindowedScrollerApi } from '@/components/windowedScrollerTypes'
import BookCatalogSearchCards from './BookCatalogSearch.Cards.vue'
import BookCatalogSearchTiles from './BookCatalogSearch.Tiles.vue'
import type { SearchFsItem, TocFsItem } from './useBookCatalogSearch'
import type { BookRow } from '@/webview-host/queries.types'
import { useInputListNavigation } from '@/composables/useInputListNavigation'
import { wantsNewTab, withNewTabHint, OPEN_IN_NEW_TAB_HINT_HE } from '@/composables/useOpenInNewTab'
import type { BooksView } from '@/stores/settingsStore'
import { titleWithAuthor } from './titleWithAuthor'

const props = defineProps<{
  items: SearchFsItem[]
  view: BooksView
  searching: boolean
}>()
const emit = defineEmits<{
  selectBook: [BookRow, boolean?]
  selectToc: [TocFsItem, boolean?]
}>()

const windowedScroller = ref<WindowedScrollerApi | null>(null)
// New results are a new list: re-keying the scroller starts it back at the top.
const generation = ref(0)
const isGridView = computed(() => props.view !== 'list')

// Combobox model: focus stays in the page's search input; the page forwards its
// keydown events here (onSearchInputKeydown) and the highlight moves through
// whichever view is active.
const { activeIndex: listActiveIndex, onKeydown: onListKeydown } = useInputListNavigation({
  getCount: () => (isGridView.value ? 0 : props.items.length),
  onActivate: (i, openInNewTab) => onSelect(props.items[i]!, openInNewTab),
  // Windowed (the list view): most rows have no element, so the scroller reveals the highlighted one.
  scrollIntoView: (index) => windowedScroller.value?.revealIndex(index),
})

// The grid views (cards, tiles) are drop-in swaps for each other: each owns its
// own highlight and keyboard navigation.
const gridRef = ref<InstanceType<typeof BookCatalogSearchCards> | null>(null)
const gridComponent = computed(() =>
  props.view === 'tiles' ? BookCatalogSearchTiles : BookCatalogSearchCards,
)

// New results make the old highlight point at a different item — drop it.
watch(
  () => props.items,
  () => {
    listActiveIndex.value = -1
    generation.value++
  },
)

// Switching view carries the highlight across rather than dropping it. The list
// index lives here for the life of the results, so without this the highlight
// would vanish on the way out and a stale one would reappear on the way back.
// Pre-flush: on the way out the grid is still mounted; on the way in it mounts
// after, so its index is set on the next tick.
watch(
  () => props.view,
  (view, oldView) => {
    if (view !== 'list' && oldView !== 'list') {
      // Grid to grid: the old grid is still mounted, so read its index first.
      const index = gridRef.value?.focusedIndex ?? -1
      nextTick(() => {
        if (gridRef.value) gridRef.value.focusedIndex = index
      })
    } else if (view !== 'list') {
      const index = listActiveIndex.value
      nextTick(() => {
        if (gridRef.value) gridRef.value.focusedIndex = index
      })
    } else listActiveIndex.value = gridRef.value?.focusedIndex ?? -1
  },
)

const itemTitle = (item: SearchFsItem) =>
  item.kind === 'toc'
    ? `${item.book.title} / ${item.tocPath}`
    : titleWithAuthor(item.book.title, item.book.authors)

function itemTooltip(item: SearchFsItem): string {
  const title = item.kind === 'toc' ? `${item.book.title} / ${item.tocPath}` : item.book.title
  const parts = [title]
  if (item.book.authors) parts.push(item.book.authors)
  if (item.book.parentPath) parts.push(item.book.parentPath)
  return withNewTabHint(parts.join('\n'), OPEN_IN_NEW_TAB_HINT_HE)
}

function onSelect(item: SearchFsItem, openInNewTab = false) {
  item.kind === 'toc'
    ? emit('selectToc', item, openInNewTab)
    : emit('selectBook', item.book, openInNewTab)
}

defineExpose({
  onSearchInputKeydown: (event: KeyboardEvent) =>
    isGridView.value ? !!gridRef.value?.onSearchInputKeydown(event) : onListKeydown(event),
})

function selectListItem(i: number, event?: MouseEvent) {
  listActiveIndex.value = i
  onSelect(props.items[i]!, wantsNewTab(event))
}
</script>

<template>
  <p v-if="!items.length && !searching" class="empty">{{ $t('bookCatalog.noResults') }}</p>
  <div v-else-if="view === 'list'" class="scroller">
    <!-- Windowed: results are never capped (a one-letter title search is thousands), so only
         the rows near the viewport exist. -->
    <WindowedScroller ref="windowedScroller" :key="generation" :count="items.length" class="windowed">
      <template #row="{ index }">
        <div
          v-if="items[index]"
          class="fs-item"
          data-nav-item
          :class="{ 'is-focused': listActiveIndex === index }"
          :title="itemTooltip(items[index]!)"
          @mousedown.prevent
          @click="selectListItem(index, $event)"
          @auxclick.middle="selectListItem(index, $event)"
        >
          <span class="icon"><IconBookRtl20 /></span>
          <span class="item-text">
            <span class="item-title">{{ itemTitle(items[index]!) }}</span>
            <span v-if="items[index]!.book.parentPath" class="item-path">{{ items[index]!.book.parentPath }}</span>
          </span>
        </div>
      </template>
    </WindowedScroller>
  </div>
  <component
    :is="gridComponent"
    v-else
    ref="gridRef"
    :items="items"
    @select="onSelect"
  />
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
}
.scroller > :deep(.windowed) {
  height: 100%;
}
.fs-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 0 12px;
  min-height: 44px;
  cursor: pointer;
  box-sizing: border-box;
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
.item-path {
  font-size: 11px;
  color: var(--text-secondary);
  line-height: 1.3;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

</style>
