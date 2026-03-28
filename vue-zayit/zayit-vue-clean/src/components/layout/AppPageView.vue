<script setup lang="ts">
import { computed } from 'vue'
import HomePage from '@/components/home/HomePage.vue'
import BooksPage from '@/components/books-fs/BooksFsPage.vue'
import BookViewPage from '@/components/book-view/BookViewPage.vue'
import PdfViewPage from '@/components/pdf/PdfViewPage.vue'
import SettingsPage from '@/components/settings/SettingsPage.vue'
import HebrewBooksPage from '@/components/hebrew-books/HebrewBooksPage.vue'
import WorkspaceManagerPage from '@/components/workspace/WorkspaceManagerPage.vue'
import SearchPage from '@/components/search-db/SearchPage.vue'
import { useTabStore } from '@/stores/tabStore'

const tabStore = useTabStore()
const route = computed(() => tabStore.activeTab.route)

const pages: Record<string, unknown> = {
  '/': HomePage,
  '/books': BooksPage,
  '/book-view': BookViewPage,
  '/pdf-view': PdfViewPage,
  '/settings': SettingsPage,
  '/hebrewbooks': HebrewBooksPage,
  '/workspaces': WorkspaceManagerPage,
  '/search': SearchPage,
}
</script>

<template>
  <component
    :is="pages[route]"
    :key="route === '/book-view' ? tabStore.activeTabId : undefined"
  />
</template>
