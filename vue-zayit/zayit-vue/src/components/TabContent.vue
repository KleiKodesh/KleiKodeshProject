<template>
  <component :is="currentPageComponent"
             class="height-fill"
             :key="`tab-${tabStore.activeTab?.id}-${tabStore.activeTab?.currentPage}`" />
</template>

<script setup lang="ts">
import { computed } from 'vue';
import { useTabStore } from '../stores/tabStore';
import HomePage from './pages/HomePage.vue';
import KezayitOpenFilePage from './pages/KezayitOpenFilePage.vue';
import BookViewPage from './pages/BookViewPage.vue';
import PdfViewPage from './pages/PdfViewPage.vue';
import HebrewBooksViewPage from './pages/HebrewBooksViewPage.vue';
import SearchPage from './pages/SearchPage.vue';
import SettingsPage from './pages/SettingsPage.vue';
import HebrewbooksPage from './pages/HebrewbooksPage.vue';
import WorkspacesPage from './pages/WorkspacesPage.vue';
import type { PageType } from '../types/Tab';

const tabStore = useTabStore();

const pageComponents: Record<PageType, any> = {
  'homepage': HomePage,
  'openfile': KezayitOpenFilePage,
  'bookview': BookViewPage,
  'pdfview': PdfViewPage,
  'hebrewbooks-view': HebrewBooksViewPage,
  'search': SearchPage,
  'settings': SettingsPage,
  'hebrewbooks': HebrewbooksPage,
  'kezayit-search': SearchPage, // Use new Bloom search page
  'workspaces': WorkspacesPage
};

const currentPageComponent = computed(() => {
  const pageType = tabStore.activeTab?.currentPage || 'openfile';
  return pageComponents[pageType] || pageComponents['openfile'];
});

</script>
