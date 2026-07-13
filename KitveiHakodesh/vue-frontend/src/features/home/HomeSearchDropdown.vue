<script setup lang="ts">
import { ref, computed } from 'vue'
import {
  IconLibrary20Filled,
  IconBookOpen20Filled,
  IconArrowSync20Regular,
  IconDocument20Filled,
  IconDocumentPdf20Filled,
  IconDocumentText20Filled,
  IconDocumentGlobe20Filled,
} from '@iconify-prerendered/vue-fluent'
import { useListKeys } from '@/composables/useListKeyNav'
import type {
  CatalogSearchResult,
  HebrewBooksSearchResult,
  FileSearchResult,
  SearchSourcePriority,
} from './useHomeSearch'
import type { TocFsItem } from '@/features/book-catalog/useBookCatalogSearch'
import type { HebrewBook } from '@/features/hebrewbooks/hebrewBooksCatalog'

const props = defineProps<{
  catalogResults: CatalogSearchResult[]
  /**
   * TOC heuristics results — from the zero-results fallback (books empty) or
   * the additive keyword trigger (shown below the book results).
   */
  catalogTocResults: TocFsItem[]
  hebrewBooksResults: HebrewBooksSearchResult[]
  fileResults: FileSearchResult[]
  sourcePriority: SearchSourcePriority
  isLoadingCatalogToc: boolean
  isLoadingHebrewBooks: boolean
  isLoadingFiles: boolean
  // Position and size passed from parent via getBoundingClientRect
  anchorTop: number
  anchorLeft: number
  anchorRight: number
  maxHeight: number
}>()

const emit = defineEmits<{
  selectCatalogBook: [bookId: number, bookTitle: string]
  selectCatalogToc: [item: TocFsItem]
  selectHebrewBook: [book: HebrewBook]
  selectFile: [fullPath: string, fileName: string]
  dropdownFocused: []
  dropdownBlurred: []
}>()

const dropdownRef = ref<HTMLElement | null>(null)

// The three sections in priority order. The prioritized source comes first;
// the other two follow in their natural default order.
const sectionOrder = computed<SearchSourcePriority[]>(() => {
  const priority = props.sourcePriority
  const all: SearchSourcePriority[] = ['catalog', 'hebrewbooks', 'files']
  return [priority, ...all.filter((source) => source !== priority)]
})

const allItems = computed(() => {
  const items: Array<
    | { kind: 'catalog'; bookId: number; title: string }
    | { kind: 'catalogToc'; item: TocFsItem }
    | { kind: 'hebrewBooks'; book: HebrewBook }
    | { kind: 'file'; fullPath: string; fileName: string }
  > = []
  for (const source of sectionOrder.value) {
    if (source === 'catalog') {
      for (const item of props.catalogResults) {
        items.push({ kind: 'catalog', bookId: item.book.id, title: item.book.title })
      }
      for (const item of props.catalogTocResults) {
        items.push({ kind: 'catalogToc', item })
      }
    } else if (source === 'hebrewbooks') {
      for (const item of props.hebrewBooksResults) {
        items.push({ kind: 'hebrewBooks', book: item.book })
      }
    } else {
      for (const item of props.fileResults) {
        items.push({ kind: 'file', fullPath: item.fullPath, fileName: item.fileName })
      }
    }
  }
  return items
})

function activateItem(index: number) {
  const item = allItems.value[index]
  if (!item) return
  if (item.kind === 'catalog') emit('selectCatalogBook', item.bookId, item.title)
  else if (item.kind === 'catalogToc') emit('selectCatalogToc', item.item)
  else if (item.kind === 'hebrewBooks') emit('selectHebrewBook', item.book)
  else emit('selectFile', item.fullPath, item.fileName)
}

const { focusedIndex, containerFocused } = useListKeys(
  dropdownRef,
  () => allItems.value.length,
  activateItem,
)

defineExpose({
  focus: () => dropdownRef.value?.focus(),
  element: dropdownRef,
})

function onDropdownFocus() {
  if (focusedIndex.value < 0) focusedIndex.value = 0
  emit('dropdownFocused')
}

function onDropdownBlur(e: FocusEvent) {
  if (e.relatedTarget !== null && !dropdownRef.value?.contains(e.relatedTarget as Node)) {
    emit('dropdownBlurred')
  }
}

type FileIconInfo = { component: unknown; color: string }

function getFileIcon(fileName: string): FileIconInfo {
  const extension = fileName.toLowerCase().split('.').pop()
  switch (extension) {
    case 'pdf':
      return { component: IconDocumentPdf20Filled, color: '#F40F02' }
    case 'html':
    case 'htm':
    case 'mht':
    case 'mhtml':
      return { component: IconDocumentGlobe20Filled, color: '#0097fb' }
    case 'txt':
      return { component: IconDocumentText20Filled, color: '#9e9e9e' }
    default:
      return { component: IconDocument20Filled, color: '#3478f6' }
  }
}
</script>

<template>
  <Teleport to="body">
    <div
      ref="dropdownRef"
      class="home-search-dropdown"
      tabindex="0"
      :style="{
        top: anchorTop + 'px',
        left: anchorLeft + 'px',
        right: anchorRight + 'px',
        maxHeight: maxHeight + 'px',
      }"
      @click.stop
      @focus="onDropdownFocus"
      @blur="onDropdownBlur"
    >
      <template v-for="source in sectionOrder" :key="source">

        <!-- ── Book catalog section ── -->
        <template v-if="source === 'catalog' && (catalogResults.length > 0 || catalogTocResults.length > 0 || isLoadingCatalogToc)">
          <div class="home-search-dropdown__section-header">
            ספרים
            <IconArrowSync20Regular v-if="isLoadingCatalogToc" class="home-search-dropdown__spinner" />
          </div>
          <div
            v-for="item in catalogResults"
            :key="item.book.id"
            role="option"
            class="home-search-dropdown__item"
            :class="{ 'is-focused': containerFocused && focusedIndex === allItems.findIndex((i) => i.kind === 'catalog' && i.bookId === item.book.id) }"
            data-nav-item
            @click="emit('selectCatalogBook', item.book.id, item.book.title)"
          >
            <IconLibrary20Filled class="home-search-dropdown__item-icon home-search-dropdown__item-icon--catalog" />
            <span class="home-search-dropdown__item-title">{{ item.book.title }}</span>
            <span v-if="item.book.parentPath" class="home-search-dropdown__item-path">
              {{ item.book.parentPath }}
            </span>
          </div>
          <!-- TOC heuristics fallback results (only present when no title matched) -->
          <div
            v-for="item in catalogTocResults"
            :key="item.uid"
            role="option"
            class="home-search-dropdown__item"
            :class="{ 'is-focused': containerFocused && focusedIndex === allItems.findIndex((i) => i.kind === 'catalogToc' && i.item.uid === item.uid) }"
            data-nav-item
            @click="emit('selectCatalogToc', item)"
          >
            <IconLibrary20Filled class="home-search-dropdown__item-icon home-search-dropdown__item-icon--catalog" />
            <span class="home-search-dropdown__item-title">{{ item.book.title }} {{ item.tocPath }}</span>
            <span v-if="item.book.parentPath" class="home-search-dropdown__item-path">
              {{ item.book.parentPath }}
            </span>
          </div>
        </template>

        <!-- ── HebrewBooks section ── -->
        <template v-if="source === 'hebrewbooks' && (hebrewBooksResults.length > 0 || isLoadingHebrewBooks)">
          <div class="home-search-dropdown__section-header">
            היברו-בוקס
            <IconArrowSync20Regular v-if="isLoadingHebrewBooks" class="home-search-dropdown__spinner" />
          </div>
          <div
            v-for="item in hebrewBooksResults"
            :key="item.book.id"
            role="option"
            class="home-search-dropdown__item"
            :class="{ 'is-focused': containerFocused && focusedIndex === allItems.findIndex((i) => i.kind === 'hebrewBooks' && i.book.id === item.book.id) }"
            data-nav-item
            @click="emit('selectHebrewBook', item.book)"
          >
            <IconBookOpen20Filled class="home-search-dropdown__item-icon home-search-dropdown__item-icon--hebrewbooks" />
            <span class="home-search-dropdown__item-title">{{ item.book.title }}</span>
            <span v-if="item.book.author" class="home-search-dropdown__item-path">
              {{ item.book.author }}
            </span>
          </div>
        </template>

        <!-- ── File search section ── -->
        <template v-if="source === 'files' && (fileResults.length > 0 || isLoadingFiles)">
          <div class="home-search-dropdown__section-header">
            קבצים
            <IconArrowSync20Regular v-if="isLoadingFiles" class="home-search-dropdown__spinner" />
          </div>
          <div
            v-for="item in fileResults"
            :key="item.fullPath"
            role="option"
            class="home-search-dropdown__item"
            :class="{ 'is-focused': containerFocused && focusedIndex === allItems.findIndex((i) => i.kind === 'file' && i.fullPath === item.fullPath) }"
            data-nav-item
            @click="emit('selectFile', item.fullPath, item.fileName)"
          >
            <component
              :is="getFileIcon(item.fileName).component"
              class="home-search-dropdown__item-icon"
              :style="{ color: getFileIcon(item.fileName).color }"
            />
            <span class="home-search-dropdown__item-title">{{ item.fileName }}</span>
            <span class="home-search-dropdown__item-path">{{ item.fullPath }}</span>
          </div>
        </template>

      </template>
    </div>
  </Teleport>
</template>

<style scoped>
.home-search-dropdown {
  position: fixed;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  box-shadow: 0 4px 20px rgba(0, 0, 0, 0.25), 0 1px 4px rgba(0, 0, 0, 0.12);
  overflow-y: auto;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
  z-index: 1000;
  outline: none;
}

.home-search-dropdown__section-header {
  display: flex;
  align-items: center;
  gap: 6px;
  height: 28px;
  padding: 0 12px;
  font-size: 11px;
  font-weight: 600;
  color: var(--text-secondary);
  background: color-mix(in srgb, var(--text-primary) 4%, transparent);
  border-bottom: 1px solid var(--border-color);
}

.home-search-dropdown__spinner {
  animation: spin 1s linear infinite;
  opacity: 0.6;
}

@keyframes spin {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
}

.home-search-dropdown__item {
  display: flex;
  align-items: center;
  gap: 8px;
  width: 100%;
  height: 36px;
  padding: 0 12px;
  text-align: right;
  background: transparent;
  border-bottom: 1px solid color-mix(in srgb, var(--border-color) 50%, transparent);
  cursor: pointer;
  overflow: hidden;
  box-sizing: border-box;
}

.home-search-dropdown__item:last-child {
  border-bottom: none;
}

.home-search-dropdown__item:hover,
.home-search-dropdown__item.is-focused {
  background: color-mix(in srgb, var(--text-primary) 6%, transparent);
}

.home-search-dropdown__item:active {
  background: color-mix(in srgb, var(--text-primary) 10%, transparent);
}

.home-search-dropdown__item-icon {
  flex-shrink: 0;
}

.home-search-dropdown__item-icon--catalog {
  color: #b5451b;
}

.home-search-dropdown__item-icon--hebrewbooks {
  color: #d94f1e;
}

.home-search-dropdown__item-title {
  flex: 1;
  min-width: 0;
  font-size: 13px;
  color: var(--text-primary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.home-search-dropdown__item-path {
  flex-shrink: 0;
  max-width: 40%;
  font-size: 11px;
  color: var(--text-secondary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
</style>
