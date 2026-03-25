<script setup lang="ts">
import { ref, computed } from 'vue'
import HomeTile from './HomePageTile.vue'
import {
  IconLibrary24Filled, IconFolderOpen24Filled, IconBookOpen24Filled,
  IconApps24Filled, IconDatabase24Filled, IconArrowDownload24Filled,
} from '@iconify-prerendered/vue-fluent'
import { IconSettings24, IconSearchSparkle24 } from '@iconify-prerendered/vue-fluent-color'
import { usePdfStore } from '@/stores/pdfStore'
import { useTabStore } from '@/stores/tabStore'
import { useGridLayout } from '@/composables/useGridLayout'
import { isHosted, dbReady } from '@/host/db'
import { pickFile } from '@/host/bridge'

const pdfStore = usePdfStore()
const tabStore = useTabStore()

const baseTiles = [
  { label: 'ספרים',        icon: IconLibrary24Filled,    color: '#B5451B' },
  { label: 'חיפוש',        icon: IconSearchSparkle24 },
  { label: 'פתח קובץ',     icon: IconFolderOpen24Filled, color: '#f0a500' },
  { label: 'היברו-בוקס',   icon: IconBookOpen24Filled,   color: '#D94F1E' },
  { label: 'הגדרות',       icon: IconSettings24 },
  { label: 'סביבות עבודה', icon: IconApps24Filled,       color: '#6b7fc4' },
]

const noDbTiles = [
  { label: 'התקן זית',          icon: IconArrowDownload24Filled, color: '#B5451B' },
  { label: 'בחר מסד נתונים',    icon: IconDatabase24Filled,      color: '#3478f6' },
  { label: 'פתח קובץ',          icon: IconFolderOpen24Filled,    color: '#f0a500' },
  { label: 'היברו-בוקס',        icon: IconBookOpen24Filled,      color: '#D94F1E' },
  { label: 'הגדרות',            icon: IconSettings24 },
  { label: 'סביבות עבודה',      icon: IconApps24Filled,          color: '#6b7fc4' },
]

const tiles = computed(() => (isHosted && !dbReady.value) ? noDbTiles : baseTiles)

const SINGLETON_ROUTES: Record<string, string> = {
  'ספרים':         '/books',
  'הגדרות':        '/settings',
  'היברו-בוקס':    '/hebrewbooks',
  'סביבות עבודה':  '/workspaces',
}

const pageRef = ref<HTMLElement | null>(null)
const gridRef = ref<HTMLElement | null>(null)
const tileCount = computed(() => tiles.value.length)
const { cols } = useGridLayout(pageRef, tileCount)

async function onTap(label: string) {
  const route = SINGLETON_ROUTES[label]
  if (route) {
    tabStore.navigateToSingleton(route as any)
  } else if (label === 'פתח קובץ') {
    const tabId = tabStore.activeTabId
    const result = await pickFile()
    if (result) pdfStore.finishLocalFileConversion(tabId, result)
  } else if (label === 'התקן זית') {
    window.open('https://zayitapp.com/#/download', '_blank')
  } else if (label === 'בחר מסד נתונים') {
    window.__webviewPickDbPath?.()
  } else if (label === 'חיפוש') {
    tabStore.navigateToSingleton('/search')
  }
}
</script>

<template>
  <div ref="pageRef" class="home-page">
    <div ref="gridRef" class="home-grid" :style="{ gridTemplateColumns: `repeat(${cols}, 1fr)` }">
      <HomeTile v-for="t in tiles" :key="t.label" v-bind="t" @tap="onTap(t.label)" />
    </div>
  </div>
</template>

<style scoped>
.home-page {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  height: 100%;
}
.home-grid {
  display: grid;
  gap: 16px;
}
</style>
