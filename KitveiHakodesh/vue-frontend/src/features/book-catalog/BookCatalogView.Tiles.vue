<script setup lang="ts">
import { ref, watch } from 'vue'
import { IconFolder20Filled } from '@iconify-prerendered/vue-fluent'
import IconBookRtl20 from '@/components/IconBookRtl20.vue'
import type { FsItem } from './useBookCatalog'
import type { CategoryNode } from '@/features/book-catalog/bookCatalogTree'
import type { BookRow } from '@/webview-host/queries.types'
import { useInputListNavigation } from '@/composables/useInputListNavigation'
import { wantsNewTab, withNewTabHint } from '@/composables/useOpenInNewTab'

const props = defineProps<{ items: FsItem[] }>()
const emit = defineEmits<{ selectBook: [BookRow, boolean?]; enterFolder: [CategoryNode] }>()

const tilesEl = ref<HTMLElement | null>(null)

function activateIndex(index: number, openInNewTab = false) {
  const item = props.items[index]
  if (!item) return
  item.kind === 'folder'
    ? emit('enterFolder', item.node)
    : emit('selectBook', item.book, openInNewTab)
}

function getTitle(item: FsItem) {
  return item.kind === 'folder' ? item.node.title : item.book.title
}

// Books open in a tab (so they get the new-tab hint); folders just navigate in.
function getTooltip(item: FsItem) {
  return item.kind === 'folder' ? item.node.title : withNewTabHint(item.book.title)
}

// Tiles wrap into rows, so one ArrowDown step is one visual ROW. Count the tiles
// sharing the first one's offsetTop — exact for any width or gap, with no tile-size
// constant to keep in sync with the CSS.
function getColumnsPerRow(): number {
  const tiles = tilesEl.value?.querySelectorAll<HTMLElement>('[data-nav-item]')
  if (!tiles?.length) return 1
  const firstRowTop = tiles[0]!.offsetTop
  let columns = 0
  while (columns < tiles.length && tiles[columns]!.offsetTop === firstRowTop) columns++
  return Math.max(1, columns)
}

// Combobox model, the same one the search results use: DOM focus never leaves the
// page's search input — the page forwards its keydown here and the arrows move a
// HIGHLIGHT through the grid. Previously this grid owned its own focus, so the
// first ArrowDown pulled the caret out of the field and the user could not keep
// typing.
const { activeIndex: focusedIndex, onKeydown } = useInputListNavigation({
  getCount: () => props.items.length,
  onActivate: activateIndex,
  containerElement: tilesEl,
  getColumnsPerRow,
})

// Entering a folder swaps the whole item list, so the old highlight would point at
// a different row — and a highlight past the new end sends the next ArrowDown to
// the LAST item instead of the first, since moveTo clamps. useInputListNavigation
// leaves this to the caller by contract.
watch(
  () => props.items,
  () => {
    focusedIndex.value = -1
  },
)

defineExpose({
  onSearchInputKeydown: (event: KeyboardEvent) => onKeydown(event),
})

function selectItem(i: number, event?: MouseEvent) {
  focusedIndex.value = i
  activateIndex(i, wantsNewTab(event))
}
</script>

<template>
  <p v-if="!items.length" class="empty">אין פריטים</p>
  <div v-else ref="tilesEl" class="tiles-grid">
    <!-- A <button>, like the home page tiles: it is a control, so it gets the
         semantics for free — and, unlike a div, it does not stretch to fill its
         grid cell, which is what keeps it the same 72px as home's. -->
    <button
      v-for="(item, i) in items"
      :key="item.uid"
      type="button"
      class="tile"
      data-nav-item
      tabindex="-1"
      :class="{ 'is-focused': focusedIndex === i }"
      :title="getTooltip(item)"
      @click="selectItem(i, $event)"
      @auxclick.middle="selectItem(i, $event)"
    >
      <div class="tile-icon" :class="item.kind === 'folder' ? 'folder-icon' : 'book-icon'">
        <IconFolder20Filled v-if="item.kind === 'folder'" /><IconBookRtl20 v-else />
      </div>
      <span class="tile-label">{{ getTitle(item) }}</span>
    </button>
  </div>
</template>

<style scoped>
.empty {
  padding: 24px 16px;
  color: var(--text-secondary);
  font-size: 14px;
  text-align: center;
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
  padding: 6px 4px;
  background: none;
  border: none;
  border-radius: 6px;
  cursor: pointer;
  -webkit-tap-highlight-color: transparent;
  position: relative;
}
/* Same three states, same curve and scales as the home page tiles
   (HomePageTile.vue) — the icon grows on hover, grows further when it is the
   keyboard-focused tile, and dips on press. Only the icon moves; the label and
   the tile box stay put, so a grid of them does not jitter. */
.tile:hover .tile-icon {
  transform: scale(1.15);
}
.tile:active .tile-icon {
  transform: scale(0.95);
}
/* The home tiles are real buttons and show keyboard focus through
   `:focus-visible` — the icon simply grows. This grid drives focus itself (the
   container holds the tabstop and marks the active tile), so the same look has to
   come from the class. That also means opting out of the global
   `[data-nav-item].is-focused` background, which home never triggers: with the
   grown icon doing the work, the filled square underneath is a second, louder
   focus ring saying the same thing. */
.tile.is-focused {
  background: none;
}
.tile.is-focused .tile-icon {
  transform: scale(1.25);
}
.tile .tile-icon {
  position: relative;
  display: flex;
  align-items: center;
  justify-content: center;
  width: 48px;
  height: 48px;
  border-radius: 6px;
  /* No chip behind the glyph, as on the home page: the icon itself carries the
     colour, and the tile reads lighter without a filled square under every one. */
  background: none;
  font-size: 28px;
  transition:
    transform 0.15s ease,
    opacity 0.12s ease;
}
/* Sized by the icon font, the way the home tiles size theirs, rather than by a
   fixed px box — so both pages scale from the same number. */
.tile .tile-icon svg {
  width: 1em;
  height: 1em;
}
.tile .folder-icon svg {
  color: var(--status-warning);
}
.tile .book-icon svg {
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
