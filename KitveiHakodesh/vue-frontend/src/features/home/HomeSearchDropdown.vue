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
} from './useHomeSearch'

const props = defineProps<{
  catalogResults: CatalogSearchResult[]
  hebrewBooksResults: HebrewBooksSearchResult[]
  fileResults: FileSearchResult[]
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
  selectHebrewBook: [bookId: number, bookTitle: string]
  selectFile: [fullPath: string, fileName: string]
  dropdownFocused: []
  dropdownBlurred: []
}>()

const dropdownRef = ref<HTMLElement | null>(null)

const allItems = computed(() => [
  ...props.catalogResults.map((item) => ({
    kind: 'catalog' as const,
    bookId: item.book.id,
    title: item.book.title,
  })),
  ...props.hebrewBooksResults.map((item) => ({
    kind: 'hebrewBooks' as const,
    bookId: item.book.id,
    title: item.book.title,
  })),
  ...props.fileResults.map((item) => ({
    kind: 'file' as const,
    fullPath: item.fullPath,
    fileName: item.fileName,
  })),
])

function activateItem(index: number) {
  const item = allItems.value[index]
  if (!item) return
  if (item.kind === 'catalog') emit('selectCatalogBook', item.bookId, item.title)
  else if (item.kind === 'hebrewBooks') emit('selectHebrewBook', item.bookId, item.title)
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
      <!-- ── Book catalog section ── -->
      <template v-if="catalogResults.length > 0">
        <div class="home-search-dropdown__section-header">ספרים</div>
        <div
          v-for="(item, sectionIndex) in catalogResults"
          :key="item.book.id"
          role="option"
          class="home-search-dropdown__item"
          :class="{ 'is-focused': containerFocused && focusedIndex === sectionIndex }"
          data-nav-item
          @click="emit('selectCatalogBook', item.book.id, item.book.title)"
        >
          <IconLibrary20Filled class="home-search-dropdown__item-icon home-search-dropdown__item-icon--catalog" />
          <span class="home-search-dropdown__item-title">{{ item.book.title }}</span>
          <span v-if="item.book.parentPath" class="home-search-dropdown__item-path">
            {{ item.book.parentPath }}
          </span>
        </div>
      </template>

      <!-- ── HebrewBooks section ── -->
      <template v-if="hebrewBooksResults.length > 0 || isLoadingHebrewBooks">
        <div class="home-search-dropdown__section-header">
          היברו-בוקס
          <IconArrowSync20Regular v-if="isLoadingHebrewBooks" class="home-search-dropdown__spinner" />
        </div>
        <div
          v-for="(item, sectionIndex) in hebrewBooksResults"
          :key="item.book.id"
          role="option"
          class="home-search-dropdown__item"
          :class="{ 'is-focused': containerFocused && focusedIndex === catalogResults.length + sectionIndex }"
          data-nav-item
          @click="emit('selectHebrewBook', item.book.id, item.book.title)"
        >
          <IconBookOpen20Filled class="home-search-dropdown__item-icon home-search-dropdown__item-icon--hebrewbooks" />
          <span class="home-search-dropdown__item-title">{{ item.book.title }}</span>
          <span v-if="item.book.author" class="home-search-dropdown__item-path">
            {{ item.book.author }}
          </span>
        </div>
      </template>

      <!-- ── File search section ── -->
      <template v-if="fileResults.length > 0 || isLoadingFiles">
        <div class="home-search-dropdown__section-header">
          קבצים
          <IconArrowSync20Regular v-if="isLoadingFiles" class="home-search-dropdown__spinner" />
        </div>
        <div
          v-for="(item, sectionIndex) in fileResults"
          :key="item.fullPath"
          role="option"
          class="home-search-dropdown__item"
          :class="{ 'is-focused': containerFocused && focusedIndex === catalogResults.length + hebrewBooksResults.length + sectionIndex }"
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
