<script setup lang="ts">
import { computed, nextTick, ref, shallowRef, watch } from 'vue'
import {
  IconChevronDown16Regular,
  IconChevronLeft16Regular,
  IconFolder20Filled,
  IconFolderOpen20Filled,
} from '@iconify-prerendered/vue-fluent'
import IconBookRtl20 from '@/components/IconBookRtl20.vue'
import type { CategoryNode } from '@/features/book-catalog/bookCatalogTree'
import type { BookRow } from '@/webview-host/queries.types'
import { useInputListNavigation } from '@/composables/useInputListNavigation'
import { wantsNewTab, withNewTabHint, OPEN_IN_NEW_TAB_HINT_HE } from '@/composables/useOpenInNewTab'
import { titleWithAuthor } from './titleWithAuthor'

// The whole catalog as one tree, the way Otzaria's library browser shows it: every
// shelf is a node that unfolds in place — subfolders first, then its books — rather
// than a folder you enter. The rows, icons and highlight are the catalog list's
// own, so switching between the two views changes the shape, not the look.
const props = defineProps<{
  /** The browsed root (hidden categories already removed). */
  root: CategoryNode
  /** The page's path. Arriving at a folder (home card, crumb) unfolds the tree to it. */
  path: CategoryNode[]
}>()
const emit = defineEmits<{ selectBook: [BookRow, boolean?] }>()

type TreeRow =
  | { uid: string; kind: 'folder'; node: CategoryNode; level: number; expanded: boolean }
  | { uid: string; kind: 'book'; book: BookRow; level: number }

const scrollEl = ref<HTMLElement | null>(null)
// Category ids that are open. A fresh Set on every change so the rows recompute.
const expanded = shallowRef<Set<number>>(new Set())

function setExpanded(id: number, open: boolean) {
  const next = new Set(expanded.value)
  open ? next.add(id) : next.delete(id)
  expanded.value = next
}
function toggle(node: CategoryNode) {
  setExpanded(node.id, !expanded.value.has(node.id))
}

// Only the open branches are flattened, so a collapsed catalog is a few dozen rows
// however many thousand books it holds.
const rows = computed((): TreeRow[] => {
  const out: TreeRow[] = []
  const walk = (node: CategoryNode, level: number) => {
    for (const child of node.children) {
      const isOpen = expanded.value.has(child.id)
      out.push({ uid: `f-${child.id}`, kind: 'folder', node: child, level, expanded: isOpen })
      if (isOpen) walk(child, level + 1)
    }
    for (const book of node.books) out.push({ uid: `b-${book.id}`, kind: 'book', book, level })
  }
  walk(props.root, 0)
  return out
})

function activateIndex(index: number, openInNewTab = false) {
  const row = rows.value[index]
  if (!row) return
  row.kind === 'folder' ? toggle(row.node) : emit('selectBook', row.book, openInNewTab)
}

function getTitle(row: TreeRow) {
  return row.kind === 'folder' ? row.node.title : titleWithAuthor(row.book.title, row.book.authors)
}
function getTooltip(row: TreeRow) {
  return row.kind === 'folder' ? row.node.title : withNewTabHint(row.book.title, OPEN_IN_NEW_TAB_HINT_HE)
}

// Combobox model (see useInputListNavigation): DOM focus stays in the page's
// search input, which forwards its keydown here. Enter on a folder unfolds it,
// Enter on a book opens it. Nothing here may take focus.
const { activeIndex: focusedIndex, onKeydown } = useInputListNavigation({
  getCount: () => rows.value.length,
  onActivate: activateIndex,
  containerElement: scrollEl,
})

// A reloaded catalog (new root) leaves the highlight pointing at a different row.
watch(
  () => props.root,
  () => {
    focusedIndex.value = -1
  },
)

// The path is the page's idea of "where you are". A folder reached from outside
// the tree (a home card, a breadcrumb) opens along the whole chain and lands the
// highlight on it; the root alone means nothing to unfold.
watch(
  () => props.path,
  (path) => {
    const chain = path.slice(1)
    if (!chain.length) return
    const next = new Set(expanded.value)
    for (const node of chain) next.add(node.id)
    expanded.value = next
    const target = chain[chain.length - 1]!
    const index = rows.value.findIndex((r) => r.kind === 'folder' && r.node.id === target.id)
    focusedIndex.value = index
    // A direct index write skips the composable's scrolling; land the row ourselves.
    void nextTick(() =>
      scrollEl.value?.querySelectorAll('[data-nav-item]')[index]?.scrollIntoView({ block: 'nearest' }),
    )
  },
  { immediate: true },
)

defineExpose({
  onSearchInputKeydown: (event: KeyboardEvent) => onKeydown(event),
  highlightFirst: () => {
    if (rows.value.length) focusedIndex.value = 0
  },
  /** Folds every shelf back up and returns to the top — the bar's home button. */
  reset: () => {
    expanded.value = new Set()
    focusedIndex.value = -1
    if (scrollEl.value) scrollEl.value.scrollTop = 0
  },
})

function selectRow(index: number, event?: MouseEvent) {
  focusedIndex.value = index
  activateIndex(index, wantsNewTab(event))
}
</script>

<template>
  <p v-if="!rows.length" class="empty">{{ $t('bookCatalog.noItems') }}</p>
  <div v-else ref="scrollEl" class="scroller">
    <div class="tree-items">
      <div
        v-for="(row, index) in rows"
        :key="row.uid"
        class="fs-item"
        data-nav-item
        :class="{ 'is-focused': focusedIndex === index }"
        :style="{ '--level': row.level }"
        :title="getTooltip(row)"
        @mousedown.prevent
        @click="selectRow(index, $event)"
        @auxclick.middle="selectRow(index, $event)"
      >
        <!-- Folders carry a chevron; books an empty slot of the same width, so the
             icons of a folder and the books inside it line up one level apart. -->
        <span class="chevron">
          <template v-if="row.kind === 'folder'">
            <IconChevronDown16Regular v-if="row.expanded" />
            <IconChevronLeft16Regular v-else class="dir-icon" />
          </template>
        </span>
        <span class="icon" :class="row.kind === 'folder' ? 'folder-icon' : 'book-icon'">
          <template v-if="row.kind === 'folder'">
            <IconFolderOpen20Filled v-if="row.expanded" />
            <IconFolder20Filled v-else />
          </template>
          <IconBookRtl20 v-else />
        </span>
        <span class="title">{{ getTitle(row) }}</span>
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
}
.tree-items {
  min-height: 100%;
}
/* The list view's row, indented by depth. */
.fs-item {
  display: flex;
  align-items: center;
  gap: 6px;
  padding-inline: calc(8px + var(--level, 0) * 18px) 12px;
  height: var(--catalog-row-height, 38px);
  cursor: pointer;
  box-sizing: border-box;
  transition: background 0.1s;
  content-visibility: auto;
  contain-intrinsic-size: auto var(--catalog-row-height, 38px);
}
.fs-item:hover {
  background: var(--hover-bg);
}
.fs-item:active {
  background: var(--active-bg);
}
.chevron {
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  width: 16px;
  height: 16px;
  color: var(--text-secondary);
}
.icon {
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  font-size: 20px;
}
.folder-icon svg {
  color: var(--status-warning);
}
.book-icon svg {
  color: #c1440e;
}
.title {
  font-size: 14px;
  color: var(--text-primary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  min-width: 0;
}
</style>
