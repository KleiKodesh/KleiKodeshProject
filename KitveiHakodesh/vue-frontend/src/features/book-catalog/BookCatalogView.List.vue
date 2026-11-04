<script setup lang="ts">
import { ref, watch } from 'vue'
import { IconFolder20Filled, IconDelete16Regular } from '@iconify-prerendered/vue-fluent'
import IconBookRtl20 from '@/components/IconBookRtl20.vue'
import type { FsItem } from './useBookCatalog'
import type { CategoryNode, LocalFileItem } from '@/features/book-catalog/bookCatalogTree'
import { fileIcon, fileTitle } from './fsItemFileIcon'
import type { BookRow } from '@/webview-host/queries.types'
import { useInputListNavigation } from '@/composables/useInputListNavigation'
import { wantsNewTab, withNewTabHint, OPEN_IN_NEW_TAB_HINT_HE } from '@/composables/useOpenInNewTab'
import { titleWithAuthor } from './titleWithAuthor'

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

const scrollEl = ref<HTMLElement | null>(null)

function activateIndex(index: number, openInNewTab = false) {
  const item = props.items[index]
  if (!item) return
  if (item.kind === 'folder') emit('enterFolder', item.node)
  else if (item.kind === 'file') emit('selectFile', item.file, openInNewTab)
  else emit('selectBook', item.book, openInNewTab)
}

function getTitle(item: FsItem) {
  if (item.kind === 'file') return fileTitle(item.file)
  return item.kind === 'folder' ? item.node.title : titleWithAuthor(item.book.title, item.book.authors)
}

// Books open in a tab (so they get the new-tab hint); folders just navigate in.
function getTooltip(item: FsItem) {
  if (item.kind === 'file') return withNewTabHint(item.file.path, OPEN_IN_NEW_TAB_HINT_HE)
  return item.kind === 'folder' ? item.node.title : withNewTabHint(item.book.title, OPEN_IN_NEW_TAB_HINT_HE)
}

// Combobox model (see useInputListNavigation): DOM focus stays in the page's
// search input, which forwards its keydown here to move a highlight through this
// list. Nothing here may take focus, or the caret leaves the field mid-type.
const { activeIndex: focusedIndex, onKeydown } = useInputListNavigation({
  getCount: () => props.items.length,
  onActivate: activateIndex,
  containerElement: scrollEl,
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

function selectItem(index: number, event?: MouseEvent) {
  focusedIndex.value = index
  activateIndex(index, wantsNewTab(event))
}
</script>

<template>
  <p v-if="!items.length" class="empty">{{ $t('bookCatalog.noItems') }}</p>
  <div v-else ref="scrollEl" class="scroller">
    <div class="list-items">
      <div
        v-for="(item, index) in items"
        :key="item.uid"
        class="fs-item"
        data-nav-item
        :class="{ 'is-focused': focusedIndex === index }"
        :title="getTooltip(item)"
        @mousedown.prevent
        @click="selectItem(index, $event)"
        @auxclick.middle="selectItem(index, $event)"
      >
        <span v-if="item.kind === 'file'" class="icon file-icon" :style="{ color: fileIcon(item.file).color }">
          <component :is="fileIcon(item.file).icon20" />
        </span>
        <span v-else class="icon" :class="item.kind === 'folder' ? 'folder-icon' : 'book-icon'">
          <IconFolder20Filled v-if="item.kind === 'folder'" />
          <IconBookRtl20 v-else />
        </span>
        <span class="title">{{ getTitle(item) }}</span>
        <button
          v-if="item.kind === 'folder' && canRemove?.(item.node)"
          type="button"
          class="row-remove"
          tabindex="-1"
          :title="$t('userSpace.removeRoot')"
          @mousedown.prevent
          @click.stop="emit('removeFolder', item.node)"
        >
          <IconDelete16Regular />
        </button>
      </div>
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
.scroller {
  height: 100%;
  overflow-y: auto;
  scrollbar-width: thin;
}
.list-items {
  min-height: 100%;
}
.fs-item {
  display: flex;
  align-items: center;
  /* Sizes shared with the home page's list (main.css --item-*): keep them the same. */
  gap: var(--item-gap);
  padding: 0 var(--item-padding-inline);
  height: var(--catalog-row-height, 38px);
  cursor: pointer;
  box-sizing: border-box;
  transition: background 0.1s;
  /* Off-screen rows skip rendering — big folders without a virtualizer. */
  content-visibility: auto;
  contain-intrinsic-size: auto var(--catalog-row-height, 38px);
}
.fs-item:hover {
  background: var(--hover-bg);
}
/* Selected: Explorer's selection look, shared with the home page (main.css --item-selected-*). */
.fs-item.is-focused {
  background: var(--item-selected-bg);
  box-shadow: inset 0 0 0 1px var(--item-selected-border);
  border-radius: 0;
}
.fs-item:active {
  background: var(--active-bg);
}
.icon {
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  font-size: var(--item-icon);
}
.folder-icon svg {
  color: var(--status-warning);
}
.book-icon svg {
  color: #c1440e;
}
/* The file's own colour is set on the span; the theme pins svg colour, so inherit it. */
.file-icon svg {
  color: inherit;
}
/* UserSpace roots: remove, shown on hover or keyboard highlight. */
.row-remove {
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  width: 22px;
  height: 22px;
  padding: 0;
  margin-inline-start: auto;
  border-radius: 4px;
  color: var(--text-secondary);
  opacity: 0;
  pointer-events: none;
}
.fs-item:hover .row-remove,
.fs-item.is-focused .row-remove {
  opacity: 1;
  pointer-events: auto;
}
.row-remove:hover {
  color: var(--status-danger);
  background: var(--hover-bg);
}
.title {
  font-size: var(--item-title);
  color: var(--text-primary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  min-width: 0;
}
</style>
