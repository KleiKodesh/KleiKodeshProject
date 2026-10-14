<script setup lang="ts">
import { ref, watch } from 'vue'
import IconBookRtl20 from '@/components/IconBookRtl20.vue'
import type { SearchFsItem } from './useBookCatalogSearch'
import { useInputListNavigation, countGridColumns } from '@/composables/useInputListNavigation'
import { wantsNewTab, withNewTabHint } from '@/composables/useOpenInNewTab'

// LEGACY: superseded by BookCatalogSearch.Cards.vue (same props, emits and
// exposed hooks). Kept, like BookCatalogView.Tiles.vue, to swap back to.
const props = defineProps<{ items: SearchFsItem[] }>()
const emit = defineEmits<{ select: [SearchFsItem, boolean] }>()

const tilesEl = ref<HTMLElement | null>(null)

const { activeIndex: focusedIndex, onKeydown } = useInputListNavigation({
  getCount: () => props.items.length,
  onActivate: (i, openInNewTab) => emit('select', props.items[i]!, openInNewTab),
  containerElement: tilesEl,
  getColumnsPerRow: () => countGridColumns(tilesEl.value),
})

watch(
  () => props.items,
  () => {
    focusedIndex.value = -1
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

function selectItem(i: number, event?: MouseEvent) {
  focusedIndex.value = i
  emit('select', props.items[i]!, wantsNewTab(event))
}

defineExpose({
  onSearchInputKeydown: (event: KeyboardEvent) => onKeydown(event),
  /** Read/written by the parent to carry the highlight across a view switch. */
  focusedIndex,
})
</script>

<template>
  <div ref="tilesEl" class="tiles-grid">
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
      :class="{ 'is-focused': focusedIndex === i }"
      :title="itemTooltip(item)"
      @mousedown.prevent
      @click="selectItem(i, $event)"
      @auxclick.middle="selectItem(i, $event)"
    >
      <div class="tile-icon"><IconBookRtl20 /></div>
      <span class="tile-label">{{ itemTitle(item) }}</span>
    </button>
  </div>
</template>

<style scoped>
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
  width: 40px;
  height: 40px;
  border-radius: 6px;
  background: none;
  font-size: 24px;
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
