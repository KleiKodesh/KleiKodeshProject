<script setup lang="ts">
import { ref, computed, watch, markRaw } from 'vue'
import { IconFolder20Filled } from '@iconify-prerendered/vue-fluent'
import IconBookRtl20 from '@/components/IconBookRtl20.vue'
import HomeSuggestionCard from '@/features/home/HomeSuggestionCard.vue'
import type { HomeSuggestionCard as CardData } from '@/features/home/homeSuggestionCard'
import type { FsItem } from './useBookCatalog'
import type { CategoryNode } from '@/features/book-catalog/bookCatalogTree'
import type { BookRow } from '@/webview-host/queries.types'
import { useInputListNavigation, countGridColumns } from '@/composables/useInputListNavigation'

// Drop-in replacement for BookCatalogView.Tiles.vue: same props, emits and
// exposed keyboard hook, rendered with the home page's card look.
const props = defineProps<{ items: FsItem[] }>()
const emit = defineEmits<{ selectBook: [BookRow, boolean?]; enterFolder: [CategoryNode] }>()

const cardsEl = ref<HTMLElement | null>(null)
const FOLDER_ICON = markRaw(IconFolder20Filled)
const BOOK_ICON = markRaw(IconBookRtl20)

function activateIndex(index: number, openInNewTab = false) {
  const item = props.items[index]
  if (!item) return
  item.kind === 'folder'
    ? emit('enterFolder', item.node)
    : emit('selectBook', item.book, openInNewTab)
}

/** Secondary line: book count for a folder, authors for a book. */
function getSecondaryLine(item: FsItem): string {
  if (item.kind === 'book') return item.book.authors ?? ''
  const count = item.node.subtreeBookIds?.length ?? 0
  return count ? `${count} ספרים` : ''
}

const cards = computed((): CardData[] =>
  props.items.map((item, i) => ({
    key: item.uid,
    title: item.kind === 'folder' ? item.node.title : item.book.title,
    path: getSecondaryLine(item),
    icon: item.kind === 'folder' ? FOLDER_ICON : BOOK_ICON,
    color: item.kind === 'folder' ? 'var(--status-warning)' : '#c1440e',
    open: (openInNewTab) => {
      focusedIndex.value = i
      activateIndex(i, openInNewTab)
    },
  })),
)

// Combobox model (see useInputListNavigation): DOM focus stays in the page's
// search input, which forwards its keydown here. Nothing here may take focus.
const { activeIndex: focusedIndex, onKeydown } = useInputListNavigation({
  getCount: () => props.items.length,
  onActivate: activateIndex,
  containerElement: cardsEl,
  getColumnsPerRow: () => countGridColumns(cardsEl.value),
})

// A new item list invalidates the old highlight (moveTo clamps past the end).
watch(
  () => props.items,
  () => {
    focusedIndex.value = -1
  },
)

defineExpose({
  onSearchInputKeydown: (event: KeyboardEvent) => onKeydown(event),
})
</script>

<template>
  <p v-if="!items.length" class="empty">אין פריטים</p>
  <!-- Host is the size container so the grid's cqi tracks the pane width. -->
  <div v-else class="catalog-cards-host">
    <div ref="cardsEl" class="catalog-cards" @mousedown.prevent>
      <HomeSuggestionCard
        v-for="(card, i) in cards"
        :key="card.key"
        :card="card"
        :class="{ 'is-focused': focusedIndex === i }"
      />
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
.catalog-cards-host {
  container-type: inline-size;
  height: 100%;
  min-height: 0;
}
/* Same grid as the home page's .home-cards. */
.catalog-cards {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(min(100%, clamp(140px, 30cqi, 220px)), 1fr));
  gap: 8px;
  padding: 12px;
  overflow-x: hidden;
  overflow-y: auto;
  height: 100%;
  box-sizing: border-box;
  align-content: flex-start;
}
/* Keyboard highlight: DOM focus never reaches a card, so it comes from a class
   on the card's root wrapper and is painted on the inner button. */
.is-focused :deep(.card) {
  background: var(--input-bg-focus);
  border-color: var(--accent-color);
}
</style>
