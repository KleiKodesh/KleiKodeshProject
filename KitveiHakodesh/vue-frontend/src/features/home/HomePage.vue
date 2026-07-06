<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import HomeTile from './HomePageTile.vue'
import {
  IconLibrary24Filled,
  IconFolder24Filled,
  IconBookOpen24Filled,
  IconApps24Filled,
  IconDatabase24Filled,
  IconArrowDownload24Filled,
  IconCalendarRtl24Filled,
  IconBookLetter24Filled,
  IconRuler24Filled,
  IconDocumentPdf24Filled,
  IconDocumentText24Filled,
  IconDocumentGlobe24Filled,
} from '@iconify-prerendered/vue-fluent'
import IconEverythingSearch from '@/components/IconEverythingSearch.vue'
import IconBookRtl24 from '@/components/IconBookRtl24.vue'
import { IconSettings24, IconSearchSparkle24 } from '@iconify-prerendered/vue-fluent-color'
import { isHosted, dbReady } from '@/webview-host/seforimDb'
import { useAppNavigation } from '@/composables/useAppNavigation'
import { useTilesKeys } from '@/composables/useTileGridKeys'
import { dateInfo, loadDateInfo } from './homeDateInfo'
import { navigateToDafYomi } from './dafYomiNavigation'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { useRecentlyOpenedStore } from '@/stores/recentlyOpenedStore'
import type { RecentlyOpenedEntry } from '@/stores/recentlyOpenedStore'
import type { Component } from 'vue'

const { navigate } = useAppNavigation()
const paneNavigation = usePaneNavigation()
const recentlyOpenedStore = useRecentlyOpenedStore()

const recentlyOpenedList = ref<RecentlyOpenedEntry[]>([])

const RECENTLY_OPENED_ICON_MAP: Record<string, { icon: Component; color: string }> = {
  '/book-view': { icon: IconBookRtl24, color: '#c1440e' },
  '/pdf-view': { icon: IconDocumentPdf24Filled, color: '#F40F02' },
  '/html-view': { icon: IconDocumentGlobe24Filled, color: '#0097fb' },
  '/txt-view': { icon: IconDocumentText24Filled, color: '#9e9e9e' },
}
const tiles = computed(() => {
  const dbMissing = isHosted && !dbReady.value
  return [
    dbMissing
      ? { label: 'הורד מסד ספרים', icon: IconArrowDownload24Filled, color: '#B5451B' }
      : { label: 'ספרים', icon: IconLibrary24Filled, color: '#B5451B' },
    dbMissing
      ? { label: 'בחר מסד ספרים', icon: IconDatabase24Filled, color: '#3478f6' }
      : { label: 'חיפוש', icon: IconSearchSparkle24 },
    { label: 'היברו-בוקס', icon: IconBookOpen24Filled, color: '#D94F1E' },
    { label: 'פתח קובץ', icon: IconFolder24Filled, color: '#f0a500' },
    { label: 'חיפוש קבצים', icon: IconEverythingSearch },
    { label: 'מילון', icon: IconBookLetter24Filled, color: '#7b5ea7' },
    { label: 'לוח שנה', icon: IconCalendarRtl24Filled, color: '#2e7d32' },
    { label: 'מידות ושיעורים', icon: IconRuler24Filled, color: '#8b6914' },
    { label: 'סביבות עבודה', icon: IconApps24Filled, color: '#6b7fc4' },
    { label: 'הגדרות', icon: IconSettings24 },
  ]
})

const pageRef = ref<HTMLElement | null>(null)

const { focusedIndex, containerFocused } = useTilesKeys(
  pageRef,
  () => tiles.value.length,
  (i) => navigate(tiles.value[i]!.label),
)

onMounted(async () => {
  pageRef.value?.focus()
  loadDateInfo()
  recentlyOpenedList.value = await recentlyOpenedStore.getList()
})

async function onTap(label: string) {
  await navigate(label)
}

function openRecentEntry(entry: RecentlyOpenedEntry) {
  if (entry.route === '/book-view' && entry.bookId !== undefined) {
    paneNavigation.updateActiveTab({ route: '/book-view', title: entry.title, bookId: entry.bookId })
    return
  }
  if (entry.route === '/pdf-view' || entry.route === '/html-view' || entry.route === '/txt-view') {
    paneNavigation.updateActiveTab({
      route: entry.route,
      title: entry.title,
      localFilePath: entry.localFilePath,
      localFileName: entry.localFileName ?? entry.title,
      localFileHbBookId: entry.localFileHbBookId,
      localFileHbBookTitle: entry.localFileHbBookTitle,
    })
  }
}
</script>

<template>
  <div ref="pageRef" class="home-page" tabindex="0">
    <div class="home-inner">
      <div class="home-grid">
        <HomeTile
          v-for="(t, i) in tiles"
          :key="t.label"
          v-bind="t"
          :is-focused="containerFocused && focusedIndex === i"
          @tap="onTap(t.label)"
        />
        <HomeTile
          v-for="entry in recentlyOpenedList"
          :key="entry.key"
          :label="entry.title"
          :icon="RECENTLY_OPENED_ICON_MAP[entry.route]!.icon"
          :color="RECENTLY_OPENED_ICON_MAP[entry.route]!.color"
          @tap="openRecentEntry(entry)"
        />
      </div>
    </div>

    <div class="date-bar">
      <button
        class="date-hebrew date-hebrew--btn"
        @click="paneNavigation.navigateToSingleton('/hebrew-calendar')"
      >
        {{ dateInfo.hebrewDate }}
      </button>
      <span class="bar-sep">·</span>
      <button
        v-if="dateInfo.dafYomi && dbReady"
        class="bar-item bar-item--btn"
        @click="navigateToDafYomi(dateInfo.dafYomi, paneNavigation)"
      >
        <span class="bar-lbl">דף יומי:</span> {{ dateInfo.dafYomi }}
      </button>
      <span v-else-if="dateInfo.dafYomi" class="bar-item"
        ><span class="bar-lbl">דף יומי:</span> {{ dateInfo.dafYomi }}</span
      >
    </div>
  </div>
</template>

<style scoped>
.home-page {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow-y: auto;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
  outline: none;
  position: relative;
}

.home-inner {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  flex: 1;
  min-height: min-content;
  padding: 24px;
}

.home-grid {
  display: flex;
  flex-wrap: wrap;
  justify-content: center;
  gap: 20px;
}

/* Bottom bar */
.date-bar {
  position: sticky;
  bottom: 0;
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: center;
  gap: 6px;
  padding: 8px 16px;
  background: var(--bg-secondary);
  border-top: 1px solid var(--border-color);
  font-size: 11px;
}
.date-hebrew {
  font-weight: 600;
  color: var(--text-primary);
}
.date-hebrew--btn {
  background: none;
  border: none;
  padding: 0;
  font-size: inherit;
  font-family: inherit;
  font-weight: 600;
  cursor: pointer;
  color: var(--text-primary);
}
.date-hebrew--btn:hover {
  color: var(--accent-color);
}
.bar-sep {
  color: var(--text-secondary);
  opacity: 0.4;
}
.bar-item {
  color: var(--text-secondary);
  white-space: nowrap;
}
.bar-lbl {
  font-weight: 600;
  color: var(--text-primary);
}
.bar-item--btn {
  background: none;
  border: none;
  padding: 0;
  font-size: inherit;
  font-family: inherit;
  cursor: pointer;
  color: var(--text-secondary);
  white-space: nowrap;
}
.bar-item--btn:hover {
  color: var(--accent-color);
}
.bar-item--btn:hover .bar-lbl {
  color: inherit;
}
</style>
