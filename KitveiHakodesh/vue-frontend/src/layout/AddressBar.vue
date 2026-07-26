<script setup lang="ts">
/**
 * AddressBar — the editable search field hosted inside AppTitleBar (an
 * Explorer-style address bar). It reuses the exact home-page search engine
 * (useHomeSearch) and the
 * home-page results dropdown (HomeSearchDropdown), so typing here behaves like
 * typing on the home page: instant catalog matches, debounced HebrewBooks/file
 * results, and Enter → full-text search in the active tab.
 *
 * The dropdown doubles as the pane's tab list (replacing the old title-bar tab
 * dropdown): it is open for the whole life of the address bar, showing the
 * open tabs — with the recently-opened documents (the home-page tile
 * collection) below them — whenever there are no search results: empty input,
 * a too-short query, or a query that matched nothing. Results otherwise.
 *
 * The title bar owns when this component is shown (search mode) and reuses the
 * pane it belongs to for all navigation, so results open in the right pane.
 */
import { ref, computed, nextTick, watch } from 'vue'
import { useIntervalFn } from '@vueuse/core'
import { IconSearch20Regular, IconDismiss20Regular } from '@iconify-prerendered/vue-fluent'
import HomeSearchDropdown from '@/features/home/HomeSearchDropdown.vue'
import { useHomeSearch } from '@/features/home/useHomeSearch'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { useAppShellPane } from '@/composables/useAppShellPane'
import { restoreLocalFile, triggerHbDownload } from '@/webview-host/bridge'
import { useLocalFileStore } from '@/stores/localFileStore'
import { useTabStore } from '@/stores/tabStore'
import { useSettingsStore } from '@/stores/settingsStore'
import { useHebrewBooksHistoryStore } from '@/stores/hebrewBooksHistoryStore'
import { useRecentlyOpenedStore, type RecentlyOpenedEntry } from '@/stores/recentlyOpenedStore'
import { getHbPdfUrl, type HebrewBook } from '@/features/hebrewbooks/hebrewBooksCatalog'
import type { TocFsItem } from '@/features/book-catalog/useBookCatalogSearch'

const props = defineProps<{ paneId: 1 | 2 }>()
const emit = defineEmits<{ close: [] }>()

const pane = useAppShellPane(props.paneId)
const localFileStore = useLocalFileStore()
const tabStore = useTabStore()
const settingsStore = useSettingsStore()
const hebrewBooksHistoryStore = useHebrewBooksHistoryStore()

const searchQuery = ref('')
const wrapperRef = ref<HTMLElement | null>(null)
const inputRef = ref<HTMLInputElement | null>(null)
const isDropdownOpen = ref(false)
const dropdownRef = ref<InstanceType<typeof HomeSearchDropdown> | null>(null)
const dropdownEl = computed(() => dropdownRef.value?.element ?? null)

// ── Animated placeholder (same phrases as the home search bar) ────────────────
const PLACEHOLDERS = [
  'חיפוש מהיר בכל המאגרים...',
  'לחץ אנטר לחיפוש תוכן במאגר',
  'הקלד חופשי לחיפוש ספר או קובץ',
  'היברו בוקס: שבת',
  'קובץ: ברכות',
]
const placeholder = ref(PLACEHOLDERS[0]!)
let phraseIdx = 0, charIdx = 0, pauseTicks = 0
const { pause: pauseTyping, resume: resumeTyping } = useIntervalFn(() => {
  if (pauseTicks > 0) { pauseTicks--; return }
  const target = PLACEHOLDERS[phraseIdx]!
  if (charIdx < target.length) {
    placeholder.value = target.slice(0, ++charIdx)
  } else {
    pauseTicks = 12
    phraseIdx = (phraseIdx + 1) % PLACEHOLDERS.length
    charIdx = 0
  }
}, 80)
watch(searchQuery, (v) => (v ? pauseTyping() : resumeTyping()))

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
  clearResults,
  pause: pauseSearch,
  resume: resumeSearch,
} = useHomeSearch(searchQuery)

useDropdownClose(wrapperRef, () => close(), { ignore: [dropdownEl] })

// ── Tab list + recents (dropdown fallback content) ────────────────────────────
// The dropdown shows the pane's open tabs — with the recently-opened documents
// (the home-page tile collection) below them — whenever the search has nothing
// to show: empty/short query or a query with no matches. Results otherwise.
// Ordered most-recently-active first (pane.mruTabs) — a display order for this
// list only; the pane's real tab order and the chrome tab strip are untouched.
const visibleTabs = computed(() => pane.mruTabs.value.filter((t) => t.route !== '/settings'))
const dropdownTabs = computed(() => (hasAnyResults() ? [] : visibleTabs.value))

const recentlyOpenedStore = useRecentlyOpenedStore()
const recentEntries = ref<RecentlyOpenedEntry[]>([])
recentlyOpenedStore.getList().then((list) => { recentEntries.value = list })
const dropdownRecentEntries = computed(() => (hasAnyResults() ? [] : recentEntries.value))

function onSelectTab(id: string) {
  pane.switchTab(id)
  close()
}

function onCloseTab(id: string) {
  // Keep the dropdown open — closing tabs from the list is a batch gesture.
  pane.closeTab(id)
}

// Recents navigate the CURRENT tab (like search results); a Ctrl/⌘/middle-click
// opens a new tab instead (openInNewTab, mirroring HomePage). File entries
// follow the tabMirror flow: place the tab, then let the shared history-restore
// (openFromHistory) fill it in.
function onSelectRecent(entry: RecentlyOpenedEntry, openInNewTab = false) {
  if (entry.route === '/book-view' && entry.bookId !== undefined) {
    pane.openOrUpdateActiveTab({ route: '/book-view', title: entry.title, bookId: entry.bookId }, openInNewTab)
  } else {
    localFileStore.openFromHistory(entry, openInNewTab)
  }
  close()
}

// ── Dropdown anchor (positioned under the field, like the home page) ──────────
// Width rule: on a narrow ("android-width") shell the dropdown fills the whole
// shell; otherwise it matches the input. The SHELL's rect is what's measured —
// not the viewport — because in split view each shell is only part of the window.
const NARROW_SHELL_WIDTH = 600
// The dropdown never grows past this — enough for a comfortable list of rows
// while leaving the field feeling like an address bar, not a full-height panel.
// It still shrinks below this to fit the viewport when space is tight.
const MAX_DROPDOWN_HEIGHT = 440

const anchorTop = ref(0)
const anchorLeft = ref(0)
const anchorRight = ref(0)
const maxHeight = ref(300)

function computeAnchor() {
  if (!wrapperRef.value) return
  const rect = wrapperRef.value.getBoundingClientRect()
  const shellRect = wrapperRef.value.closest('.app-shell')?.getBoundingClientRect()
  const anchor = shellRect && shellRect.width <= NARROW_SHELL_WIDTH ? shellRect : rect
  anchorTop.value = rect.bottom + 6
  anchorLeft.value = anchor.left
  anchorRight.value = window.innerWidth - anchor.right
  maxHeight.value = Math.min(MAX_DROPDOWN_HEIGHT, Math.max(120, window.innerHeight - rect.bottom - 12))
}

function onInput() {
  computeAnchor()
  isDropdownOpen.value = true
}

function onFocus() {
  computeAnchor()
  isDropdownOpen.value = true
}

function onKeydown(e: KeyboardEvent) {
  if (e.code === 'Enter') {
    e.preventDefault()
    launchFullTextSearch()
    return
  }
  if (e.code === 'Escape') {
    e.preventDefault()
    close()
    return
  }
  if (!isDropdownOpen.value) return
  if (e.code === 'ArrowDown' || e.code === 'ArrowUp') {
    e.preventDefault()
    dropdownRef.value?.focus()
  }
}

function launchFullTextSearch() {
  const query = searchQuery.value.trim()
  if (!query) return
  pane.updateActiveTab({ route: '/search', title: `חיפוש: ${query}`, searchQuery: query })
  close()
}

function close() {
  isDropdownOpen.value = false
  clearResults()
  searchQuery.value = ''
  emit('close')
}

// ── Result selection — mirrors HomePage, routed through this pane ─────────────
// A Ctrl/⌘/middle-click opens a new tab (openInNewTab); a plain click navigates
// the active tab in place. For the async cases (HebrewBook, File) the target tab
// id is captured up front and patched by id, because the awaited work can change
// which tab is active.
function onSelectCatalogBook(bookId: number, bookTitle: string, openInNewTab = false) {
  pane.openOrUpdateActiveTab({ route: '/book-view', title: bookTitle, bookId }, openInNewTab)
  close()
}

function onSelectCatalogToc(item: TocFsItem, openInNewTab = false) {
  pane.openOrUpdateActiveTab({
    route: '/book-view',
    title: item.book.title,
    bookId: item.book.id,
    openTocEntryId: item.tocEntryId,
    openTocLineIndex: item.tocLineIndex ?? undefined,
  }, openInNewTab)
  close()
}

function onSelectHebrewBook(book: HebrewBook, openInNewTab = false) {
  hebrewBooksHistoryStore.trackAccess(book)
  // Download lifecycle is tab-id-driven — for a Ctrl/⌘-click open a fresh
  // placeholder tab and target its id.
  const tabId = openInNewTab
    ? pane.openTab({ route: '/pdf-view', title: book.title }).id
    : pane.activeTabId.value
  localFileStore.startHbDownload(book.title, tabId)
  triggerHbDownload(
    String(book.id),
    book.title,
    getHbPdfUrl(book.id),
    tabId,
    settingsStore.hebrewBooksLocalFolder || undefined,
    navigator.onLine,
  ).catch(() => {})
  close()
}

async function onSelectFile(fullPath: string, fileName: string, openInNewTab = false) {
  // Dev opens local files too now (restoreLocalFile → service capability + /khs-file proxy).
  const extension = fileName.substring(fileName.lastIndexOf('.')).toLowerCase()
  const dotIndex = fileName.lastIndexOf('.')
  const titleWithoutExtension = dotIndex > 0 ? fileName.substring(0, dotIndex) : fileName

  const isHtmlLike = extension === '.htm' || extension === '.html'
  const route = extension === '.txt' ? '/txt-view' : isHtmlLike ? '/html-view' : '/pdf-view'

  // Capture the target tab id up front (a new tab for Ctrl/⌘-click, else the
  // active tab) and patch it by id — restoreLocalFile awaits, and the active tab
  // may change during that await.
  const targetTabId = openInNewTab
    ? pane.openTab({ route, title: titleWithoutExtension }).id
    : pane.activeTabId.value

  if (extension === '.txt') {
    tabStore.updateTab(targetTabId, {
      route: '/txt-view',
      title: titleWithoutExtension,
      localFileName: fileName,
      localFilePath: fullPath,
      localFileVirtualUrl: undefined,
    })
    close()
    return
  }

  const restored = await restoreLocalFile(fullPath)
  if (!restored?.url) { close(); return }
  // Route by what is actually served (dev Word docs may render to HTML via the fallback).
  const servedRoute =
    restored.kind === 'html' ? '/html-view' : restored.kind === 'pdf' ? '/pdf-view' : route

  tabStore.updateTab(targetTabId, {
    route: servedRoute as '/html-view' | '/pdf-view',
    title: titleWithoutExtension,
    localFileName: fileName,
    localFilePath: fullPath,
    localFileVirtualUrl: restored.url,
  })
  close()
}

// Focus the field as soon as it mounts (search mode was just entered) and open
// the dropdown right away — with an empty query it shows the tab list.
nextTick(() => {
  inputRef.value?.focus()
  computeAnchor()
  isDropdownOpen.value = true
})
</script>

<template>
  <div ref="wrapperRef" class="address-bar" @click.stop="inputRef?.focus()">
    <input
      ref="inputRef"
      v-model="searchQuery"
      class="address-bar__field"
      type="search"
      :placeholder="placeholder"
      autocomplete="off"
      @focus="onFocus"
      @input="onInput"
      @keydown="onKeydown"
    />
    <button
      class="address-bar__button"
      tabindex="-1"
      :title="searchQuery.trim() ? 'חיפוש תוכן במאגר (Enter)' : 'סגור'"
      @click.stop="searchQuery.trim() ? launchFullTextSearch() : close()"
    >
      <IconSearch20Regular v-if="searchQuery.trim()" />
      <IconDismiss20Regular v-else />
    </button>
    <HomeSearchDropdown
      v-if="isDropdownOpen"
      ref="dropdownRef"
      :catalog-results="catalogResults"
      :catalog-toc-results="catalogTocResults"
      :hebrew-books-results="hebrewBooksResults"
      :file-results="fileResults"
      :source-priority="sourcePriority"
      :is-loading-catalog-toc="isLoadingCatalogToc"
      :is-loading-hebrew-books="isLoadingHebrewBooks"
      :is-loading-files="isLoadingFiles"
      :anchor-top="anchorTop"
      :anchor-left="anchorLeft"
      :anchor-right="anchorRight"
      :max-height="maxHeight"
      :tabs="dropdownTabs"
      :active-tab-id="pane.activeTabId.value"
      :recent-entries="dropdownRecentEntries"
      @select-catalog-book="onSelectCatalogBook"
      @select-catalog-toc="onSelectCatalogToc"
      @select-hebrew-book="onSelectHebrewBook"
      @select-file="onSelectFile"
      @select-tab="onSelectTab"
      @close-tab="onCloseTab"
      @select-recent="onSelectRecent"
      @dropdown-focused="pauseSearch"
      @dropdown-blurred="resumeSearch"
    />
  </div>
</template>

<style scoped>
/* Windows Explorer address-bar look: a flat, near-rectangular field that fills
   the title area and blends into the title-bar chrome (--bg-secondary) with a
   subtle frame — not a stand-out filled search pill. On focus only the BOTTOM
   border lights up in the accent color (an underline), matching .bar-title's
   resting geometry so entering search mode causes no visual jump. Trailing icon
   button = search when there's a query, dismiss when empty. */
.address-bar {
  display: flex;
  align-items: center;
  width: 100%;
  min-width: 0;
  height: 24px;
  padding: 0 2px 0 6px;
  background: color-mix(in srgb, var(--text-primary) 3%, transparent);
  border: 1px solid var(--border-color);
  border-radius: 6px;
  cursor: text;
}
.address-bar:focus-within {
  background: color-mix(in srgb, var(--text-primary) 6%, transparent);
  /* Highlight the underline only, leaving the other three sides as the quiet
     frame — a bottom-accent input, not a fully-outlined box. */
  border-bottom-color: var(--accent-color);
  box-shadow: inset 0 -1px 0 0 var(--accent-color);
}
.address-bar__field {
  flex: 1;
  min-width: 0;
  height: 100%;
  background: none;
  border: none;
  outline: none;
  font-size: 13px;
  color: var(--text-primary);
  direction: rtl;
  /* Explorer draws no rounded search affordance inside the box */
  padding: 0;
}
.address-bar__field::placeholder {
  color: var(--text-secondary);
  opacity: 0.7;
}
/* Hide the native search-type clear (×) — we render our own trailing button. */
.address-bar__field::-webkit-search-cancel-button {
  display: none;
}
.address-bar__button {
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  width: 20px;
  height: 20px;
  border-radius: 3px;
  color: var(--text-secondary);
}
.address-bar__button svg {
  width: 16px;
  height: 16px;
}
.address-bar__button:hover {
  color: var(--text-primary);
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}
</style>
