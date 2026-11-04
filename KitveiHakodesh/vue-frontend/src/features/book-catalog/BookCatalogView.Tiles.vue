<script setup lang="ts">
import { ref, watch } from 'vue'
import { IconFolder20Filled, IconDelete16Regular } from '@iconify-prerendered/vue-fluent'
import IconBookRtl20 from '@/components/IconBookRtl20.vue'
import type { FsItem } from './useBookCatalog'
import type { CategoryNode, LocalFileItem } from '@/features/book-catalog/bookCatalogTree'
import { fileIcon, fileTitle } from './fsItemFileIcon'
import type { BookRow } from '@/webview-host/queries.types'
import { useInputListNavigation, countGridColumns } from '@/composables/useInputListNavigation'
import { wantsNewTab, withNewTabHint, OPEN_IN_NEW_TAB_HINT_HE } from '@/composables/useOpenInNewTab'

const props = defineProps<{
  items: FsItem[]
  /** UserSpace only: which folders carry a remove action (its roots). */
  canRemove?: (node: CategoryNode) => boolean
}>()
const emit = defineEmits<{
  selectBook: [BookRow, boolean?]
  enterFolder: [CategoryNode]
  /** UserSpace only: a file from a computer folder. */
  selectFile: [LocalFileItem, boolean?]
  /** UserSpace only: the remove action on a folder that canRemove allows. */
  removeFolder: [CategoryNode]
}>()

const tilesEl = ref<HTMLElement | null>(null)

function activateIndex(index: number, openInNewTab = false) {
  const item = props.items[index]
  if (!item) return
  if (item.kind === 'folder') emit('enterFolder', item.node)
  else if (item.kind === 'file') emit('selectFile', item.file, openInNewTab)
  else emit('selectBook', item.book, openInNewTab)
}

function getTitle(item: FsItem) {
  if (item.kind === 'file') return fileTitle(item.file)
  return item.kind === 'folder' ? item.node.title : item.book.title
}

// Books open in a tab (so they get the new-tab hint); folders just navigate in.
function getTooltip(item: FsItem) {
  if (item.kind === 'file') return withNewTabHint(item.file.path, OPEN_IN_NEW_TAB_HINT_HE)
  return item.kind === 'folder' ? item.node.title : withNewTabHint(item.book.title, OPEN_IN_NEW_TAB_HINT_HE)
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
  /** Highlights the first item — the page calls it after a keyboard drill-down. */
  highlightFirst: () => {
    if (props.items.length) focusedIndex.value = 0
  },
})

function selectItem(i: number, event?: MouseEvent) {
  focusedIndex.value = i
  activateIndex(i, wantsNewTab(event))
}
</script>

<template>
  <p v-if="!items.length" class="empty">{{ $t('bookCatalog.noItems') }}</p>
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
      @mousedown.prevent
      @click="selectItem(i, $event)"
      @auxclick.middle="selectItem(i, $event)"
    >
      <div v-if="item.kind === 'file'" class="tile-icon file-icon" :style="{ color: fileIcon(item.file).color }">
          <component :is="fileIcon(item.file).icon20" />
        </div>
        <div v-else class="tile-icon" :class="item.kind === 'folder' ? 'folder-icon' : 'book-icon'">
        <IconFolder20Filled v-if="item.kind === 'folder'" /><IconBookRtl20 v-else />
      </div>
      <span class="tile-label"><span class="tile-label-text">{{ getTitle(item) }}</span></span>
      <!-- A span, not a button: the tile itself is a <button>, and buttons cannot nest. -->
      <span
        v-if="item.kind === 'folder' && canRemove?.(item.node)"
        class="tile-remove"
        role="button"
        :title="$t('userSpace.removeRoot')"
        @mousedown.prevent
        @click.stop="emit('removeFolder', item.node)"
      >
        <IconDelete16Regular />
      </span>
      <span v-if="focusedIndex === i" class="tile-expanded" aria-hidden="true">
        <div v-if="item.kind === 'file'" class="tile-icon file-icon" :style="{ color: fileIcon(item.file).color }">
          <component :is="fileIcon(item.file).icon20" />
        </div>
        <div v-else class="tile-icon" :class="item.kind === 'folder' ? 'folder-icon' : 'book-icon'">
          <IconFolder20Filled v-if="item.kind === 'folder'" /><IconBookRtl20 v-else />
        </div>
        <span class="tile-label"><span class="tile-label-text">{{ getTitle(item) }}</span></span>
      </span>
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
  scrollbar-width: thin;
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
  border-radius: var(--item-radius);
  cursor: pointer;
  -webkit-tap-highlight-color: transparent;
}
/* Three states — the icon grows on hover, grows further when it is the
   keyboard-focused tile, and dips on press. Only the icon moves; the label and
   the tile box stay put, so a grid of them does not jitter. */
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
.tile .tile-icon {
  position: relative;
  display: flex;
  align-items: center;
  justify-content: center;
  width: 40px;
  height: 40px;
  border-radius: 6px;
  /* No chip behind the glyph, as on the home page: the icon itself carries the
     colour, and the tile reads lighter without a filled square under every one. */
  background: none;
  /* Sizes shared with the home page's tiles (main.css --item-*): keep them the same. */
  font-size: var(--item-large-icon);
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
/* The file's own colour is set on the box; the theme pins svg colour, so inherit it. */
.tile .file-icon svg {
  color: inherit;
}
/* UserSpace roots: remove in the tile's corner, shown on hover or keyboard highlight. */
.tile {
  position: relative;
}
.tile-remove {
  position: absolute;
  top: 2px;
  inset-inline-end: 2px;
  z-index: 3;
  display: flex;
  align-items: center;
  justify-content: center;
  width: 20px;
  height: 20px;
  border-radius: 50%;
  border: 1px solid var(--border-color);
  background: var(--bg-primary);
  color: var(--text-secondary);
  font-size: 12px;
  opacity: 0;
  pointer-events: none;
}
.tile:hover .tile-remove,
.tile.is-focused .tile-remove {
  opacity: 1;
  pointer-events: auto;
}
.tile-remove:hover {
  color: var(--status-danger);
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
