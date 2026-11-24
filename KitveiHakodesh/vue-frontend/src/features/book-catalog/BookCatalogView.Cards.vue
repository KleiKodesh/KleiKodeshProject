<script setup lang="ts">
import { t } from '@/i18n'
import { ref, computed, watch, markRaw } from 'vue'
import { IconFolder20Filled } from '@iconify-prerendered/vue-fluent'
import IconBookRtl20 from '@/components/IconBookRtl20.vue'
import HomeSuggestionCard from '@/features/home/HomeSuggestionCard.vue'
import type { HomeSuggestionCard as CardData } from '@/features/home/homeSuggestionCard'
import type { FsItem } from './useBookCatalog'
import type { CategoryNode, LocalFileItem } from '@/features/book-catalog/bookCatalogTree'
import { fileIcon, fileTitle } from './fsItemFileIcon'
import type { BookRow } from '@/webview-host/queries.types'
import { useInputListNavigation, countGridColumns } from '@/composables/useInputListNavigation'
import { titleWithAuthor } from './titleWithAuthor'
import { stripCommentaryTarget } from './stripCommentaryTarget'
import { useFolderReorder } from './useFolderReorder'

// Drop-in replacement for BookCatalogView.Tiles.vue: same props, emits and
// exposed keyboard hook, rendered with the home page's card look.
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
  /** UserSpace only: a root dragged onto another root's place. */
  moveFolder: [CategoryNode, CategoryNode]
}>()
const reorder = useFolderReorder(() => props.canRemove, (from, to) => emit('moveFolder', from, to))

const cardsEl = ref<HTMLElement | null>(null)
const CARD_CELL = ':scope > *'
const FOLDER_ICON = markRaw(IconFolder20Filled)
const BOOK_ICON = markRaw(IconBookRtl20)

function activateIndex(index: number, openInNewTab = false) {
  const item = props.items[index]
  if (!item) return
  if (item.kind === 'folder') emit('enterFolder', item.node)
  else if (item.kind === 'file') emit('selectFile', item.file, openInNewTab)
  else emit('selectBook', item.book, openInNewTab)
}

/** Secondary line: book count for a folder; a book's author is in its title. */
function getSecondaryLine(item: FsItem): string {
  if (item.kind !== 'folder') return ''
  const count = item.node.subtreeBookIds?.length ?? 0
  return count ? t('bookCatalog.bookCount', { n: count }) : ''
}

function getTitle(item: FsItem): string {
  if (item.kind === 'folder') return item.node.title
  if (item.kind === 'file') return fileTitle(item.file)
  return titleWithAuthor(stripCommentaryTarget(item.book.title), item.book.authors)
}

function folderOf(i: number): CategoryNode | null {
  const item = props.items[i]
  return item?.kind === 'folder' ? item.node : null
}

const cards = computed((): CardData[] =>
  props.items.map((item, i) => ({
    key: item.uid,
    title: getTitle(item),
    path: getSecondaryLine(item),
    icon: item.kind === 'folder' ? FOLDER_ICON : item.kind === 'file' ? markRaw(fileIcon(item.file).icon20) : BOOK_ICON,
    color: item.kind === 'folder' ? 'var(--status-warning)' : item.kind === 'file' ? fileIcon(item.file).color : '#c1440e',
    remove: item.kind === 'folder' && props.canRemove?.(item.node) ? () => emit('removeFolder', item.node) : undefined,
    move:
      item.kind === 'folder' && props.canRemove?.(item.node)
        ? { title: t('userSpace.moveRoot'), events: reorder.handleEvents(item.node) as Record<string, (e: never) => void> }
        : undefined,
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
  // The grid cells are the card wrappers. The card's inner button has its own
  // data-nav-item, but its offsetTop is relative to the wrapper (always 0), so
  // counting buttons would give the wrong number of columns.
  itemSelector: CARD_CELL,
  getColumnsPerRow: () => countGridColumns(cardsEl.value, CARD_CELL),
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
  /** Highlights the first item — the page calls it after a keyboard drill-down. */
  highlightFirst: () => {
    if (props.items.length) focusedIndex.value = 0
  },
})
</script>

<template>
  <p v-if="!items.length" class="empty">{{ $t('bookCatalog.noItems') }}</p>
  <!-- Host is the size container so the grid's cqi tracks the pane width. -->
  <div v-else class="catalog-cards-host">
    <div ref="cardsEl" class="catalog-cards" @mousedown.prevent>
      <HomeSuggestionCard
        v-for="(card, i) in cards"
        :key="card.key"
        :card="card"
        :class="{ 'is-focused': focusedIndex === i, 'is-drop-target': items[i]?.kind === 'folder' && reorder.over.value === folderOf(i) }"
        v-bind="folderOf(i) ? reorder.targetAttrs(folderOf(i)!) : {}"
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
  /* Pure CSS, no measuring: as many 140px+ columns as fit, sharing the width. Cards
     shrink to fit another column; with two or more columns none passes ~220px. */
  grid-template-columns: repeat(auto-fill, minmax(min(100%, 140px), 1fr));
  /* Explorer-like: wide columns, tight rows, so each row reads as one line. */
  column-gap: 16px;
  row-gap: 4px;
  padding: 12px;
  overflow-x: hidden;
  overflow-y: auto;
  scrollbar-width: thin;
  height: 100%;
  box-sizing: border-box;
  align-content: flex-start;
}
/* Keyboard highlight: DOM focus never reaches a card, so it comes from a class
   on the card's root wrapper and is painted on the inner button. */
/* Off-screen cards skip rendering — big folders without a virtualizer. */
.catalog-cards > * {
  content-visibility: auto;
  contain-intrinsic-size: auto 49px;
}
/* Title and path each wrap to at most 2 lines, then end with an ellipsis. */
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
.is-focused :deep(.card-action) {
  opacity: 1;
  pointer-events: auto;
}
.is-drop-target :deep(.card) {
  box-shadow: inset 0 0 0 2px var(--accent-color);
}
.is-focused :deep(.card) {
  background: color-mix(in srgb, var(--text-primary) 12%, var(--bg-primary));
}
</style>
