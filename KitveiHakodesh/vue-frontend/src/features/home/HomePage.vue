<script setup lang="ts">
import { ref, computed, onMounted, watch, nextTick } from 'vue'
import { useIntervalFn } from '@vueuse/core'
import HomeTile from './HomePageTile.vue'
import HomeSearchDropdown from './HomeSearchDropdown.vue'
import { useHomeSearch } from './useHomeSearch'
import { IconSearch20Regular } from '@iconify-prerendered/vue-fluent'
import { restoreLocalFile } from '@/webview-host/bridge'
import { useDropdownClose } from '@/composables/useDropdownClose'
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
import { useLocalFileStore } from '@/stores/localFileStore'
import { useSettingsStore } from '@/stores/settingsStore'
import { storeToRefs } from 'pinia'
import { useElementSize } from '@vueuse/core'
import type { Component } from 'vue'

const TILE_WIDTH = 72
const TILE_GAP = 20

const { navigate } = useAppNavigation()
const paneNavigation = usePaneNavigation()
const recentlyOpenedStore = useRecentlyOpenedStore()
const localFileStore = useLocalFileStore()
const settingsStore = useSettingsStore()
const { showRecentlyOpened } = storeToRefs(settingsStore)

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
const innerRef = ref<HTMLElement | null>(null)
const homeSearchQuery = ref('')
const searchBarRef = ref<HTMLElement | null>(null)
const searchBarInputRef = ref<HTMLInputElement | null>(null)
const isSearchDropdownOpen = ref(false)
const searchDropdownRef = ref<InstanceType<typeof HomeSearchDropdown> | null>(null)
const searchDropdownEl = computed(() => searchDropdownRef.value?.element ?? null)

// ── Animated placeholder ──────────────────────────────────────────────────────

const SEARCH_PLACEHOLDERS = [
  'חיפוש מהיר בכל המאגרים...',
  'כדי למיין תוצאות כתוב',
  'היברו בוקס: שבת',
  'או היברו: שבת',
  'קובץ: ברכות',
  'או מחשב: ברכות'
]
const searchPlaceholder = ref(SEARCH_PLACEHOLDERS[0]!)
let placeholderPhraseIndex = 0, placeholderCharIndex = 0, placeholderPauseTicks = 0

const { pause: pausePlaceholderTyping, resume: resumePlaceholderTyping } = useIntervalFn(() => {
  if (placeholderPauseTicks > 0) { placeholderPauseTicks--; return }
  const target = SEARCH_PLACEHOLDERS[placeholderPhraseIndex]!
  if (placeholderCharIndex < target.length) {
    searchPlaceholder.value = target.slice(0, ++placeholderCharIndex)
  } else {
    placeholderPauseTicks = 12
    placeholderPhraseIndex = (placeholderPhraseIndex + 1) % SEARCH_PLACEHOLDERS.length
    placeholderCharIndex = 0
  }
}, 80)

watch(homeSearchQuery, (value) => (value ? pausePlaceholderTyping() : resumePlaceholderTyping()))

const {
  catalogResults,
  hebrewBooksResults,
  fileResults,
  sourcePriority,
  isLoadingHebrewBooks,
  isLoadingFiles,
  hasAnyResults,
  clearResults,
  pause: pauseSearch,
  resume: resumeSearch,
} = useHomeSearch(homeSearchQuery)

useDropdownClose(searchBarRef, () => { isSearchDropdownOpen.value = false }, { ignore: [searchDropdownEl] })

// Open the dropdown when async sources resolve results after the debounce
watch([hebrewBooksResults, fileResults], () => {
  if ((homeSearchQuery.value ?? '').trim().length >= 2 && hasAnyResults()) {
    isSearchDropdownOpen.value = true
  }
})

function onSearchBarFocus() {
  if (hasAnyResults()) {
    computeDropdownAnchor()
    isSearchDropdownOpen.value = true
  }
}

function onSearchInputKeydown(e: KeyboardEvent) {
  if (!isSearchDropdownOpen.value) return
  if (e.code === 'Escape') {
    e.preventDefault()
    closeSearchDropdown()
    return
  }
  if (e.code === 'ArrowDown' || e.code === 'ArrowUp') {
    e.preventDefault()
    searchDropdownRef.value?.focus()
  }
}

function onSearchBarInput() {
  const hasQuery = (homeSearchQuery.value ?? '').trim().length >= 2
  if (hasQuery) computeDropdownAnchor()
  isSearchDropdownOpen.value = hasQuery
}

function closeSearchDropdown() {
  isSearchDropdownOpen.value = false
  clearResults()
  homeSearchQuery.value = ''
}

function onSelectCatalogBook(bookId: number, bookTitle: string) {
  closeSearchDropdown()
  paneNavigation.updateActiveTab({ route: '/book-view', title: bookTitle, bookId })
}

function onSelectHebrewBook(bookId: number, bookTitle: string) {
  closeSearchDropdown()
  // Navigate to the HebrewBooks page — the user can open the specific book from there.
  // Direct open would require triggering the download flow which belongs to useHebrewBooks.
  paneNavigation.navigateToSingleton('/hebrewbooks')
}

async function onSelectFile(fullPath: string, fileName: string) {
  closeSearchDropdown()
  if (!isHosted) return

  const extension = fileName.substring(fileName.lastIndexOf('.')).toLowerCase()
  const dotIndex = fileName.lastIndexOf('.')
  const titleWithoutExtension = dotIndex > 0 ? fileName.substring(0, dotIndex) : fileName

  if (extension === '.txt') {
    paneNavigation.updateActiveTab({
      route: '/txt-view',
      title: titleWithoutExtension,
      localFileName: fileName,
      localFilePath: fullPath,
      localFileVirtualUrl: undefined,
    })
    return
  }

  const isHtmlLike = extension === '.htm' || extension === '.html'
  const route = isHtmlLike ? '/html-view' : '/pdf-view'
  const restored = await restoreLocalFile(fullPath)
  if (!restored?.url) return

  paneNavigation.updateActiveTab({
    route,
    title: titleWithoutExtension,
    localFileName: fileName,
    localFilePath: fullPath,
    localFileVirtualUrl: restored.url,
  })
}
const { width: containerWidth } = useElementSize(innerRef)

// Compute dropdown position once when it opens — not reactively,
// because reactive position tracking would update on every scroll and fight scrollTop.
function computeDropdownAnchor() {
  if (!searchBarRef.value) return
  const rect = searchBarRef.value.getBoundingClientRect()
  dropdownAnchorTop.value = rect.bottom + 6
  dropdownAnchorLeft.value = rect.left
  dropdownAnchorRight.value = window.innerWidth - rect.right
  dropdownMaxHeight.value = Math.max(120, window.innerHeight - rect.bottom - 12)
}

const dropdownAnchorTop = ref(0)
const dropdownAnchorLeft = ref(0)
const dropdownAnchorRight = ref(0)
const dropdownMaxHeight = ref(300)

const visibleRecentlyOpenedList = computed(() => {
  if (!showRecentlyOpened.value) return []
  if (!recentlyOpenedList.value.length) return []
  const effectiveWidth = containerWidth.value || 320
  const tilesPerRow = Math.max(1, Math.floor((effectiveWidth + TILE_GAP) / (TILE_WIDTH + TILE_GAP)))
  const staticTailSlots = tiles.value.length % tilesPerRow
  const freeOnLastRow = staticTailSlots === 0 ? 0 : tilesPerRow - staticTailSlots
  const count = Math.min(20, freeOnLastRow + tilesPerRow)
  return recentlyOpenedList.value.slice(0, count)
})

const { focusedIndex, containerFocused } = useTilesKeys(
  pageRef,
  () => tiles.value.length,
  (i) => navigate(tiles.value[i]!.label),
)

onMounted(async () => {
  loadDateInfo()
  recentlyOpenedList.value = await recentlyOpenedStore.getList()
  nextTick(() => searchBarInputRef.value?.focus())
})

async function onTap(label: string) {
  await navigate(label)
}

function openRecentEntry(entry: RecentlyOpenedEntry) {
  if (entry.route === '/book-view' && entry.bookId !== undefined) {
    paneNavigation.updateActiveTab({ route: '/book-view', title: entry.title, bookId: entry.bookId })
    return
  }
  localFileStore.openFromHistory(entry)
}
</script>

<template>
  <div ref="pageRef" class="home-page" tabindex="0">
    <div ref="innerRef" class="home-inner">
      <div ref="searchBarRef" class="home-search-bar-wrapper">
        <div class="home-search-bar">
          <input
            ref="searchBarInputRef"
            v-model="homeSearchQuery"
            class="home-search-bar__field"
            type="search"
            :placeholder="searchPlaceholder"
            autocomplete="off"
            @focus="onSearchBarFocus"
            @input="onSearchBarInput"
            @keydown="onSearchInputKeydown"
          />
          <IconSearch20Regular class="home-search-bar__icon" />
        </div>
        <HomeSearchDropdown
          v-if="isSearchDropdownOpen"
          ref="searchDropdownRef"
          :catalog-results="catalogResults"
          :hebrew-books-results="hebrewBooksResults"
          :file-results="fileResults"
          :source-priority="sourcePriority"
          :is-loading-hebrew-books="isLoadingHebrewBooks"
          :is-loading-files="isLoadingFiles"
          :anchor-top="dropdownAnchorTop"
          :anchor-left="dropdownAnchorLeft"
          :anchor-right="dropdownAnchorRight"
          :max-height="dropdownMaxHeight"
          @select-catalog-book="onSelectCatalogBook"
          @select-hebrew-book="onSelectHebrewBook"
          @select-file="onSelectFile"
          @dropdown-focused="pauseSearch"
          @dropdown-blurred="resumeSearch"
        />
      </div>
      <div class="home-grid">
        <HomeTile
          v-for="(t, i) in tiles"
          :key="t.label"
          v-bind="t"
          :is-focused="containerFocused && focusedIndex === i"
          @tap="onTap(t.label)"
        />
        <HomeTile
          v-for="entry in visibleRecentlyOpenedList"
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
  container-type: inline-size;
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
  max-width: 920px;
}

/* Wide-screen search bar — always visible */
.home-search-bar-wrapper {
  display: block;
  position: relative;
  width: 100%;
  max-width: 560px;
  margin-bottom: 24px;
}

.home-search-bar {
  display: flex;
  align-items: center;
  gap: 8px;
  width: 100%;
  height: 38px;
  padding: 0 14px;
  background: var(--bg-primary);
  border: 1px solid transparent;
  border-radius: 999px;
  box-shadow: 0 2px 10px rgba(0, 0, 0, 0.18), 0 1px 3px rgba(0, 0, 0, 0.12);
  transition: box-shadow 150ms;
  overflow: hidden;
  min-width: 0;
}

.home-search-bar:focus-within {
  box-shadow: 0 3px 16px rgba(0, 0, 0, 0.22), 0 1px 4px rgba(0, 0, 0, 0.14);
}

.home-search-bar__field {
  flex: 1;
  min-width: 0;
  height: 100%;
  background: none;
  border: none;
  outline: none;
  font-size: 14px;
  color: var(--text-primary);
  direction: rtl;
}

.home-search-bar__icon {
  flex-shrink: 0;
  color: var(--text-secondary);
  opacity: 0.7;
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
