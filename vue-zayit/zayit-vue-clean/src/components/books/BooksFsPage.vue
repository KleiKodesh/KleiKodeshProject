<script setup lang="ts">
import { onMounted } from 'vue'
import { IconSearch20Regular } from '@iconify-prerendered/vue-fluent'
import { useBooksFs } from './useBooksFs'
import BooksBreadcrumb from './BooksBreadcrumb.vue'
import BooksBookList from './BooksBookList.vue'
import type { BookRow } from './booksFsTree'
import { useTabStore } from '@/stores/tabStore'

const tabStore = useTabStore()
const { loading, error, path, searchQuery, listItems, load, enter, navigateTo } = useBooksFs()

onMounted(load)

function onSelectBook(book: BookRow) {
  tabStore.openTab({ title: book.title, route: '/book-view' })
}
</script>

<template>
  <div class="books-page">
    <BooksBreadcrumb :path="path" @navigate="navigateTo" />
    <div class="books-content">
      <div v-if="loading" class="state">טוען...</div>
      <div v-else-if="error" class="state error">{{ error }}</div>
      <BooksBookList v-else :items="listItems" @select-book="onSelectBook" @enter-folder="enter" />
    </div>
    <div class="search-bar">
      <IconSearch20Regular class="search-icon" />
      <input v-model="searchQuery" type="search" placeholder="חיפוש ספר..." class="search-input" />
    </div>
  </div>
</template>

<style scoped>
.books-page { display: flex; flex-direction: column; height: 100%; }
.books-content { flex: 1; overflow: hidden; }

.search-bar {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 16px;
  border-top: 1px solid var(--border-color);
  background: var(--bg-secondary);
}
.search-icon { color: var(--text-secondary); flex-shrink: 0; }
.search-input {
  flex: 1;
  background: none;
  border: none;
  outline: none;
  font-size: 14px;
  color: var(--text-primary);
  font-family: var(--header-font);
}
.search-input::placeholder { color: var(--text-secondary); }

.state { padding: 32px 16px; text-align: center; color: var(--text-secondary); font-size: 14px; }
.state.error { color: #d32f2f; }
</style>
