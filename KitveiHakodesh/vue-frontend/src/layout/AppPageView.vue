<script setup lang="ts">
import { computed, defineAsyncComponent } from 'vue'
import { useTabStore } from '@/stores/tabStore'

const props = defineProps<{ paneId?: 1 | 2 }>()
const tabStore = useTabStore()

const activeTab = computed(() =>
  props.paneId === 2 ? tabStore.activeTabForPane(2) : tabStore.activeTab,
)
const activeTabId = computed(() =>
  props.paneId === 2 ? tabStore.pane2ActiveTabId : tabStore.activeTabId,
)
const route = computed(() => activeTab.value.route)

// Remount key for pages that read their target once at setup (they don't watch
// for in-place changes). Book view reads bookId once, so switching to a
// DIFFERENT book on the SAME tab (e.g. picking a result from the address bar
// while already in book view) must force a remount — include bookId in the key.
// navNonce covers the same-book case: jumping to a different position within the
// book already open (a recently-opened row, a catalog TOC hit) leaves bookId
// unchanged, so the caller bumps the nonce to force the remount.
// Other keyed routes just key by tab id.
const pageKey = computed(() => {
  const r = route.value
  if (r === '/book-view') {
    const t = activeTab.value
    return `${activeTabId.value}:${t.bookId ?? ''}:${t.navNonce ?? ''}`
  }
  if (r === '/search' || r === '/txt-view') return activeTabId.value
  return undefined
})

const pages: Record<string, unknown> = {
  '/': defineAsyncComponent(() => import('@/features/home/HomePage.vue')),
  '/books': defineAsyncComponent(() => import('@/features/book-catalog/BookCatalogPage.vue')),
  '/book-view': defineAsyncComponent(() => import('@/features/book-view/BookViewPage.vue')),
  '/pdf-view': defineAsyncComponent(() => import('@/features/pdf-viewer/PdfViewPage.vue')),
  '/html-view': defineAsyncComponent(() => import('@/features/html-view/HtmlViewPage.vue')),
  '/txt-view': defineAsyncComponent(() => import('@/features/txt-view/TxtViewPage.vue')),
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
  '/file-search': defineAsyncComponent(() => import('@/features/local-file-search/LocalFileSearchPage.vue')),
}
</script>

<template>
  <component
    :is="pages[route]"
    :key="pageKey"
  />
</template>
