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

const scrollEl = ref<HTMLElement | null>(null)

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
})

function selectItem(index: number, event?: MouseEvent) {
  focusedIndex.value = index
  activateIndex(index, wantsNewTab(event))
}
</script>

<template>
  <p v-if="!items.length" class="empty">אין פריטים</p>
  <div v-else ref="scrollEl" class="scroller">
    <div class="list-items">
      <div
        v-for="(item, index) in items"
        :key="item.uid"
        class="fs-item"
        data-nav-item
        :class="{ 'is-focused': focusedIndex === index }"
        :title="getTooltip(item)"
        @click="selectItem(index, $event)"
        @auxclick.middle="selectItem(index, $event)"
      >
        <span class="icon" :class="item.kind === 'folder' ? 'folder-icon' : 'book-icon'">
          <IconFolder20Filled v-if="item.kind === 'folder'" />
          <IconBookRtl20 v-else />
        </span>
        <span class="title">{{ getTitle(item) }}</span>
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
.list-items {
  min-height: 100%;
}
.fs-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 0 12px;
  height: var(--catalog-row-height, 38px);
  cursor: pointer;
  box-sizing: border-box;
  transition: background 0.1s;
}
.fs-item:hover {
  background: var(--hover-bg);
}
.fs-item:active {
  background: var(--active-bg);
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
}
</style>
