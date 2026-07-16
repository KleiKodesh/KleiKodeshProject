<script setup lang="ts">
/**
 * AddressBar — the editable search field hosted inside AppTitleBar (an
 * Explorer-style address bar). It reuses the exact home-page search engine
 * (useHomeSearch) and the
 * home-page results dropdown (HomeSearchDropdown), so typing here behaves like
 * typing on the home page: instant catalog matches, debounced HebrewBooks/file
 * results, and Enter → full-text search in the active tab.
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
import { isHosted } from '@/webview-host/seforimDb'
import { restoreLocalFile, triggerHbDownload } from '@/webview-host/bridge'
import { useLocalFileStore } from '@/stores/localFileStore'
import { useSettingsStore } from '@/stores/settingsStore'
import { useHebrewBooksHistoryStore } from '@/stores/hebrewBooksHistoryStore'
import { getHbPdfUrl, type HebrewBook } from '@/features/hebrewbooks/hebrewBooksCatalog'
import type { TocFsItem } from '@/features/book-catalog/useBookCatalogSearch'

const props = defineProps<{ paneId: 1 | 2 }>()
const emit = defineEmits<{ close: [] }>()

const pane = useAppShellPane(props.paneId)
const localFileStore = useLocalFileStore()
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
  isLoadingAny,
  clearResults,
  pause: pauseSearch,
  resume: resumeSearch,
} = useHomeSearch(searchQuery)

useDropdownClose(wrapperRef, () => close(), { ignore: [dropdownEl] })

// Open the dropdown as async sources resolve results after the debounce.
watch([catalogTocResults, hebrewBooksResults, fileResults], () => {
  if ((searchQuery.value ?? '').trim().length >= 2) {
    if (hasAnyResults()) {
      isDropdownOpen.value = true
    } else if (!isLoadingAny()) {
      isDropdownOpen.value = false
    }
  }
})

// ── Dropdown anchor (positioned under the field, like the home page) ──────────
const anchorTop = ref(0)
const anchorLeft = ref(0)
const anchorRight = ref(0)
const maxHeight = ref(300)

function computeAnchor() {
  if (!wrapperRef.value) return
  const rect = wrapperRef.value.getBoundingClientRect()
  anchorTop.value = rect.bottom + 6
  anchorLeft.value = rect.left
  anchorRight.value = window.innerWidth - rect.right
  maxHeight.value = Math.max(120, window.innerHeight - rect.bottom - 12)
}

function onInput() {
  const hasQuery = (searchQuery.value ?? '').trim().length >= 2
  if (hasQuery) computeAnchor()
  isDropdownOpen.value = hasQuery && (hasAnyResults() || isLoadingAny())
}

function onFocus() {
  if (hasAnyResults()) {
    computeAnchor()
    isDropdownOpen.value = true
  }
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
function onSelectCatalogBook(bookId: number, bookTitle: string) {
  pane.updateActiveTab({ route: '/book-view', title: bookTitle, bookId })
  close()
}

function onSelectCatalogToc(item: TocFsItem) {
  pane.updateActiveTab({
    route: '/book-view',
    title: item.book.title,
    bookId: item.book.id,
    openTocEntryId: item.tocEntryId,
    openTocLineIndex: item.tocLineIndex ?? undefined,
  })
  close()
}

function onSelectHebrewBook(book: HebrewBook) {
  hebrewBooksHistoryStore.trackAccess(book)
  const tabId = pane.activeTabId.value
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

async function onSelectFile(fullPath: string, fileName: string) {
  if (!isHosted) { close(); return }
  const extension = fileName.substring(fileName.lastIndexOf('.')).toLowerCase()
  const dotIndex = fileName.lastIndexOf('.')
  const titleWithoutExtension = dotIndex > 0 ? fileName.substring(0, dotIndex) : fileName

  if (extension === '.txt') {
    pane.updateActiveTab({
      route: '/txt-view',
      title: titleWithoutExtension,
      localFileName: fileName,
      localFilePath: fullPath,
      localFileVirtualUrl: undefined,
    })
    close()
    return
  }

  const isHtmlLike = extension === '.htm' || extension === '.html'
  const route = isHtmlLike ? '/html-view' : '/pdf-view'
  const restored = await restoreLocalFile(fullPath)
  if (!restored?.url) { close(); return }

  pane.updateActiveTab({
    route: route as '/html-view' | '/pdf-view',
    title: titleWithoutExtension,
    localFileName: fileName,
    localFilePath: fullPath,
    localFileVirtualUrl: restored.url,
  })
  close()
}

// Focus the field as soon as it mounts (search mode was just entered).
nextTick(() => inputRef.value?.focus())
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
      :min-width="320"
      @select-catalog-book="onSelectCatalogBook"
      @select-catalog-toc="onSelectCatalogToc"
      @select-hebrew-book="onSelectHebrewBook"
      @select-file="onSelectFile"
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
