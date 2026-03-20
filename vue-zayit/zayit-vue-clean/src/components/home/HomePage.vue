<script setup lang="ts">
import HomeTile from './HomePageTile.vue'
import { IconLibrary24Filled, IconFolderOpen24Filled, IconBookOpen24Filled } from '@iconify-prerendered/vue-fluent'
import { IconSettings24, IconSearchSparkle24 } from '@iconify-prerendered/vue-fluent-color'
import { usePdfStore } from '@/stores/pdfStore'
import { useTabStore } from '@/stores/tabStore'

const pdfStore = usePdfStore()
const tabStore = useTabStore()

const tiles = [
  { label: 'ספרים', icon: IconLibrary24Filled, color: '#C1440E' },
  { label: 'חיפוש', icon: IconSearchSparkle24 },
  { label: 'פתח קובץ', icon: IconFolderOpen24Filled, color: '#f0a500' },
  { label: 'היברו-בוקס', icon: IconBookOpen24Filled, color: '#f97316' },
  { label: 'הגדרות', icon: IconSettings24 },
]

const ROUTES: Record<string, string> = { 'ספרים': '/books', 'הגדרות': '/settings', 'היברו-בוקס': '/hebrewbooks' }

function onTap(label: string) {
  const route = ROUTES[label]
  if (route) {
    tabStore.updateActiveTab({ title: label, route: route as any })
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
  <div class="home-page">
    <div class="home-grid">
      <HomeTile v-for="t in tiles" :key="t.label" v-bind="t" @tap="onTap(t.label)" />
    </div>
  </div>
</template>

<style scoped>
.home-page { display: flex; align-items: center; justify-content: center; height: 100%; }
.home-grid { display: flex; flex-wrap: wrap; justify-content: center; gap: 16px; }
</style>
