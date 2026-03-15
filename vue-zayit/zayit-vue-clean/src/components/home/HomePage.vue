<script setup lang="ts">
import HomeTile from './HomePageTile.vue'
import { IconLibrary24Regular, IconSearch24Regular, IconFolderOpen24Regular, IconSettings24Regular } from '@iconify-prerendered/vue-fluent'
import { usePdfStore } from '@/stores/pdfStore'
import { useTabStore } from '@/stores/tabStore'

const pdfStore = usePdfStore()
const tabStore = useTabStore()

const tiles = [
  { label: 'ספרים', icon: IconLibrary24Regular, color: '#e8622a' },
  { label: 'חיפוש', icon: IconSearch24Regular, color: '#3478f6' },
  { label: 'פתח קובץ', icon: IconFolderOpen24Regular, color: '#f0a500' },
  { label: 'הגדרות', icon: IconSettings24Regular, color: '#6c757d' },
]

const ROUTES: Record<string, string> = { 'ספרים': '/books', 'הגדרות': '/settings' }

function onTap(label: string) {
  const route = ROUTES[label]
  if (route) {
    const existing = tabStore.tabs.find(t => t.route === route)
    if (existing) tabStore.switchTab(existing.id)
    else tabStore.openTab({ title: label, route: route as any })
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
