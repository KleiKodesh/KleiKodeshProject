<script setup lang="ts">
import { computed, defineAsyncComponent } from 'vue'
import { useTabStore } from '@/stores/tabStore'

const tabStore = useTabStore()
const route = computed(() => tabStore.activeTab.route)

const pages: Record<string, unknown> = {
  '/': defineAsyncComponent(() => import('@/components/home/HomePage.vue')),
  '/book-view': defineAsyncComponent(() => import('@/components/book-view/BookViewPage.vue')),
  '/search': defineAsyncComponent(() => import('@/components/search-db/SearchPage.vue')),
  '/pdf-view': defineAsyncComponent(() => import('@/components/pdf/PdfViewPage.vue')),
}

const activePageKey = computed(() => {
  if (route.value === '/book-view' || route.value === '/search' || route.value === '/pdf-view') {
    return tabStore.activeTabId
  }
  return route.value
})
</script>

<template>
  <div class="desktop-main-panel">
    <component
      v-if="pages[route]"
      :is="pages[route]"
      :key="activePageKey"
    />
    <div v-else class="desktop-main-panel__empty">
      בחר דף ראשי להצגה כאן
    </div>
  </div>
</template>

<style scoped>
.desktop-main-panel {
  display: flex;
  flex-direction: column;
  min-height: 0;
  flex: 1;
  overflow: hidden;
  background: var(--bg-primary);
}

.desktop-main-panel__empty {
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 24px;
  color: var(--text-secondary);
  font-size: 0.95rem;
  text-align: center;
}
</style>
