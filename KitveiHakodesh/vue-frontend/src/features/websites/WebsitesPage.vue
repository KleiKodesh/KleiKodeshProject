<script setup lang="ts">
import { computed, h, onMounted, ref } from 'vue'
import { useElementSize, watchDebounced } from '@vueuse/core'
import { IconDocument20Regular } from '@iconify-prerendered/vue-fluent'
import { useCardGridLayout } from '@/composables/useCardGridLayout'
import { useSettingsStore } from '@/stores/settingsStore'
import HomeSuggestionCard from '@/features/home/HomeSuggestionCard.vue'
import type { HomeSuggestionCard as HomeSuggestionCardData } from '@/features/home/homeSuggestionCard'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { useHomeSearchNavigation } from '@/features/home/useHomeSearchNavigation'
import type { FileSearchResult } from '@/features/global-search/useGlobalSearch'
import TopSearchBar from '@/components/TopSearchBar.vue'
import ViewModeDropdown from '@/components/ViewModeDropdown.vue'
import { fileSystemSearch } from '@/webview-host/bridge'
import { t } from '@/i18n'
import WebsiteFavicon from './WebsiteFavicon.vue'
import OtzariaAddinIcon from './OtzariaAddinIcon.vue'
import { loadOtzariaAddins, otzariaAddinsRef, type OtzariaAddin } from './otzariaAddinsList'
import { displayHost, loadWebsites, sortedWebsites, websiteName, websitesRef, type Website } from './websitesList'

/** The Websites & add-ins destination: every whitelisted Torah site and every installed Otzaria addin as a card.
 *  The search bar filters them and adds the local HTML files whose name matches. A click opens it in a tab. */
const paneNavigation = usePaneNavigation()
const settingsStore = useSettingsStore()

const VIEW_LABELS = {
  list: t('home.viewMode.list'),
  cards: t('home.viewMode.cards'),
  tiles: t('home.viewMode.tiles'),
}

// Same flow as the home page: whole columns that fit, cards shrink to fit one more but never stretch.
const scrollRef = ref<HTMLElement | null>(null)
const { width: scrollWidth } = useElementSize(scrollRef)
const layout = useCardGridLayout(scrollWidth, () => settingsStore.websitesView, 960)

const fileNavigation = useHomeSearchNavigation(() => {})

onMounted(() => {
  void loadWebsites()
  void loadOtzariaAddins()
})

const query = ref('')
const needle = computed(() => query.value.trim().toLowerCase())
const matches = (...texts: (string | undefined)[]) =>
  !needle.value || texts.some((text) => (text ?? '').toLowerCase().includes(needle.value))

function openAddin(addin: OtzariaAddin, openInNewTab: boolean): void {
  void fileNavigation.onSelectFile({ source: 'files', fileName: addin.fileName, fullPath: addin.path, addinName: addin.addinName }, openInNewTab)
}

function openSite(site: Website, openInNewTab: boolean): void {
  paneNavigation.openOrUpdateActiveTab({ route: '/web-view', title: websiteName(site), webUrl: site.url }, openInNewTab)
}

const siteCards = computed<HomeSuggestionCardData[]>(() =>
  sortedWebsites(websitesRef.value)
    .filter((site) => matches(site.name, site.nameEn, displayHost(site.url)))
    .map((site) => ({
      key: `site:${site.id}`,
      title: websiteName(site),
      path: displayHost(site.url),
      icon: () => h(WebsiteFavicon, { url: site.url }),
      open: (openInNewTab) => openSite(site, openInNewTab),
    })),
)

const addinCards = computed<HomeSuggestionCardData[]>(() =>
  [...otzariaAddinsRef.value]
    .filter((addin) => matches(addin.name, addin.path))
    .sort((a, b) => a.name.localeCompare(b.name, 'he'))
    .map((addin) => ({
      key: `addin:${addin.path}`,
      title: addin.name,
      path: addin.path,
      icon: () => h(OtzariaAddinIcon, { path: addin.path }),
      open: (openInNewTab) => openAddin(addin, openInNewTab),
    })),
)

// Local HTML files from the local file search; only while something is typed.
const htmlFiles = ref<FileSearchResult[]>([])
let searchGeneration = 0
watchDebounced(
  needle,
  async (text) => {
    const generation = ++searchGeneration
    if (text.length < 2) {
      htmlFiles.value = []
      return
    }
    // -1 = every match (as the file-search page asks), so no HTML file is pushed out by other types.
    const response = await fileSystemSearch(text, -1).catch(() => ({ results: undefined }))
    if (generation !== searchGeneration) return
    htmlFiles.value = (response.results ?? [])
      // Addin entry points already show as add-in cards.
      .filter((hit) => !hit.addinName && /\.html?$/i.test(hit.fileName))
      .map((hit) => ({
        source: 'files' as const,
        fileName: hit.fileName,
        fullPath: hit.path ? `${hit.path}\\${hit.fileName}` : hit.fileName,
        addinName: '',
        displayName: hit.displayName ?? '',
      }))
  },
  { debounce: 200 },
)

const fileCards = computed<HomeSuggestionCardData[]>(() =>
  htmlFiles.value.map((file) => ({
    key: `file:${file.fullPath}`,
    title: file.displayName || file.fileName,
    path: file.fullPath,
    icon: IconDocument20Regular,
    open: (openInNewTab) => void fileNavigation.onSelectFile(file, openInNewTab),
  })),
)

const allCards = computed(() => [...siteCards.value, ...addinCards.value, ...fileCards.value])
</script>

<template>
  <div class="websites-page" :style="{ '--grid-width': `${layout.width}px`, '--grid-columns': layout.columns }">
    <TopSearchBar class="websites-search">
      <input v-model="query" type="search" :placeholder="$t('websites.searchPlaceholder')" autocomplete="off" @keydown.esc="query = ''" />
      <template #right>
        <ViewModeDropdown
          v-model="settingsStore.websitesView"
          class="websites-search__button"
          tabindex="-1"
          :labels="VIEW_LABELS"
          :title="$t('home.viewMode.menu')"
        />
      </template>
    </TopSearchBar>
    <div ref="scrollRef" class="websites-scroll">
      <div class="websites-grid" :class="`websites-grid--${settingsStore.websitesView}`">
        <HomeSuggestionCard v-for="card in allCards" :key="card.key" :card="card" :layout="settingsStore.websitesView" />
      </div>
    </div>
  </div>
</template>

<style scoped>
.websites-page {
  height: 100%;
  display: flex;
  flex-direction: column;
  min-height: 0;
}
/* The pill lines up with the grid's edges, like the home page's (the bar adds 10px each side). */
.websites-search {
  width: 100%;
  max-width: calc(var(--grid-width) + 20px);
  align-self: center;
  box-sizing: border-box;
  padding-top: 12px;
}
.websites-search input {
  flex: 1;
  min-width: 0;
}
.websites-search__button {
  width: 20px;
  height: 20px;
  padding: 0;
}
.websites-scroll {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  padding: 12px 16px 24px;
  box-sizing: border-box;
  display: flex;
  justify-content: center;
}
.websites-grid {
  display: grid;
  grid-template-columns: repeat(var(--grid-columns, 3), minmax(0, 1fr));
  width: 100%;
  max-width: var(--grid-width);
  align-content: start;
}
/* Gaps match HomePage.vue's .home-cards and useCardGridLayout. */
.websites-grid--cards {
  column-gap: 16px;
  row-gap: 4px;
}
.websites-grid--list {
  grid-template-columns: 1fr;
}
.websites-grid--tiles {
  grid-template-columns: repeat(var(--grid-columns, 9), 72px);
  gap: 6px;
  justify-items: center;
}
</style>
