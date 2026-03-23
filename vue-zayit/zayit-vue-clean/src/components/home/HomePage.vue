<script setup lang="ts">
import { ref, computed } from 'vue'
import HomeTile from './HomePageTile.vue'
import { IconLibrary24Filled, IconFolderOpen24Filled, IconBookOpen24Filled, IconApps24Filled } from '@iconify-prerendered/vue-fluent'
import { IconSettings24, IconSearchSparkle24 } from '@iconify-prerendered/vue-fluent-color'
import { usePdfStore } from '@/stores/pdfStore'
import { useTabStore } from '@/stores/tabStore'
import { useGridLayout } from '@/composables/useGridLayout'

const pdfStore = usePdfStore()
const tabStore = useTabStore()

const tiles = [
  { label: 'ספרים', icon: IconLibrary24Filled, color: '#B5451B' },
  { label: 'חיפוש', icon: IconSearchSparkle24 },
  { label: 'פתח קובץ', icon: IconFolderOpen24Filled, color: '#f0a500' },
  { label: 'היברו-בוקס', icon: IconBookOpen24Filled, color: '#D94F1E' },
  { label: 'הגדרות', icon: IconSettings24 },
  { label: 'סביבות עבודה', icon: IconApps24Filled, color: '#6b7fc4' },
]

const SINGLETON_ROUTES: Record<string, string> = {
  'ספרים': '/books',
  'הגדרות': '/settings',
  'היברו-בוקס': '/hebrewbooks',
  'סביבות עבודה': '/workspaces',
}

const pageRef = ref<HTMLElement | null>(null)
const gridRef = ref<HTMLElement | null>(null)
const tileCount = computed(() => tiles.length)
const { cols } = useGridLayout(pageRef, tileCount)

function onTap(label: string) {
  const route = SINGLETON_ROUTES[label]
  if (route) {
    tabStore.navigateToSingleton(route as any)
  } else if (label === 'פתח קובץ') {
    const input = Object.assign(document.createElement('input'), { type: 'file', accept: '.pdf,.doc,.docx,.rtf,.txt,.odt,.htm,.html,.xml' })
    input.onchange = () => {
      const file = input.files?.[0]
      if (file?.type === 'application/pdf' || file?.name.endsWith('.pdf'))
        pdfStore.openPdf(URL.createObjectURL(file), file.name)
    }
    input.click()
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
