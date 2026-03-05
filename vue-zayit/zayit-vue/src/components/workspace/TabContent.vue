<template>
  <component :is="currentPageComponent"
             class="height-fill"
             :key="`tab-${activeTab?.id}-${activeTab?.currentPage}`" />
</template>

<script setup lang="ts">
import { computed } from 'vue';
import { useWorkspace } from '@/components/workspace/useWorkspace';
import HomePage from '@/components/home/HomePage.vue';
import ZayitOpenFilePage from '@/components/zayitdb-fs/ZayitOpenFilePage.vue';
import BookViewPage from '@/components/book/BookViewPage.vue';
import PdfViewPage from '@/components/pdf/PdfViewPage.vue';
import HebrewBookViewPage from '@/components/hebrew-books/HebrewBookViewPage.vue';
import ZayitSearchPage from '@/components/zayitdb-search/ZayitSearchPage.vue';
import SettingsPage from '@/components/settings/SettingsPage.vue';
import HebrewBooksPage from '@/components/hebrew-books/HebrewBooksPage.vue';
import WorkspacesPage from '@/components/workspace/WorkspacesPage.vue';
import type { PageType } from '@/data/types/Tab';

const { activeTab } = useWorkspace();

const pageComponents: Record<PageType, any> = {
  'homepage': HomePage,
  'openfile': ZayitOpenFilePage,
  'bookview': BookViewPage,
  'pdfview': PdfViewPage,
  'hebrewbooks-view': HebrewBookViewPage,
  'search': ZayitSearchPage,
  'settings': SettingsPage,
  'hebrewbooks': HebrewBooksPage,
  'kezayit-search': ZayitSearchPage, // Use new Bloom search page
  'workspaces': WorkspacesPage
};

const currentPageComponent = computed(() => {
  const pageType = activeTab.value?.currentPage || 'openfile';
  return pageComponents[pageType] || pageComponents['openfile'];
});

</script>
