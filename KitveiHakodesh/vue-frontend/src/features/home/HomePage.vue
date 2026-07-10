<script setup lang="ts">
import { ref, computed, onMounted, watch, nextTick } from 'vue'
import { useIntervalFn, useElementSize } from '@vueuse/core'
import HomeTile from './HomePageTile.vue'
import HomeSearchDropdown from './HomeSearchDropdown.vue'
import { useHomeSearch } from './useHomeSearch'
import { IconSearch20Regular } from '@iconify-prerendered/vue-fluent'
import { restoreLocalFile, triggerHbDownload } from '@/webview-host/bridge'
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
import { dateInfo, loadDateInfo } from './homeDateInfo'
import { navigateToDafYomi } from './dafYomiNavigation'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { useRecentlyOpenedStore } from '@/stores/recentlyOpenedStore'
import type { RecentlyOpenedEntry } from '@/stores/recentlyOpenedStore'
import { useLocalFileStore } from '@/stores/localFileStore'
import { useSettingsStore } from '@/stores/settingsStore'
import { useHebrewBooksHistoryStore } from '@/stores/hebrewBooksHistoryStore'
import { getHbPdfUrl, type HebrewBook } from '@/features/hebrewbooks/hebrewBooksCatalog'
import { storeToRefs } from 'pinia'
import type { Component } from 'vue'

const TILE_WIDTH = 72
const TILE_GAP = 16

const { navigate } = useAppNavigation()
const paneNavigation = usePaneNavigation()
const recentlyOpenedStore = useRecentlyOpenedStore()
const localFileStore = useLocalFileStore()
const settingsStore = useSettingsStore()
const hebrewBooksHistoryStore = useHebrewBooksHistoryStore()
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
    { label: 'חיפוש קבצים', icon: IconEverythingSearch, iconScale: 0.93 },
    { label: 'מילון', icon: IconBookLetter24Filled, color: '#7b5ea7' },
    { label: 'לוח שנה', icon: IconCalendarRtl24Filled, color: '#2e7d32' },
    { label: 'מידות ושיעורים', icon: IconRuler24Filled, color: '#8b6914' },
    { label: 'סביבות עבודה', icon: IconApps24Filled, color: '#6b7fc4' },
    { label: 'הגדרות', icon: IconSettings24 },
  ]
})

function onTileKeydown(e: KeyboardEvent, index: number) {
  const totalTiles = tiles.value.length + visibleRecentlyOpenedList.value.length
  const isLastTile = index === totalTiles - 1
  if (e.code === 'Tab' && !e.shiftKey && isLastTile) {
    e.preventDefault()
    searchBarInputRef.value?.focus()
  }
}

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
  'כדי להקדים תוצאות מהיברו בוקס כתוב',
  'היברו בוקס: שבת',
  'או היברו: שבת',
  'או: \\ שבת',
  'כדי להקדים תוצאות מהמחשב כתוב',
  'קובץ: ברכות',
  'או מחשב: ברכות',
  'או: \\\\ ברכות',
  'לחץ אנטר לחיפוש תוכן במאגר'
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
  isLoadingAny,
  clearResults,
  pause: pauseSearch,
  resume: resumeSearch,
} = useHomeSearch(homeSearchQuery)

useDropdownClose(searchBarRef, () => { isSearchDropdownOpen.value = false }, { ignore: [searchDropdownEl] })

// Open the dropdown when async sources resolve results after the debounce
watch([hebrewBooksResults, fileResults], () => {
  if ((homeSearchQuery.value ?? '').trim().length >= 2) {
    if (hasAnyResults()) {
      isSearchDropdownOpen.value = true
    } else if (!isLoadingAny()) {
      isSearchDropdownOpen.value = false
    }
  }
})

function onSearchBarFocus() {
  if (hasAnyResults()) {
    computeDropdownAnchor()
    isSearchDropdownOpen.value = true
  }
}

function launchFullTextSearch() {
  const query = homeSearchQuery.value.trim()
  if (!query) return
  closeSearchDropdown()
  paneNavigation.updateActiveTab({ route: '/search', title: `חיפוש: ${query}`, searchQuery: query })
}

function onSearchInputKeydown(e: KeyboardEvent) {
  if (e.code === 'Enter') {
    e.preventDefault()
    launchFullTextSearch()
    return
  }
  if (e.code === 'Escape') {
    e.preventDefault()
    closeSearchDropdown()
    return
  }
  if (!isSearchDropdownOpen.value) return
  if (e.code === 'ArrowDown' || e.code === 'ArrowUp') {
    e.preventDefault()
    searchDropdownRef.value?.focus()
  }
}

function onSearchBarInput() {
  const hasQuery = (homeSearchQuery.value ?? '').trim().length >= 2
  if (hasQuery) computeDropdownAnchor()
  isSearchDropdownOpen.value = hasQuery && (hasAnyResults() || isLoadingAny())
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

function onSelectHebrewBook(book: HebrewBook) {
  closeSearchDropdown()
  hebrewBooksHistoryStore.trackAccess(book)
  const tabId = paneNavigation.activeTabId
  localFileStore.startHbDownload(book.title, tabId)
  triggerHbDownload(
    String(book.id),
    book.title,
    getHbPdfUrl(book.id),
    tabId,
    settingsStore.hebrewBooksLocalFolder || undefined,
    navigator.onLine,
  ).catch(() => {})
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
  <div class="home-page">
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
          <button class="home-search-bar__search-button" tabindex="-1" title="הקלד לחיפוש שם ספר. לחץ כאן לחיפוש תוכן במאגרץ" @click="launchFullTextSearch">
            <IconSearch20Regular />
          </button>
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
          @tap="onTap(t.label)"
          @keydown="onTileKeydown($event, i)"
        />
        <HomeTile
          v-for="(entry, i) in visibleRecentlyOpenedList"
          :key="entry.key"
          :label="entry.title"
          :icon="RECENTLY_OPENED_ICON_MAP[entry.route]!.icon"
          :color="RECENTLY_OPENED_ICON_MAP[entry.route]!.color"
          @tap="openRecentEntry(entry)"
          @keydown="onTileKeydown($event, tiles.length + i)"
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
  padding: 16px 24px;
}

.home-grid {
  display: flex;
  flex-wrap: wrap;
  justify-content: center;
  gap: 4px 8px;
  max-width: 920px;
}

/* Search bar — uses the same .search-inner pattern as the rest of the app */
.home-search-bar-wrapper {
  display: block;
  position: relative;
  width: 100%;
  max-width: 560px;
  margin-bottom: 20px;
}

.home-search-bar {
  display: flex;
  align-items: center;
  gap: 6px;
  width: 100%;
  padding: 5px 10px;
  background: var(--input-bg);
  border: 1px solid var(--border-color);
  border-radius: 999px;
  transition: background 150ms;
  overflow: hidden;
  min-width: 0;
}

.home-search-bar:focus-within {
  background: var(--input-bg-focus);
}

.home-search-bar__field {
  flex: 1;
  min-width: 0;
  height: 100%;
  background: none;
  border: none;
  outline: none;
  font-size: 13px;
  color: var(--text-primary);
  direction: rtl;
}

.home-search-bar__field::placeholder {
  color: var(--text-secondary);
  opacity: 0.7;
}

.home-search-bar__search-button {
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  width: 24px;
  height: 24px;
  padding: 0;
  border-radius: 4px;
  color: var(--text-secondary);
  opacity: 0.6;
}

.home-search-bar__search-button:hover {
  opacity: 1;
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}

/* Date bar */
.date-bar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: center;
  gap: 6px;
  padding: 8px 16px;
  font-size: 11.5px;
  color: var(--text-secondary);
  border-top: 1px solid var(--border-color);
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
  opacity: 0.3;
}
.bar-item {
  color: var(--text-secondary);
  white-space: nowrap;
}
.bar-lbl {
  font-weight: 600;
  color: var(--text-primary);
  opacity: 0.7;
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
  opacity: 1;
}
</style>
