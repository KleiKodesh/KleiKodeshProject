<script setup lang="ts">
import { ref, computed, watch, markRaw } from 'vue'
import IconBookRtl20 from '@/components/IconBookRtl20.vue'
import HomeSuggestionCard from '@/features/home/HomeSuggestionCard.vue'
import type { HomeSuggestionCard as CardData } from '@/features/home/homeSuggestionCard'
import type { SearchFsItem } from './useBookCatalogSearch'
import { useInputListNavigation, countGridColumns } from '@/composables/useInputListNavigation'

// Drop-in replacement for BookCatalogSearch.Tiles.vue: same props, emits and
// exposed keyboard hook, rendered with the browse view's card look
// (BookCatalogView.Cards.vue).
const props = defineProps<{ items: SearchFsItem[] }>()
const emit = defineEmits<{ select: [SearchFsItem, boolean] }>()

const cardsEl = ref<HTMLElement | null>(null)
const CARD_CELL = ':scope > *'
const BOOK_ICON = markRaw(IconBookRtl20)

const itemTitle = (item: SearchFsItem) =>
  item.kind === 'toc' ? `${item.book.title} ${item.tocPath}` : item.book.title

const cards = computed((): CardData[] =>
  props.items.map((item, i) => ({
    key: item.uid,
    title: itemTitle(item),
    path: item.book.authors ?? '',
    icon: BOOK_ICON,
    color: '#c1440e',
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
  grid-template-columns: repeat(auto-fill, minmax(min(100%, clamp(140px, 30cqi, 220px)), 1fr));
  gap: 8px;
  padding: 12px;
  overflow-x: hidden;
  overflow-y: auto;
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
  background: var(--input-bg-focus);
}
</style>
