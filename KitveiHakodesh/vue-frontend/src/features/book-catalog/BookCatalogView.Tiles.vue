<script setup lang="ts">
import { ref, watch } from 'vue'
import { IconFolder20Filled } from '@iconify-prerendered/vue-fluent'
import IconBookRtl20 from '@/components/IconBookRtl20.vue'
import type { FsItem } from './useBookCatalog'
import type { CategoryNode } from '@/features/book-catalog/bookCatalogTree'
import type { BookRow } from '@/webview-host/queries.types'
import { useInputListNavigation, countGridColumns } from '@/composables/useInputListNavigation'
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


// Combobox model (see useInputListNavigation): DOM focus stays in the page's
// search input, which forwards its keydown here to move a highlight through this
// grid. Nothing here may take focus, or the caret leaves the field mid-type.
const { activeIndex: focusedIndex, onKeydown } = useInputListNavigation({
  getCount: () => props.items.length,
  onActivate: activateIndex,
  containerElement: tilesEl,
  getColumnsPerRow: () => countGridColumns(tilesEl.value),
})

// Required by useInputListNavigation's contract: a new item list leaves the old
// highlight pointing at a different item, and one past the new end sends the next
// ArrowDown to the LAST item, since moveTo clamps.
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
/* Keyboard focus reads as the grown icon, the same as the home page's tiles. It
   has to come from a class rather than `:focus-visible`, because DOM focus never
   reaches a tile — it stays in the search field, and this grid only tracks WHICH
   tile is current. That also means opting out of the global
   `[data-nav-item].is-focused` background: with the grown icon doing the work,
   the filled square underneath is a second, louder ring saying the same thing. */
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
