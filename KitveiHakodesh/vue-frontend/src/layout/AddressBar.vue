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
 * dropdown): it is open for the whole life of the address bar, showing the tab
 * list whenever there are no search results — empty input, a too-short query, or
 * a query that matched nothing. Results otherwise.
 *
 * That list is tabStore.recentLocations — places the reader has been, not tabs.
 * Selecting one navigates the CURRENT tab, because an address bar belongs to its
 * tab; Ctrl/⌘/middle-click opens a new tab instead, exactly as a browser link does.
 * Nothing in the list reflects which tabs are open, and removing a row closes
 * nothing. Back/Forward through a tab's own history lives on the title bar.
 *
 * The title bar owns when this component is shown (search mode) and reuses the
 * pane it belongs to for all navigation, so results open in the right pane.
 */
import { ref, computed, nextTick, watch } from 'vue'
import { useIntervalFn } from '@vueuse/core'
import { IconSearch20Regular } from '@iconify-prerendered/vue-fluent'
import HomeSearchDropdown from '@/features/home/HomeSearchDropdown.vue'
import { useHomeSearch, type FileSearchResult } from '@/features/home/useHomeSearch'
import { addinDisplayTitle } from '@/features/local-file-search/otzariaAddins'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { useAppShellPane } from '@/composables/useAppShellPane'
import { restoreLocalFile, triggerHbDownload } from '@/webview-host/bridge'
import { useLocalFileStore } from '@/stores/localFileStore'
import { useTabStore, type Tab } from '@/stores/tabStore'
import { sortedByRecency } from '@/stores/recentLocations'
import { useSettingsStore } from '@/stores/settingsStore'
import { useHebrewBooksHistoryStore } from '@/stores/hebrewBooksHistoryStore'
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
  'תוספים: עבור תוספי אוצריא',
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

// ── Recents list (dropdown fallback content) ──────────────────────────────────
// Shown whenever the search has nothing to display: empty/short query, or a query
// with no matches. Results otherwise.
//
// These are LOCATIONS, not tabs — places the reader has been, most recent first.
// Selecting one navigates the current tab, exactly as an address bar does; a
// Ctrl-click opens a new tab instead. Nothing here switches tabs or reflects which
// tabs are open, and removing a row closes nothing.
const visibleLocations = computed(() => sortedByRecency(tabStore.recentLocations))
const dropdownTabs = computed(() => (hasAnyResults() ? [] : visibleLocations.value))

// An empty panel is just a floating blank rectangle, so render nothing at all
// until there is something in it. The loading flags count as content: a search in
// flight shows spinners, and hiding the panel until the first row lands would
// make it flicker in and out mid-type.
const hasDropdownContent = computed(
  () => dropdownTabs.value.length > 0 || hasAnyResults() || isLoadingAny(),
)

// The one condition the panel renders on. The field's merged styling reads the
// same value, so it can never square its bottom corners with no panel attached
// to them.
const isPanelVisible = computed(() => isDropdownOpen.value && hasDropdownContent.value)

// The address bar belongs to the current tab, so its rows navigate IN PLACE —
// the same rule every other row here follows, and the same one a browser applies
// to a link. Ctrl/⌘/middle-click is the single exception and opens a new tab.
async function onSelectLocation(id: string, openInNewTab = false) {
  const patch = tabStore.locationPatch(id)
  if (!patch) return
  // A file location carries only its path — the virtual host registration that
  // served it lives and dies with the tab that opened it, so any URL the target
  // tab still holds belongs to a different document. Clear it, apply the patch,
  // and re-register the file (restoreTab → restoreLocalFile/restoreHbPdf writes
  // the fresh localFileVirtualUrl onto the tab), same as the home-page recents
  // and native dropdown paths.
  const targetTabId = openInNewTab
    ? pane.openTab({ ...patch, localFileVirtualUrl: undefined } as Omit<Tab, 'id'>).id
    : pane.activeTabId.value
  if (!openInNewTab) pane.updateActiveTab({ ...patch, localFileVirtualUrl: undefined })
  close()
  if (
    (patch.localFilePath || patch.localFileHbBookId) &&
    (patch.route === '/pdf-view' || patch.route === '/html-view')
  ) {
    await localFileStore.restoreTab(targetTabId, true)
  }
}

function onForgetLocation(id: string) {
  // Keep the dropdown open — pruning the list is a batch gesture. This only
  // forgets the location; any tab showing that document is untouched.
  tabStore.forgetLocation(id)
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
  // Flush against the field's bottom edge, no gap and no overlap: in merged mode
  // the field drops its bottom border entirely, so there is no seam line left to
  // cover and the two backgrounds simply continue into each other. Floored
  // because getBoundingClientRect returns fractional values at non-integral zoom
  // or DPI, and a fractional top would leave a hairline of the page showing
  // through the join — rounding down overlaps by a subpixel instead.
  anchorTop.value = Math.floor(rect.bottom)
  anchorLeft.value = anchor.left
  anchorRight.value = window.innerWidth - anchor.right
  maxHeight.value = Math.min(MAX_DROPDOWN_HEIGHT, Math.max(120, window.innerHeight - rect.bottom - 12))
}

function onInput() {
  // Typing releases the arrow-key pause below — fresh results may reshuffle now.
  resumeSearch()
  computeAnchor()
  isDropdownOpen.value = true
}

function onFocus() {
  computeAnchor()
  isDropdownOpen.value = true
}

function onKeydown(e: KeyboardEvent) {
  // Combobox model: focus stays here; arrows/paging move the dropdown's
  // highlight, and Enter WITH a highlight activates it. While the user is
  // arrowing, pause the async sources so late results don't reshuffle the list
  // under the highlight (typing resumes them in onInput).
  if (isDropdownOpen.value && dropdownRef.value?.onSearchInputKeydown(e)) {
    pauseSearch()
    return
  }
  if (e.code === 'Enter') {
    e.preventDefault()
    launchFullTextSearch()
    return
  }
  if (e.code === 'Escape') {
    e.preventDefault()
    close()
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

async function onSelectFile(item: FileSearchResult, openInNewTab = false) {
  // Dev opens local files too now (restoreLocalFile → service capability + /khs-file proxy).
  const { fullPath, fileName } = item
  const extension = fileName.substring(fileName.lastIndexOf('.')).toLowerCase()
  const dotIndex = fileName.lastIndexOf('.')
  const titleWithoutExtension = dotIndex > 0 ? fileName.substring(0, dotIndex) : fileName

  const isHtmlLike = extension === '.htm' || extension === '.html'
  const route = extension === '.txt' ? '/txt-view' : isHtmlLike ? '/html-view' : '/pdf-view'

  // The addin name is known synchronously, so the placeholder tab gets it too —
  // otherwise a Ctrl/⌘-clicked addin reads "index" until restoreLocalFile returns.
  const displayTitle = item.addinName ? addinDisplayTitle(item.addinName) : titleWithoutExtension

  // Capture the target tab id up front (a new tab for Ctrl/⌘-click, else the
  // active tab) and patch it by id — restoreLocalFile awaits, and the active tab
  // may change during that await.
  const targetTabId = openInNewTab
    ? pane.openTab({ route, title: displayTitle }).id
    : pane.activeTabId.value

  if (extension === '.txt') {
    tabStore.updateTab(targetTabId, {
      route: '/txt-view',
      title: displayTitle,
      localFileName: fileName,
      localFilePath: fullPath,
      localFileVirtualUrl: undefined,
      isOtzariaAddin: false,
    })
    close()
    return
  }

  const restored = await restoreLocalFile(fullPath)
  if (!restored?.url) { close(); return }
  // Route by what is actually served (dev Word docs may render to HTML via the fallback).
  const servedRoute =
    restored.kind === 'html' ? '/html-view' : restored.kind === 'pdf' ? '/pdf-view' : route

  // An Otzaria addin is presented by its addin name, and the tab is flagged so
  // HtmlViewPage activates the addin bridge — same as the file-search page.
  // isOtzariaAddin is written unconditionally (updateTab merges, so omitting the
  // key would leave a previous addin's `true` on this tab) and gated on the
  // served route, since only /html-view ever reads it.
  tabStore.updateTab(targetTabId, {
    route: servedRoute as '/html-view' | '/pdf-view',
    title: displayTitle,
    localFileName: fileName,
    localFilePath: fullPath,
    localFileVirtualUrl: restored.url,
    isOtzariaAddin: !!item.addinName && servedRoute === '/html-view',
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
  <div
    ref="wrapperRef"
    class="address-bar"
    :class="{ 'is-merged': isPanelVisible }"
    @click.stop="inputRef?.focus()"
  >
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
      v-if="searchQuery.trim()"
      class="address-bar__button"
      tabindex="-1"
      title="חיפוש תוכן במאגר (Enter)"
      @click.stop="launchFullTextSearch()"
    >
      <IconSearch20Regular />
    </button>
    <HomeSearchDropdown
      v-if="isPanelVisible"
      ref="dropdownRef"
      merge-with-anchor
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
      @select-catalog-book="onSelectCatalogBook"
      @select-catalog-toc="onSelectCatalogToc"
      @select-hebrew-book="onSelectHebrewBook"
      @select-file="onSelectFile"
      @select-tab="onSelectLocation"
      @forget-tab="onForgetLocation"
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

/* ── Merged with the dropdown (browser-omnibox seam) ──────────────────────────
   While the panel is attached below, the field stops being a self-contained box
   and becomes the top of one continuous slab.

   Everything here removes a seam:
   - The bottom border AND the accent underline from :focus-within go. That
     underline is the single most visible seam — it would draw a bright accent
     line straight through the middle of the merged surface.
   - The bottom corners square off to meet the panel's square top ones.
   - The background becomes flat --bg-secondary, the panel's exact surface
     colour. The resting/focused states wash --text-primary over the title bar
     to lift the field off the chrome, which is right for a standalone field but
     is precisely what made the field read LIGHTER than the panel below it.

   Placed after :focus-within so it wins on equal specificity — the panel is only
   ever visible while the field holds focus, so that rule is always in play. */
.address-bar.is-merged {
  background: var(--bg-secondary);
  border-bottom: none;
  border-bottom-left-radius: 0;
  border-bottom-right-radius: 0;
  box-shadow: none;
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
.address-bar__field::-webkit-search-cancel-button {
  filter: grayscale(1) opacity(0.4);
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
