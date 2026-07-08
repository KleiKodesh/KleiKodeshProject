<script setup lang="ts">
import {
  IconBookOpen20Regular,
  IconDocument20Regular,
  IconFolder20Regular,
  IconArrowSync20Regular,
} from '@iconify-prerendered/vue-fluent'
import type {
  CatalogSearchResult,
  HebrewBooksSearchResult,
  FileSearchResult,
} from './useHomeSearch'

defineProps<{
  catalogResults: CatalogSearchResult[]
  hebrewBooksResults: HebrewBooksSearchResult[]
  fileResults: FileSearchResult[]
  isLoadingHebrewBooks: boolean
  isLoadingFiles: boolean
}>()

const emit = defineEmits<{
  selectCatalogBook: [bookId: number, bookTitle: string]
  selectHebrewBook: [bookId: number, bookTitle: string]
  selectFile: [fullPath: string, fileName: string]
}>()
</script>

<template>
  <div class="home-search-dropdown">

    <!-- ── Book catalog section ── -->
    <template v-if="catalogResults.length > 0">
      <div class="home-search-dropdown__section-header">ספרים</div>
      <button
        v-for="item in catalogResults"
        :key="item.book.id"
        class="home-search-dropdown__item"
        @click="emit('selectCatalogBook', item.book.id, item.book.title)"
      >
        <IconBookOpen20Regular class="home-search-dropdown__item-icon home-search-dropdown__item-icon--catalog" />
        <span class="home-search-dropdown__item-title">{{ item.book.title }}</span>
        <span v-if="item.book.parentPath" class="home-search-dropdown__item-path">
          {{ item.book.parentPath }}
        </span>
      </button>
    </template>

    <!-- ── HebrewBooks section ── -->
    <template v-if="hebrewBooksResults.length > 0 || isLoadingHebrewBooks">
      <div class="home-search-dropdown__section-header">
        היברו-בוקס
        <IconArrowSync20Regular v-if="isLoadingHebrewBooks" class="home-search-dropdown__spinner" />
      </div>
      <button
        v-for="item in hebrewBooksResults"
        :key="item.book.id"
        class="home-search-dropdown__item"
        @click="emit('selectHebrewBook', item.book.id, item.book.title)"
      >
        <IconDocument20Regular class="home-search-dropdown__item-icon home-search-dropdown__item-icon--hebrewbooks" />
        <span class="home-search-dropdown__item-title">{{ item.book.title }}</span>
        <span v-if="item.book.author" class="home-search-dropdown__item-path">
          {{ item.book.author }}
        </span>
      </button>
    </template>

    <!-- ── File search section ── -->
    <template v-if="fileResults.length > 0 || isLoadingFiles">
      <div class="home-search-dropdown__section-header">
        קבצים
        <IconArrowSync20Regular v-if="isLoadingFiles" class="home-search-dropdown__spinner" />
      </div>
      <button
        v-for="item in fileResults"
        :key="item.fullPath"
        class="home-search-dropdown__item"
        @click="emit('selectFile', item.fullPath, item.fileName)"
      >
        <IconFolder20Regular class="home-search-dropdown__item-icon home-search-dropdown__item-icon--files" />
        <span class="home-search-dropdown__item-title">{{ item.fileName }}</span>
        <span class="home-search-dropdown__item-path">{{ item.fullPath }}</span>
      </button>
    </template>

  </div>
</template>

<style scoped>
.home-search-dropdown {
  position: absolute;
  top: calc(100% + 6px);
  right: 0;
  left: 0;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  box-shadow: 0 4px 20px rgba(0, 0, 0, 0.25), 0 1px 4px rgba(0, 0, 0, 0.12);
  overflow: hidden;
  max-height: 420px;
  overflow-y: auto;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
  z-index: 100;
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
  border: none;
  border-bottom: 1px solid color-mix(in srgb, var(--border-color) 50%, transparent);
  cursor: pointer;
  overflow: hidden;
}

.home-search-dropdown__item:last-child {
  border-bottom: none;
}

.home-search-dropdown__item:hover {
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

.home-search-dropdown__item-icon--files {
  color: #f0a500;
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
