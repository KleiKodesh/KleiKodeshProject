<template>
  <component :is="currentPageComponent"
             class="height-fill"
             :key="`tab-${tabStore.activeTab?.id}-${tabStore.activeTab?.currentPage}`" />
</template>

<script setup lang="ts">
import { computed } from 'vue';
import { useTabStore } from '../stores/tabStore';
import HomePage from './pages/HomePage.vue';
import KezayitLandingPage from './pages/KezayitLandingPage.vue';
import BookViewPage from './pages/BookViewPage.vue';
import PdfViewPage from './pages/PdfViewPage.vue';
import HebrewBooksViewPage from './pages/HebrewBooksViewPage.vue';
import SearchPage from './pages/SearchPage.vue';
import SettingsPage from './pages/SettingsPage.vue';
import AboutPage from './pages/AboutPage.vue';
import HebrewbooksPage from './pages/HebrewbooksPage.vue';
import KezayitSearchPage from './pages/KezayitSearchPage.vue';
import type { PageType } from '../types/Tab';

const tabStore = useTabStore();

const pageComponents: Record<PageType, any> = {
  'homepage': HomePage,
  'kezayit-landing': KezayitLandingPage,
  'bookview': BookViewPage,
  'pdfview': PdfViewPage,
  'hebrewbooks-view': HebrewBooksViewPage,
  'search': SearchPage,
  'settings': SettingsPage,
  'about': AboutPage,
  'hebrewbooks': HebrewbooksPage,
  'kezayit-search': KezayitSearchPage
};

const currentPageComponent = computed(() => {
  const pageType = tabStore.activeTab?.currentPage || 'kezayit-landing';
  // Handle legacy 'landing' page type
  const normalizedPageType = (pageType as string) === 'landing' ? 'kezayit-landing' : pageType;
  return pageComponents[normalizedPageType as PageType] || pageComponents['kezayit-landing'];
});

</script>
