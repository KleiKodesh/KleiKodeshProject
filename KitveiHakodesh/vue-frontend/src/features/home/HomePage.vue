<script setup lang="ts">
import { ref, computed, onMounted, watch, nextTick } from 'vue'
import { useIntervalFn, useElementSize, useNow, useWindowSize, useResizeObserver, useEventListener } from '@vueuse/core'
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
import { useNextZman } from './useNextZman'
import NextZmanPopup from './NextZmanPopup.vue'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { useTabStore } from '@/stores/tabStore'
import { useRecentlyOpenedStore } from '@/stores/recentlyOpenedStore'
import type { RecentlyOpenedEntry } from '@/stores/recentlyOpenedStore'
import { useLocalFileStore } from '@/stores/localFileStore'
import { useSettingsStore } from '@/stores/settingsStore'
import { useHebrewBooksHistoryStore } from '@/stores/hebrewBooksHistoryStore'
import { getHbPdfUrl, type HebrewBook } from '@/features/hebrewbooks/hebrewBooksCatalog'
import type { TocFsItem } from '@/features/book-catalog/useBookCatalogSearch'
import { storeToRefs } from 'pinia'
import type { Component } from 'vue'

const TILE_WIDTH = 72
const TILE_GAP = 16

const { navigate } = useAppNavigation()
const paneNavigation = usePaneNavigation()
const tabStore = useTabStore()
const recentlyOpenedStore = useRecentlyOpenedStore()
const localFileStore = useLocalFileStore()
const settingsStore = useSettingsStore()
const hebrewBooksHistoryStore = useHebrewBooksHistoryStore()
const { showRecentlyOpened, showClock } = storeToRefs(settingsStore)

// Current clock time for the bottom bar. Hidden when the floating fullscreen
// ClockWidget (App.vue) is already showing it — i.e. showClock && fullscreen —
// so we never display the time twice.
const now = useNow({ interval: 10_000 })
const { height: windowHeight } = useWindowSize()
const isFullscreen = computed(() => windowHeight.value >= screen.height)
const clockTime = computed(() =>
  now.value.toLocaleTimeString('he-IL', { hour: '2-digit', minute: '2-digit', hour12: false }),
)
const showBarClock = computed(() => !(showClock.value && isFullscreen.value))

const { next: nextZman, displayTime: nextZmanTime, rows: zmanRows, city: zmanCity } = useNextZman()

// The date bar never wraps. When it can't fit on one line we drop optional
// items by priority — clock first (level ≥ 1), then the nearest-zman
// (level ≥ 2) — keeping the date and daf yomi.
const dateBarRef = ref<HTMLElement | null>(null)
const barHideLevel = ref(0)
const showClockInBar = computed(() => showBarClock.value && barHideLevel.value < 1)
const showZmanInBar = computed(() => !!nextZman.value && barHideLevel.value < 2)

let measuring = false
function measureBarFit() {
  const el = dateBarRef.value
  if (!el || measuring) return
  measuring = true
  // Try to show as much as possible, then step down until it fits (or we've
  // dropped everything droppable). Each step re-measures after the DOM updates.
  const step = () => {
    if (!dateBarRef.value) { measuring = false; return }
    const overflow = dateBarRef.value.scrollWidth > dateBarRef.value.clientWidth + 1
    if (overflow && barHideLevel.value < 2) {
      barHideLevel.value++
      nextTick(step)
    } else if (!overflow && barHideLevel.value > 0) {
      // Room may have opened up — try restoring one level and see if it still fits.
      const prev = barHideLevel.value
      barHideLevel.value--
      nextTick(() => {
        if (dateBarRef.value && dateBarRef.value.scrollWidth > dateBarRef.value.clientWidth + 1) {
          barHideLevel.value = prev // didn't fit; revert
          measuring = false
        } else {
          step() // fit — keep trying to restore more
        }
      })
    } else {
      measuring = false
    }
  }
  step()
}

useResizeObserver(dateBarRef, () => measureBarFit())
// Re-measure when the content itself changes (zman appears, daf loads, etc.).
watch([showBarClock, nextZman, () => dateInfo.value.dafYomi, clockTime], () =>
  nextTick(measureBarFit),
)

const isZmanPopupOpen = ref(false)
const zmanBarItemRef = ref<HTMLElement | null>(null)
const zmanButtonRef = ref<HTMLElement | null>(null)
const zmanPopupRef = ref<InstanceType<typeof NextZmanPopup> | null>(null)
const zmanPopupEl = computed<HTMLElement | null>(() => (zmanPopupRef.value?.$el as HTMLElement) ?? null)

// Fixed-position anchor for the zmanim popup: centered over the trigger, then
// clamped so it never escapes the viewport, and pinned just above the bottom bar.
const ZMAN_POPUP_MARGIN = 8
const zmanPopupLeft = ref(0)
const zmanPopupBottom = ref(0)
const zmanPopupMaxHeight = ref(0)
const zmanPopupStyle = computed(() => ({
  left: `${zmanPopupLeft.value}px`,
  bottom: `${zmanPopupBottom.value}px`,
  maxHeight: `${zmanPopupMaxHeight.value}px`,
}))

function computeZmanPopupAnchor() {
  const btn = zmanButtonRef.value
  if (!btn) return
  const rect = btn.getBoundingClientRect()
  // Best-effort width (falls back to the popup's min-width before it mounts).
  const width = zmanPopupEl.value?.offsetWidth || 220
  const center = rect.left + rect.width / 2
  let left = center - width / 2
  const maxLeft = window.innerWidth - width - ZMAN_POPUP_MARGIN
  left = Math.min(Math.max(left, ZMAN_POPUP_MARGIN), Math.max(ZMAN_POPUP_MARGIN, maxLeft))
  zmanPopupLeft.value = left
  zmanPopupBottom.value = window.innerHeight - rect.top + ZMAN_POPUP_MARGIN
  // The popup opens upward from just above the bar; cap its height to the space
  // between the top margin and the bar so it never overflows the top.
  zmanPopupMaxHeight.value = Math.max(120, rect.top - ZMAN_POPUP_MARGIN * 2)
}

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
  'לחץ אנטר לחיפוש תוכן במאגר',
  'הקלד חופשי לחיפוש ספר או קובץ',
  'כדי להקדים תוצאות מהיברו בוקס כתוב',
  'היברו בוקס: שבת',
  'או היברו: שבת',
  'או: \\ שבת',
  'כדי להקדים תוצאות מהמחשב כתוב',
  'קובץ: ברכות',
  'או מחשב: ברכות',
  'או: \\\\ ברכות',
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
  catalogTocResults,
  hebrewBooksResults,
  fileResults,
  sourcePriority,
  isLoadingCatalogToc,
  isLoadingHebrewBooks,
  isLoadingFiles,
  hasAnyResults,
  isLoadingAny,
  clearResults,
  pause: pauseSearch,
  resume: resumeSearch,
} = useHomeSearch(homeSearchQuery)

useDropdownClose(searchBarRef, () => { isSearchDropdownOpen.value = false }, { ignore: [searchDropdownEl] })
const zmanCloser = useDropdownClose(zmanBarItemRef, () => { isZmanPopupOpen.value = false }, {
  ignore: [zmanPopupEl],
})
function toggleZmanPopup() {
  if (zmanCloser.justClosed.value) return
  const opening = !isZmanPopupOpen.value
  isZmanPopupOpen.value = opening
  if (opening) {
    computeZmanPopupAnchor()
    // Re-clamp once the popup has rendered and its real width is known.
    nextTick(computeZmanPopupAnchor)
  }
}

// Open the dropdown when async sources resolve results after the debounce
watch([catalogTocResults, hebrewBooksResults, fileResults], () => {
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

function onSelectCatalogBook(bookId: number, bookTitle: string, openInNewTab = false) {
  closeSearchDropdown()
  paneNavigation.openOrUpdateActiveTab(
    { route: '/book-view', title: bookTitle, bookId },
    openInNewTab,
  )
}

function onSelectCatalogToc(item: TocFsItem, openInNewTab = false) {
  closeSearchDropdown()
  paneNavigation.openOrUpdateActiveTab(
    {
      route: '/book-view',
      title: item.book.title,
      bookId: item.book.id,
      openTocEntryId: item.tocEntryId,
      openTocLineIndex: item.tocLineIndex ?? undefined,
    },
    openInNewTab,
  )
}

function onSelectHebrewBook(book: HebrewBook, openInNewTab = false) {
  closeSearchDropdown()
  hebrewBooksHistoryStore.trackAccess(book)
  // Download lifecycle is tab-id-driven (see useHebrewBooks.openBook) — for a
  // Ctrl/⌘-click open a fresh placeholder tab and target its id.
  const tabId = openInNewTab
    ? paneNavigation.openTab({ route: '/pdf-view', title: book.title }).id
    : paneNavigation.activeTabId
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

async function onSelectFile(fullPath: string, fileName: string, openInNewTab = false) {
  closeSearchDropdown()
  if (!isHosted) return

  const extension = fileName.substring(fileName.lastIndexOf('.')).toLowerCase()
  const dotIndex = fileName.lastIndexOf('.')
  const titleWithoutExtension = dotIndex > 0 ? fileName.substring(0, dotIndex) : fileName

  const isHtmlLike = extension === '.htm' || extension === '.html'
  const route = extension === '.txt' ? '/txt-view' : isHtmlLike ? '/html-view' : '/pdf-view'

  // Capture the target tab id up front (a new tab for Ctrl/⌘-click, else the
  // current active tab) and patch it by id — restoreLocalFile awaits, and the
  // active tab may change during that await.
  const targetTabId = openInNewTab
    ? paneNavigation.openTab({ route, title: titleWithoutExtension }).id
    : paneNavigation.activeTabId

  if (extension === '.txt') {
    tabStore.updateTab(targetTabId, {
      route: '/txt-view',
      title: titleWithoutExtension,
      localFileName: fileName,
      localFilePath: fullPath,
      localFileVirtualUrl: undefined,
    })
    return
  }

  const restored = await restoreLocalFile(fullPath)
  if (!restored?.url) return

  tabStore.updateTab(targetTabId, {
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


// On a cold start the hosting WebView2 may not hold OS focus yet when HomePage
// mounts — the host only gives the web content OS focus on NavigationCompleted /
// Form.Activated (see AppViewerFocus), which can land after us. focus() on an
// element whose window lacks OS focus silently doesn't stick, which is why the
// initial focus was unreliable on cold start but fine on later warm navigations.
// Fix: focus now if we already own OS focus, otherwise focus once the window
// gains it (the host's _webView.Focus() fires window's 'focus' event).
function focusSearchInput() {
  const input = searchBarInputRef.value
  if (!input) return
  if (document.hasFocus()) input.focus()
  else useEventListener(window, 'focus', () => input.focus(), { once: true })
}

onMounted(() => {
  loadDateInfo()
  // Focus up front, independent of the getList() await below — on a cold start
  // that first-run IndexedDB read is slow and must not delay the focus.
  focusSearchInput()
  recentlyOpenedStore.getList().then((list) => { recentlyOpenedList.value = list })
})

async function onTap(label: string) {
  await navigate(label)
}

function openRecentEntry(entry: RecentlyOpenedEntry, openInNewTab = false) {
  if (entry.route === '/book-view' && entry.bookId !== undefined) {
    paneNavigation.openOrUpdateActiveTab(
      { route: '/book-view', title: entry.title, bookId: entry.bookId },
      openInNewTab,
    )
    return
  }
  localFileStore.openFromHistory(entry, openInNewTab)
}

// ── Recently-opened tile actions (pin / remove) ─────────────────────────────────
function onTogglePinRecent(entry: RecentlyOpenedEntry) {
  recentlyOpenedList.value = recentlyOpenedStore.togglePin(entry.key)
}
function onRemoveRecent(entry: RecentlyOpenedEntry) {
  recentlyOpenedList.value = recentlyOpenedStore.removeEntry(entry.key)
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
          :catalog-toc-results="catalogTocResults"
          :hebrew-books-results="hebrewBooksResults"
          :file-results="fileResults"
          :source-priority="sourcePriority"
          :is-loading-catalog-toc="isLoadingCatalogToc"
          :is-loading-hebrew-books="isLoadingHebrewBooks"
          :is-loading-files="isLoadingFiles"
          :anchor-top="dropdownAnchorTop"
          :anchor-left="dropdownAnchorLeft"
          :anchor-right="dropdownAnchorRight"
          :max-height="dropdownMaxHeight"
          @select-catalog-book="onSelectCatalogBook"
          @select-catalog-toc="onSelectCatalogToc"
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
          :pinned="entry.pinned"
          actions
          @tap="openRecentEntry(entry, $event)"
          @keydown="onTileKeydown($event, tiles.length + i)"
          @toggle-pin="onTogglePinRecent(entry)"
          @remove="onRemoveRecent(entry)"
        />
      </div>
    </div>

    <div ref="dateBarRef" class="date-bar">
      <template v-if="showClockInBar">
        <span class="bar-item bar-clock">{{ clockTime }}</span>
        <span class="bar-sep">·</span>
      </template>
      <template v-if="nextZman && showZmanInBar">
        <div ref="zmanBarItemRef" class="zman-wrap">
          <button
            ref="zmanButtonRef"
            class="bar-item bar-item--btn zman"
            :class="[`zman--${nextZman.urgency}`, { on: isZmanPopupOpen, 'zman--flash': nextZman.flash }]"
            :title="`בעוד ${nextZman.minutesUntil} דקות · לחץ לכל הזמנים`"
            @click="toggleZmanPopup"
          >
            <span class="bar-lbl">{{ nextZman.label }}:</span> {{ nextZmanTime }}
          </button>
        </div>
        <Teleport to="body">
          <div v-if="isZmanPopupOpen" class="zman-popup-anchor" :style="zmanPopupStyle">
            <NextZmanPopup ref="zmanPopupRef" :rows="zmanRows" :city-name="zmanCity.name" />
          </div>
        </Teleport>
        <span class="bar-sep">·</span>
      </template>
      <button
        class="bar-item bar-item--btn"
        @click="paneNavigation.navigateToSingleton('/hebrew-calendar')"
      >
        {{ dateInfo.hebrewDate }}
      </button>
      <template v-if="dateInfo.dafYomi">
        <span class="bar-sep">·</span>
        <button
          v-if="dbReady"
          class="bar-item bar-item--btn"
          @click="navigateToDafYomi(dateInfo.dafYomi, paneNavigation)"
        >
          <span class="bar-lbl">דף יומי:</span> {{ dateInfo.dafYomi }}
        </button>
        <span v-else class="bar-item"
          ><span class="bar-lbl">דף יומי:</span> {{ dateInfo.dafYomi }}</span
        >
      </template>
    </div>
  </div>
</template>

<style scoped>
.home-page {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
  outline: none;
  position: relative;
  container-type: inline-size;
}

/* Search + tiles scroll together as one group, centered when there's room and
   pushed toward the top once they overflow. Only the date bar stays fixed. */
.home-inner {
  display: flex;
  flex-direction: column;
  align-items: center;
  /* safe center: vertically centered when there's room, but aligns to the top
     once content overflows so the search bar stays reachable by scrolling. */
  justify-content: safe center;
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
  /* No top padding: the sticky search wrapper supplies its own top spacing so
     it can stick flush to the scroll-area top with nothing peeking above it. */
  padding: 0 24px 16px;
}

.home-grid {
  display: flex;
  flex-wrap: wrap;
  justify-content: center;
  gap: 4px 8px;
  max-width: 920px;
}

/* Search bar — flows with the tiles (centered as a group when there's room),
   but sticks to the top of the scroll area once the tiles scroll under it. */
.home-search-bar-wrapper {
  display: block;
  position: sticky;
  top: 0;
  z-index: 2;
  width: 100%;
  max-width: 560px;
  margin-bottom: 20px;
  padding: 16px 0 8px;
  flex-shrink: 0;
  /* Opaque backdrop so tiles scrolling underneath don't show through around
     the rounded search pill. */
  background: var(--bg-primary);
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

/* Date bar — always a single line; items are dropped by priority (see
   barHideLevel) rather than wrapping. */
.date-bar {
  display: flex;
  flex-wrap: nowrap;
  align-items: center;
  justify-content: center;
  gap: 6px;
  padding: 8px 16px;
  font-size: 11.5px;
  color: var(--text-secondary);
  border-top: 1px solid var(--border-color);
  overflow: hidden;
  white-space: nowrap;
}
.bar-sep {
  color: var(--text-secondary);
  opacity: 0.7;
  font-weight: 700;
}
.bar-item {
  color: var(--text-primary);
  white-space: nowrap;
  font-weight: 600;
}
.bar-lbl {
  font-weight: 600;
  color: var(--text-primary);
}
.bar-clock {
  font-variant-numeric: tabular-nums;
  letter-spacing: 0.03em;
}
.bar-item--btn {
  background: none;
  border: none;
  padding: 0;
  font-size: inherit;
  font-family: inherit;
  cursor: pointer;
  color: var(--text-primary);
  white-space: nowrap;
}
.bar-item--btn:hover {
  color: var(--accent-color);
}
.bar-item--btn:hover .bar-lbl {
  color: inherit;
  opacity: 1;
}

/* ── Next-zman color cue: warms up as the time approaches ── */
.zman--soon,
.zman--soon .bar-lbl {
  color: #d98324;
  opacity: 1;
}
.zman--imminent,
.zman--imminent .bar-lbl {
  color: #d64545;
  opacity: 1;
  font-weight: 700;
}
/* Pulse is reserved for deadline-critical zmanim (see CRITICAL_KEYS). Other
   imminent zmanim still turn red above, just without the flashing. */
.zman--flash {
  animation: zman-pulse 1.6s ease-in-out infinite;
}
@keyframes zman-pulse {
  0%,
  100% {
    opacity: 1;
  }
  50% {
    opacity: 0.45;
  }
}
@media (prefers-reduced-motion: reduce) {
  .zman--flash {
    animation: none;
  }
}

/* ── Next-zman popup (all times) ── */
.zman-wrap {
  position: relative;
  display: inline-flex;
}
.zman.on {
  color: var(--accent-color);
}
.zman-popup-anchor {
  position: fixed;
  z-index: 200;
}
</style>
