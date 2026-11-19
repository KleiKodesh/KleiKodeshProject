<script setup lang="ts">
import { ref, watch } from 'vue'
import IconBookRtl20 from '@/components/IconBookRtl20.vue'
import type { SearchFsItem } from './useBookCatalogSearch'
import { fileIcon, fileTitle } from './fsItemFileIcon'
import { useInputListNavigation } from '@/composables/useInputListNavigation'
import WindowedCardGrid from '@/components/WindowedCardGrid.vue'
import { wantsNewTab, withNewTabHint, OPEN_IN_NEW_TAB_HINT_HE } from '@/composables/useOpenInNewTab'

// LEGACY: superseded by BookCatalogSearch.Cards.vue (same props, emits and
// exposed hooks). Kept, like BookCatalogView.Tiles.vue, to swap back to.
const props = defineProps<{ items: SearchFsItem[] }>()
const emit = defineEmits<{ select: [SearchFsItem, boolean] }>()

// Windowed: results are never capped, so only the lines of tiles near the viewport exist.
const gridRef = ref<InstanceType<typeof WindowedCardGrid> | null>(null)

const { activeIndex: focusedIndex, onKeydown } = useInputListNavigation({
  getCount: () => props.items.length,
  onActivate: (i, openInNewTab) => emit('select', props.items[i]!, openInNewTab),
  scrollIntoView: (index) => gridRef.value?.revealIndex(index),
  getColumnsPerRow: () => gridRef.value?.columns ?? 1,
})

watch(
  () => props.items,
  () => {
    focusedIndex.value = -1
  },
)

const itemTitle = (item: SearchFsItem) => {
  if (item.kind === 'file') return fileTitle(item.file)
  return item.kind === 'toc' ? `${item.book.title} / ${item.tocPath}` : item.book.title
}

function itemTooltip(item: SearchFsItem): string {
  if (item.kind === 'file') return withNewTabHint(item.file.path, OPEN_IN_NEW_TAB_HINT_HE)
  const title = item.kind === 'toc' ? `${item.book.title} / ${item.tocPath}` : item.book.title
  const parts = [title]
  if (item.book.authors) parts.push(item.book.authors)
  if (item.book.parentPath) parts.push(item.book.parentPath)
  return withNewTabHint(parts.join('\n'), OPEN_IN_NEW_TAB_HINT_HE)
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
  <WindowedCardGrid ref="gridRef" :count="items.length" view="tiles" :list-key="items">
    <template #item="{ index: i }">
    <!-- A <button>, like the browse grid's and the home page's: it is a control,
         and unlike a div it does not stretch to fill its grid cell, which is what
         keeps it the same 72px as theirs. -->
    <button
      type="button"
      class="tile"
      data-nav-item
      tabindex="-1"
      :class="{ 'is-focused': focusedIndex === i }"
      :title="itemTooltip(items[i]!)"
      @mousedown.prevent
      @click="selectItem(i, $event)"
      @auxclick.middle="selectItem(i, $event)"
    >
      <div v-if="items[i]!.kind === 'file'" class="tile-icon file-icon" :style="{ color: fileIcon(items[i]!.file).color }"><component :is="fileIcon(items[i]!.file).icon20" /></div>
        <div v-else class="tile-icon"><IconBookRtl20 /></div>
      <span class="tile-label"><span class="tile-label-text">{{ itemTitle(items[i]!) }}</span></span>
      <span v-if="focusedIndex === i" class="tile-expanded" aria-hidden="true">
        <div v-if="items[i]!.kind === 'file'" class="tile-icon file-icon" :style="{ color: fileIcon(items[i]!.file).color }"><component :is="fileIcon(items[i]!.file).icon20" /></div>
        <div v-else class="tile-icon"><IconBookRtl20 /></div>
        <span class="tile-label"><span class="tile-label-text">{{ itemTitle(items[i]!) }}</span></span>
      </span>
    </button>
    </template>
  </WindowedCardGrid>
</template>

<style scoped>
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
  border-radius: var(--item-radius);
  cursor: pointer;
  -webkit-tap-highlight-color: transparent;
}
.tile:hover .tile-icon {
  transform: scale(1.15);
}
.tile:active .tile-icon {
  transform: scale(0.95);
}
/* Current tile: Explorer's selection look (main.css --item-selected-*), the same
   as the home page. A class, not `:focus-visible`: DOM focus stays in the search
   field and this grid only tracks WHICH tile is current. */
.tile.is-focused {
  background: var(--item-selected-bg);
  box-shadow: inset 0 0 0 1px var(--item-selected-border);
  border-radius: 0;
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
  /* Sizes shared with the home page's tiles (main.css --item-*): keep them the same. */
  font-size: var(--item-large-icon);
  transition:
    transform 0.15s ease,
    opacity 0.12s ease;
}
.tile-icon svg {
  width: 1em;
  height: 1em;
  color: #c1440e;
}
/* The file's own colour is set on the box. */
.tile-icon.file-icon svg {
  color: inherit;
}
.tile-label {
  display: block;
  font-size: var(--item-tile-label);
  line-height: 1.3;
  /* Up to N lines; fewer when the title is short. Max, not fixed, so a focused
     tile's full title can still overflow this box. */
  max-height: calc(var(--item-tile-lines) * 1.3em);
}
.tile-label-text {
  font-size: var(--item-tile-label);
  color: var(--text-primary);
  text-align: center;
  line-height: 1.3;
  max-width: 68px;
  overflow: hidden;
  white-space: normal;
  word-break: break-word;
  display: -webkit-box;
  -webkit-line-clamp: var(--item-tile-lines);
  -webkit-box-orient: vertical;
}
/* Focused tile: like Explorer's selected icon, the whole selection box grows to
   fit the full title and lays over the row below. Drawn as a copy on top of the
   tile (only for the focused one), so the tile keeps its size and the grid never
   moves. Same as the home page's tiles (HomeSuggestionCard .tile-expanded). */
.tile {
  position: relative;
}
.tile.is-focused {
  z-index: 3;
}
.tile-expanded {
  position: absolute;
  top: 0;
  inset-inline: 0;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 5px;
  padding: 6px 4px;
  background: var(--item-selected-bg);
  box-shadow: inset 0 0 0 1px var(--item-selected-border);
  pointer-events: none;
}
.tile-expanded .tile-label {
  max-height: none;
}
.tile-expanded .tile-label-text {
  -webkit-line-clamp: unset;
}
</style>
