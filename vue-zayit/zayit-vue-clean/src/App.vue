<script setup lang="ts">
import { computed } from 'vue'
import AppTitleBar from '@/components/layout/AppTitleBar.vue'
import HomePage from '@/components/home/HomePage.vue'
import BooksPage from '@/components/books-fs/BooksFsPage.vue'
import BookViewPage from '@/components/book-view/BookViewPage.vue'
import PdfViewPage from '@/components/pdf/PdfViewPage.vue'
import SettingsPage from '@/components/settings/SettingsPage.vue'
import { useTabStore } from '@/stores/tabStore'

const route = computed(() => useTabStore().activeTab.route)
</script>

<template>
  <div class="app-layout">
    <AppTitleBar />
    <main class="app-content">
      <HomePage v-if="route === '/'" />
      <BooksPage v-else-if="route === '/books'" />
      <BookViewPage v-else-if="route === '/book-view'" />
      <PdfViewPage v-else-if="route === '/pdf-view'" />
      <SettingsPage v-else-if="route === '/settings'" />
    </main>
  </div>
</template>

<style scoped>
.app-layout { display: flex; flex-direction: column; height: 100%; }
.app-content { flex: 1; overflow-y: auto; }
</style>
