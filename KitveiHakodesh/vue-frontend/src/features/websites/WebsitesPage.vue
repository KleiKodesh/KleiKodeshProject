<script setup lang="ts">
import { computed, h, onMounted } from 'vue'
import HomeSuggestionCard from '@/features/home/HomeSuggestionCard.vue'
import type { HomeSuggestionCard as HomeSuggestionCardData } from '@/features/home/homeSuggestionCard'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { useHomeSearchNavigation } from '@/features/home/useHomeSearchNavigation'
import WebsiteFavicon from './WebsiteFavicon.vue'
import OtzariaAddinIcon from './OtzariaAddinIcon.vue'
import { loadOtzariaAddins, otzariaAddinsRef, type OtzariaAddin } from './otzariaAddinsList'
import { displayHost, loadWebsites, sortedWebsites, websiteName, websitesRef, type Website } from './websitesList'

/** The Websites & add-ins destination: every whitelisted Torah site and every installed Otzaria addin as a card. A click opens it in a tab. */
const paneNavigation = usePaneNavigation()

const fileNavigation = useHomeSearchNavigation(() => {})

onMounted(() => {
  void loadWebsites()
  void loadOtzariaAddins()
})

function openAddin(addin: OtzariaAddin, openInNewTab: boolean): void {
  void fileNavigation.onSelectFile({ source: 'files', fileName: addin.fileName, fullPath: addin.path, addinName: addin.addinName }, openInNewTab)
}

function openSite(site: Website, openInNewTab: boolean): void {
  paneNavigation.openOrUpdateActiveTab({ route: '/web-view', title: websiteName(site), webUrl: site.url }, openInNewTab)
}

const cards = computed<HomeSuggestionCardData[]>(() =>
  sortedWebsites(websitesRef.value).map((site) => ({
    key: `site:${site.id}`,
    title: websiteName(site),
    path: displayHost(site.url),
    icon: () => h(WebsiteFavicon, { url: site.url }),
    open: (openInNewTab) => openSite(site, openInNewTab),
  })),
)

const addinCards = computed<HomeSuggestionCardData[]>(() =>
  [...otzariaAddinsRef.value]
    .sort((a, b) => a.name.localeCompare(b.name, 'he'))
    .map((addin) => ({
      key: `addin:${addin.path}`,
      title: addin.name,
      path: addin.path,
      icon: () => h(OtzariaAddinIcon, { path: addin.path }),
      open: (openInNewTab) => openAddin(addin, openInNewTab),
    })),
)
</script>

<template>
  <div class="websites-page">
    <div class="websites-grid">
      <HomeSuggestionCard v-for="card in [...cards, ...addinCards]" :key="card.key" :card="card" />
    </div>
  </div>
</template>

<style scoped>
.websites-page {
  height: 100%;
  overflow-y: auto;
  padding: 24px 16px;
  box-sizing: border-box;
  display: flex;
  justify-content: center;
}
.websites-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
  column-gap: 16px;
  row-gap: 4px;
  width: 100%;
  max-width: 960px;
  align-content: start;
}
</style>
