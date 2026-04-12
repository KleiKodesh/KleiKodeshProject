<script setup lang="ts">
import { computed, defineAsyncComponent } from 'vue'
import { useTabStore } from '@/stores/tabStore'

const tabStore = useTabStore()
const route = computed(() => tabStore.activeTab.route)

const pages: Record<string, unknown> = {
  '/': defineAsyncComponent(() => import('@/components/home/HomePage.vue')),
  '/books': defineAsyncComponent(() => import('@/components/books-fs/BooksFsPage.vue')),
  '/book-view': defineAsyncComponent(() => import('@/components/book-view/BookViewPage.vue')),
  '/pdf-view': defineAsyncComponent(() => import('@/components/pdf/PdfViewPage.vue')),
  '/settings': defineAsyncComponent(() => import('@/components/settings/SettingsPage.vue')),
  '/hebrewbooks': defineAsyncComponent(
    () => import('@/components/hebrew-books/HebrewBooksPage.vue'),
  ),
  '/workspaces': defineAsyncComponent(
    () => import('@/components/workspace/WorkspaceManagerPage.vue'),
  ),
  '/search': defineAsyncComponent(() => import('@/components/search-db/SearchPage.vue')),
  '/hebrew-calendar': defineAsyncComponent(
    () => import('@/components/hebrew-calendar/HebrewCalendarPage.vue'),
  ),
  '/dictionary': defineAsyncComponent(() => import('@/components/dictionary/DictionaryPage.vue')),
}
</script>

<template>
  <component
    :is="pages[route]"
    :key="route === '/book-view' || route === '/search' ? tabStore.activeTabId : undefined"
  />
</template>
