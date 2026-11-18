<script setup lang="ts">
import { computed, h, onMounted } from 'vue'
import HomeSuggestionCard from '@/features/home/HomeSuggestionCard.vue'
import type { HomeSuggestionCard as HomeSuggestionCardData } from '@/features/home/homeSuggestionCard'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import WebsiteFavicon from './WebsiteFavicon.vue'
import { displayHost, loadWebsites, sortedWebsites, websiteName, websitesRef, type Website } from './websitesList'

/** The Websites destination: every whitelisted Torah site as a card. A click opens it in a tab. */
const paneNavigation = usePaneNavigation()

onMounted(() => void loadWebsites())

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
</script>

<template>
  <div class="websites-page">
    <div class="websites-grid">
      <HomeSuggestionCard v-for="card in cards" :key="card.key" :card="card" />
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
