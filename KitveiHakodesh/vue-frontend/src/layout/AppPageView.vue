<script setup lang="ts">
import { computed, defineAsyncComponent } from 'vue'
import { useTabStore } from '@/stores/tabStore'

const tabStore = useTabStore()
const route = computed(() => tabStore.activeTab.route)

const pages: Record<string, unknown> = {
  '/': defineAsyncComponent(() => import('@/features/home/HomePage.vue')),
  '/books': defineAsyncComponent(() => import('@/features/book-catalog/BookCatalogPage.vue')),
  '/book-view': defineAsyncComponent(() => import('@/features/book-view/BookViewPage.vue')),
  '/pdf-view': defineAsyncComponent(() => import('@/features/pdf-viewer/PdfViewPage.vue')),
  '/html-view': defineAsyncComponent(() => import('@/features/html-view/HtmlViewPage.vue')),
  '/settings': defineAsyncComponent(() => import('@/features/settings/SettingsPage.vue')),
  '/hebrewbooks': defineAsyncComponent(
    () => import('@/features/hebrewbooks/HebrewBooksPage.vue'),
  ),
  '/workspaces': defineAsyncComponent(
    () => import('@/features/workspace/WorkspaceManagerPage.vue'),
  ),
  '/search': defineAsyncComponent(() => import('@/features/full-text-search/FullTextSearchPage.vue')),
  '/hebrew-calendar': defineAsyncComponent(
    () => import('@/features/hebrew-calendar/HebrewCalendarPage.vue'),
  ),
  '/dictionary': defineAsyncComponent(() => import('@/features/dictionary/DictionaryPage.vue')),
  '/midot': defineAsyncComponent(() => import('@/features/halachic-units/HalachicUnitsPage.vue')),
  '/file-search': defineAsyncComponent(() => import('@/features/file-search/FileSearchPage.vue')),
}
</script>

<template>
  <component
    :is="pages[route]"
    :key="route === '/book-view' || route === '/search' ? tabStore.activeTabId : undefined"
  />
</template>
