<script setup lang="ts">
import { ref, onMounted, nextTick } from 'vue'
import { IconSearch20Regular, IconTextBulletList20Regular, IconGrid20Regular } from '@iconify-prerendered/vue-fluent'
import { useBooksFs } from './useBooksFs'
import BooksBreadcrumb from './BooksBreadcrumb.vue'
import BooksTreeView from './BooksTreeView.vue'
import BooksSearchResults from './BooksSearchResults.vue'
import LoadingAnimation from '@/components/common/LoadingAnimation.vue'
import type { BookRow } from './booksFsTree'
import { useTabStore } from '@/stores/tabStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import { persistGet, persistSet, PERSIST_KEYS } from '@/utils/persist'

const tabStore = useTabStore()
const bookViewStore = useBookViewStore()
const { loading, error, path, searchQuery, isSearching, treeItems, searchItems, load, enter, navigateTo } = useBooksFs()

const view = ref<'list' | 'tiles'>(persistGet(PERSIST_KEYS.BOOKS_VIEW, 'list') as 'list' | 'tiles')

function toggleView() {
  view.value = view.value === 'list' ? 'tiles' : 'list'
  persistSet(PERSIST_KEYS.BOOKS_VIEW, view.value)
}

const searchInputRef = ref<HTMLInputElement | null>(null)

onMounted(() => {
  load()
  nextTick(() => searchInputRef.value?.focus())
})

function onSelectBook(book: BookRow) {
  bookViewStore.setTabState(tabStore.activeTabId, { tocVisible: true })
  tabStore.updateActiveTab({ title: book.title, route: '/book-view', bookId: book.id })
}
</script>

<template>
  <div class="books-page">
    <div class="books-toolbar">
      <BooksBreadcrumb :path="path" @navigate="navigateTo" />
      <button class="view-toggle" :title="view === 'list' ? 'תצוגת אריחים' : 'תצוגת רשימה'" @click="toggleView">
        <IconGrid20Regular v-if="view === 'list'" />
        <IconTextBulletList20Regular v-else />
      </button>
    </div>

    <div class="books-content">
      <LoadingAnimation v-if="loading" />
      <div v-else-if="error" class="state error">{{ error }}</div>
      <template v-else>
        <BooksTreeView v-show="!isSearching" :items="treeItems" :view="view" @select-book="onSelectBook" @enter-folder="enter" />
        <BooksSearchResults v-show="isSearching" :items="searchItems" :view="view" @select-book="onSelectBook" />
      </template>
    </div>

    <div class="search-bar">
      <div class="search-inner">
        <IconSearch20Regular class="search-icon" />
        <input ref="searchInputRef" v-model="searchQuery" type="search" placeholder="חיפוש ספר..." class="search-input" />
      </div>
    </div>
  </div>
</template>

<style scoped>
.books-page { display: flex; flex-direction: column; height: 100%; background: var(--bg-primary); }
.books-content { flex: 1; overflow: hidden; position: relative; }

.books-toolbar {
  display: flex;
  align-items: center;
  border-bottom: 1px solid var(--border-color);
  background: var(--bg-secondary);
}

.view-toggle {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 32px;
  height: 32px;
  flex-shrink: 0;
  border: none;
  border-radius: 4px;
  background: transparent;
  color: var(--text-secondary);
  cursor: pointer;
  margin-inline-start: auto;
  margin-inline-end: 4px;
  transition: background 0.1s, color 0.1s;
}
.view-toggle:hover { background: var(--hover-bg); color: var(--text-primary); }
.view-toggle:active { background: var(--active-bg); }

.search-bar {
  padding: 6px 12px 8px;
  background: var(--bg-secondary);
  border-top: 1px solid var(--border-color);
}
.search-inner {
  display: flex;
  align-items: center;
  gap: 6px;
  background: color-mix(in srgb, var(--text-secondary) 12%, transparent);
  border-radius: 10px;
  padding: 6px 10px;
}
.search-icon { color: var(--text-secondary); flex-shrink: 0; }
.search-input {
  flex: 1;
  background: none;
  border: none;
  outline: none;
  font-size: 14px;
  color: var(--text-primary);
}
.search-input::placeholder { color: var(--text-secondary); }
.search-input::-webkit-search-cancel-button { filter: grayscale(1) opacity(0.4); }

.state.error { padding: 32px 16px; text-align: center; color: #ff3b30; font-size: 15px; }
</style>
