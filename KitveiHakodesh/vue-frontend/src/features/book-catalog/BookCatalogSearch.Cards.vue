<script setup lang="ts">
import { ref, computed, watch, markRaw } from 'vue'
import IconBookRtl20 from '@/components/IconBookRtl20.vue'
import HomeSuggestionCard from '@/features/home/HomeSuggestionCard.vue'
import type { HomeSuggestionCard as CardData } from '@/features/home/homeSuggestionCard'
import type { SearchFsItem } from './useBookCatalogSearch'
import { useInputListNavigation } from '@/composables/useInputListNavigation'
import WindowedCardGrid from '@/components/WindowedCardGrid.vue'
import { titleWithAuthor } from './titleWithAuthor'
import { stripCommentaryTarget } from './stripCommentaryTarget'
import { fileIcon, fileTitle, fileFolder } from './fsItemFileIcon'

// Drop-in replacement for BookCatalogSearch.Tiles.vue: same props, emits and
// exposed keyboard hook, rendered with the browse view's card look
// (BookCatalogView.Cards.vue).
const props = defineProps<{ items: SearchFsItem[] }>()
const emit = defineEmits<{ select: [SearchFsItem, boolean] }>()

// Windowed: results are never capped, so only the lines of cards near the viewport exist.
const gridRef = ref<InstanceType<typeof WindowedCardGrid> | null>(null)
const BOOK_ICON = markRaw(IconBookRtl20)

const itemTitle = (item: SearchFsItem) =>
  item.kind === 'file'
    ? fileTitle(item.file)
    : item.kind === 'toc'
    ? `${item.book.title} / ${item.tocPath}`
    : titleWithAuthor(stripCommentaryTarget(item.book.title), item.book.authors)

// Same shape as the search list row: author in the title, folder path below.
const cards = computed((): CardData[] =>
  props.items.map((item, i) => ({
    key: item.uid,
    title: itemTitle(item),
    path: item.kind === 'file' ? fileFolder(item.file) : item.book.parentPath ?? '',
    icon: item.kind === 'file' ? markRaw(fileIcon(item.file).icon20) : BOOK_ICON,
    color: item.kind === 'file' ? fileIcon(item.file).color : '#c1440e',
    open: (openInNewTab) => {
      focusedIndex.value = i
      emit('select', item, openInNewTab)
    },
  })),
)

// Combobox model: DOM focus stays in the page's search input, which forwards its
// keydown here. Nothing here may take focus.
const { activeIndex: focusedIndex, onKeydown } = useInputListNavigation({
  getCount: () => props.items.length,
  onActivate: (i, openInNewTab) => emit('select', props.items[i]!, openInNewTab),
  // Most cards have no element: the grid's scroller lands on the highlighted one's line.
  scrollIntoView: (index) => gridRef.value?.revealIndex(index),
  getColumnsPerRow: () => gridRef.value?.columns ?? 1,
})

watch(
  () => props.items,
  () => {
    focusedIndex.value = -1
  },
)

defineExpose({
  onSearchInputKeydown: (event: KeyboardEvent) => onKeydown(event),
  /** Read/written by the parent to carry the highlight across a view switch. */
  focusedIndex,
})
</script>

<template>
  <WindowedCardGrid ref="gridRef" class="catalog-cards" :count="cards.length" view="cards" :list-key="props.items" stretch @mousedown.prevent>
    <template #item="{ index }">
      <HomeSuggestionCard :card="cards[index]!" :class="{ 'is-focused': focusedIndex === index }" />
    </template>
  </WindowedCardGrid>
</template>

<style scoped>
/* Title and path each wrap to at most 2 lines, then end with an ellipsis - as the browse cards (BookCatalogView.Cards.vue). */
.catalog-cards :deep(.card-title),
.catalog-cards :deep(.card-path) {
  display: -webkit-box;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 2;
  line-clamp: 2;
  white-space: normal;
  overflow: hidden;
  overflow-wrap: anywhere;
}
.is-focused :deep(.card) {
  background: color-mix(in srgb, var(--text-primary) 12%, var(--bg-primary));
}
</style>
