<script setup lang="ts">
import { ref, computed, watch, markRaw } from 'vue'
import IconBookRtl20 from '@/components/IconBookRtl20.vue'
import HomeSuggestionCard from '@/features/home/HomeSuggestionCard.vue'
import type { HomeSuggestionCard as CardData } from '@/features/home/homeSuggestionCard'
import type { SearchFsItem } from './useBookCatalogSearch'
import { useInputListNavigation, countGridColumns } from '@/composables/useInputListNavigation'
import { titleWithAuthor } from './titleWithAuthor'
import { stripCommentaryTarget } from './stripCommentaryTarget'
import { fileIcon, fileTitle, fileFolder } from './fsItemFileIcon'

// Drop-in replacement for BookCatalogSearch.Tiles.vue: same props, emits and
// exposed keyboard hook, rendered with the browse view's card look
// (BookCatalogView.Cards.vue).
const props = defineProps<{ items: SearchFsItem[] }>()
const emit = defineEmits<{ select: [SearchFsItem, boolean] }>()

const cardsEl = ref<HTMLElement | null>(null)
const CARD_CELL = ':scope > *'
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
  containerElement: cardsEl,
  // Cells are the card wrappers — see BookCatalogView.Cards.vue for why not the buttons.
  itemSelector: CARD_CELL,
  getColumnsPerRow: () => countGridColumns(cardsEl.value, CARD_CELL),
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
  <!-- Host is the size container so the grid's cqi tracks the pane width. -->
  <div class="catalog-cards-host">
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
.catalog-cards-host {
  container-type: inline-size;
  height: 100%;
  min-height: 0;
}
/* Same grid as BookCatalogView.Cards.vue / the home page's .home-cards. */
.catalog-cards {
  display: grid;
  /* Column count and grid width come from the page (useCardGridLayout): cards
     shrink to fit another column, never stretch past their max, and the grid
     sits centred. Until the page has measured, it falls back to auto-fill. */
  grid-template-columns: repeat(var(--catalog-columns, auto-fill), minmax(min(100%, 140px), 1fr));
  /* Explorer-like: wide columns, tight rows, so each row reads as one line. */
  column-gap: 16px;
  row-gap: 4px;
  padding: 12px;
  padding-inline: calc((100% - 24px - var(--catalog-content-width, calc(100% - 24px))) / 2 + 12px);
  overflow-x: hidden;
  overflow-y: auto;
  scrollbar-width: thin;
  height: 100%;
  box-sizing: border-box;
  align-content: flex-start;
}
/* Results can run to thousands: skip rendering off-screen cards without a
   virtualizer. auto-length remembers each card's real height once rendered. */
.catalog-cards > * {
  content-visibility: auto;
  contain-intrinsic-size: auto 49px;
}
.is-focused :deep(.card) {
  background: color-mix(in srgb, var(--text-primary) 12%, var(--bg-primary));
}
</style>
