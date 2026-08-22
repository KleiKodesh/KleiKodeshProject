<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount, nextTick, watch } from 'vue'
import { useIntervalFn } from '@vueuse/core'
import { useBookCatalog } from './useBookCatalog'
import BookCatalogTitleBar from './BookCatalogTitleBar.vue'
import BookCatalogViewTree from './BookCatalogView.Tree.vue'
import BookCatalogViewTiles from './BookCatalogView.Tiles.vue'
import BookCatalogViewList from './BookCatalogView.List.vue'
import BookCatalogSearch from './BookCatalogSearch.vue'
import LoadingAnimation from '@/components/LoadingAnimation.vue'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { useTabStore } from '@/stores/tabStore'
import { useSettingsStore, type BooksView } from '@/stores/settingsStore'
import { storeToRefs } from 'pinia'
import type { CategoryNode } from '@/features/book-catalog/bookCatalogTree'
import type { BookRow } from '@/webview-host/queries.types'
import type { TocFsItem } from './useBookCatalogSearch'
import { getDiagnostics } from '@/webview-host/bridge'
import { copyTextToClipboard } from '@/utils/clipboard'
import type { ComponentPublicInstance } from 'vue'

const paneNavigation = usePaneNavigation()
const tabStore = useTabStore()
const {
  loading,
  error,
  path,
  searchQuery,
  isSearching,
  treeItems,
  searchItems,
  tocSearching,
  load,
  enter,
  navigateTo,
  navigateToSibling,
} = useBookCatalog()

// Persisted in settingsStore alongside every other display preference — it is
// app-wide, not per-tab, so it needs no load-on-mount step.
const { booksView: view } = storeToRefs(useSettingsStore())

// This tab's id, captured at mount. The catalog is NOT a singleton (multiple
// '/books' tabs can coexist), so we persist against the specific tab, never the
// "active" tab, which may change after we navigate away.
const booksTabId = paneNavigation.activeTabId

// ── Query persistence across navigation ────────────────────────────────────────
// Holds the query on the tab so leaving and coming back returns to the results
// rather than to the browse view. The VSTO host seed (catalogQuery) wins when
// present — it is a fresh one-shot request, not a restored session — and its watch
// runs synchronously at setup (immediate), before this onMounted, so only restore
// when searchQuery is still empty.
onMounted(() => {
  if (!searchQuery.value) {
    const saved = paneNavigation.activeTab.booksSearchQuery
    if (saved) searchQuery.value = saved
  }
})

// On unmount, mirror the file-search rule:
//  • Tab still '/books' (tab switch, or a NEW tab opened via Ctrl+click) → save
//    the query so the results restore when the user returns to this tab.
//  • Tab navigated in place to a book (route changed to '/book-view') → clear it.
onBeforeUnmount(() => {
  const tab = tabStore.tabs.find((t) => t.id === booksTabId)
  const stillBooks = tab?.route === '/books'
  tabStore.updateTab(booksTabId, {
    booksSearchQuery: stillBooks ? searchQuery.value || undefined : undefined,
  })
})

// Query pushed from the VSTO host ("חיפוש ספר בכתבי הקודש" context menu) arrives on
// the tab as catalogQuery. Watch it (immediate) so it works both on first mount and
// when the host re-targets an already-open catalog tab. Consume once and clear it so
// the search doesn't re-fire when the user returns to this tab. Mutating searchQuery
// is enough — useBookCatalogSearch watches it and runs the search reactively.
watch(
  () => paneNavigation.activeTab.catalogQuery,
  (seed) => {
    if (!seed) return
    searchQuery.value = seed
    paneNavigation.updateActiveTab({ catalogQuery: undefined })
  },
  { immediate: true },
)

// ── Diagnostics (auto-runs when a bitness-mismatch error is detected) ─────────

const diagData = ref<Record<string, string> | null>(null)
const diagLoading = ref(false)

function isBitnessMismatch(msg: string | null) {
  if (!msg) return false
  return msg.includes('0x8007000B') || msg.toLowerCase().includes('incorrect format')
}

watch(error, async (msg) => {
  if (!isBitnessMismatch(msg)) return
  diagLoading.value = true
  diagData.value = null
  const result = await getDiagnostics()
  diagLoading.value = false
  diagData.value = result
})

function copyDiagnostics() {
  if (!diagData.value) return
  const lines = Object.entries(diagData.value).map(([k, v]) => k + ': ' + v)
  void copyTextToClipboard(lines.join('\n'))
}
const activeViewComponent = computed(() => {
  if (view.value === 'tree') return BookCatalogViewTree
  if (view.value === 'tiles') return BookCatalogViewTiles
  return BookCatalogViewList
})

const activeViewProps = computed(() => (view.value === 'tree' ? {} : { items: treeItems.value }))

type ActiveViewInstance = ComponentPublicInstance & {
  /** List/tiles forward the search input's keys and keep the caret in the field. */
  onSearchInputKeydown?: (event: KeyboardEvent) => boolean
  /** The tree view instead takes focus, having its own tree keyboard model. */
  focusContainer?: () => void
  reset?: () => void
}

const activeViewRef = ref<ActiveViewInstance | null>(null)
const searchResultsRef = ref<InstanceType<typeof BookCatalogSearch> | null>(null)
const searchInputRef = ref<HTMLInputElement | null>(null)

// One combobox for the whole page: DOM focus stays in the search input and the
// arrows move a highlight through whichever list is on screen — the search results
// while a query is running, the browse view otherwise. The caret never leaves the
// field, so the user can keep typing at any point.
function onSearchKeydown(event: KeyboardEvent) {
  // Escape first: it means "leave the field", whoever else might want the key.
  if (event.code === 'Escape' && !searchQuery.value) {
    editingSearch.value = false
    return
  }

  const list = isSearching.value ? searchResultsRef.value : activeViewRef.value
  if (list?.onSearchInputKeydown?.(event)) return

  // An Enter the list did not consume (nothing highlighted) falls through to the
  // single-result shortcut.
  if (isSearching.value && event.code === 'Enter') {
    onSearchEnter()
    return
  }

  // The tree view is the exception: it is a real tree with its own focus and
  // expand/collapse keys, not a flat list a highlight can walk, so it cannot be
  // driven from here. There the arrows still hand focus over to it.
  if (event.code === 'ArrowUp' || event.code === 'ArrowDown' || event.code === 'Tab') {
    if (!activeViewRef.value?.focusContainer) return
    event.preventDefault()
    activeViewRef.value.focusContainer()
  }
}

const PLACEHOLDERS = ['בראשית פרק ד', 'בבלי ברכות דף יד', 'רמב"ם משנה תורה']
const placeholder = ref(PLACEHOLDERS[0]!)
let phraseIdx = 0,
  charIdx = 0,
  pauseTicks = 0

const { pause: pauseTyping, resume: resumeTyping } = useIntervalFn(() => {
  if (pauseTicks > 0) {
    pauseTicks--
    return
  }
  const target = PLACEHOLDERS[phraseIdx]!
  if (charIdx < target.length) {
    placeholder.value = target.slice(0, ++charIdx)
  } else {
    pauseTicks = 12
    phraseIdx = (phraseIdx + 1) % PLACEHOLDERS.length
    charIdx = 0
  }
}, 80)

watch(searchQuery, (val) => (val ? pauseTyping() : resumeTyping()))

// ── Which face the address bar shows ─────────────────────────────────────────
// The path is the resting state: the bar is a breadcrumb that becomes a text field
// while the user is typing in it, like Explorer's.
//
// `showSearch` is DERIVED, never assigned — the field shows while the user is
// editing OR whenever a query is live, because results are on screen then and a
// path display would misdescribe what they are looking at. Deriving the second
// half is what keeps the two from drifting: opening a result in a NEW tab leaves
// this page mounted with its query intact, and a handler that merely flipped a
// flag would strand those results under a breadcrumb.
const editingSearch = ref(false)
const showSearch = computed(() => editingSearch.value || !!searchQuery.value)

function openSearch() {
  editingSearch.value = true
  // The user asked for the field, so put the caret in it — after the v-if has
  // actually mounted the input.
  nextTick(() => searchInputRef.value?.focus())
}

// Leaving the field returns the bar to the path — but only when there is no query.
// A live query keeps the field regardless (see showSearch), so this is just the
// empty-field case.
function onSearchBlur() {
  if (!searchQuery.value) editingSearch.value = false
}

// Opening anything — a folder, a book — puts the user back on the shelves, so the
// bar goes back to showing where they are.
function onEnterFolder(node: CategoryNode) {
  editingSearch.value = false
  enter(node)
}

// Home from inside the search field: back to the root AND out of search, so the
// bar returns to the path rather than sitting on an emptied field. navigateTo
// clears the query; the face has to be switched here, since the page owns it.
function onGoHome() {
  editingSearch.value = false
  navigateTo(0)
}

function setView(v: BooksView) {
  // Assigning the store ref persists it — settingsStore watches and writes.
  view.value = v
}

onMounted(() => {
  load()
  // Open with the field ready to type in — the catalog is a place you usually
  // arrive at looking for something. openSearch (not a bare focus call) because
  // the input only exists while showSearch is true: the default is the crumbs, so
  // the field has to be swapped in before there is anything to focus.
  openSearch()
})

function onSelectBook(book: BookRow, openInNewTab = false) {
  editingSearch.value = false
  paneNavigation.openOrUpdateActiveTab(
    {
      title: book.title,
      route: '/book-view',
      bookId: book.id,
    },
    openInNewTab,
  )
}
function onSelectToc(item: TocFsItem, openInNewTab = false) {
  editingSearch.value = false
  paneNavigation.openOrUpdateActiveTab(
    {
      title: item.book.title,
      route: '/book-view',
      bookId: item.book.id,
      openTocEntryId: item.tocEntryId,
      openTocLineIndex: item.tocLineIndex ?? undefined,
    },
    openInNewTab,
  )
}
function onSearchEnter() {
  if (isSearching.value && searchItems.value.length === 1) onSelectBook(searchItems.value[0]!.book)
}
</script>

<template>
  <div class="books-page">
    <BookCatalogTitleBar
      :view="view"
      :path="path"
      :is-searching="isSearching"
      :show-search="showSearch"
      @set-view="setView"
      @navigate="navigateTo"
      @navigate-to-sibling="navigateToSibling($event.atIndex, $event.node)"
      @reset="activeViewRef?.reset?.()"
      @open-search="openSearch"
      @go-home="onGoHome"
    >
      <template #search>
        <input
          ref="searchInputRef"
          v-model="searchQuery"
          type="search"
          class="search-input"
          :placeholder="placeholder"
          spellcheck="true"
          autocomplete="off"
          @keydown="onSearchKeydown"
          @blur="onSearchBlur"
        />
      </template>
    </BookCatalogTitleBar>
    <div class="books-content">
      <LoadingAnimation v-if="loading" />
      <div v-else-if="error" class="state error">
        <span class="error-msg">{{ error }}</span>
        <button class="retry-btn" @click="load()">נסה שוב</button>
        <template v-if="isBitnessMismatch(error)">
          <div v-if="diagLoading" class="diag-loading">אוסף נתוני אבחון...</div>
          <div v-else-if="diagData" class="diag-panel">
            <div class="diag-table">
              <div v-for="(val, key) in diagData" :key="key" class="diag-row">
                <span class="diag-key" dir="ltr">{{ key }}</span>
                <span
                  class="diag-val"
                  dir="ltr"
                  :class="{
                    'val-error':
                      (String(key).includes('sqlite.interop') &&
                        String(key).includes('present') &&
                        val === 'false') ||
                      String(val).startsWith('error:') ||
                      val === 'not found',
                    'val-ok':
                      (String(key).includes('sqlite.interop') &&
                        String(key).includes('present') &&
                        val === 'true') ||
                      val === 'True',
                  }"
                  >{{ val }}</span
                >
              </div>
            </div>
            <button class="diag-copy-btn" @click="copyDiagnostics">העתק לדוח</button>
          </div>
        </template>
      </div>
      <template v-else>
        <component
          :is="activeViewComponent"
          ref="activeViewRef"
          v-show="!isSearching"
          v-bind="activeViewProps"
          @select-book="onSelectBook"
          @enter-folder="onEnterFolder"
        />
        <!-- select-book/select-toc emit (item, openInNewTab) — the boolean is
             forwarded straight through to the pane-navigation helper. -->
        <template v-if="isSearching">
          <LoadingAnimation v-if="tocSearching && !searchItems.length" />
          <BookCatalogSearch
            ref="searchResultsRef"
            v-else
            :items="searchItems"
            :view="view"
            :searching="tocSearching"
            @select-book="onSelectBook"
            @select-toc="onSelectToc"
          />
        </template>
      </template>
    </div>
  </div>
</template>

<style scoped>
/* Fill, border, colors, placeholder and the search-cancel button all come from
   the global `.search-inner input` rule; the sizing comes from the title bar. */
.search-input {
  flex: 1;
  min-width: 0;
}
.books-page {
  display: flex;
  flex-direction: column;
  height: 100%;
  background: var(--bg-primary);
}
.books-content {
  flex: 1;
  overflow: hidden;
  position: relative;
}
.state.error {
  padding: 32px 16px;
  text-align: center;
  color: var(--status-danger);
  font-size: 15px;
}
.error-msg {
  display: block;
  margin-bottom: 12px;
}
.retry-btn {
  height: 32px;
  padding: 0 16px;
  font-size: 13px;
  color: var(--text-primary);
  border: 1px solid var(--border-color);
  margin-bottom: 16px;
}
.diag-loading {
  font-size: 12px;
  color: var(--text-secondary);
  margin-top: 8px;
}
.diag-panel {
  margin-top: 12px;
  text-align: start;
  width: 100%;
  max-width: 560px;
  margin-inline: auto;
}
.diag-table {
  border: 1px solid var(--border-color);
  border-radius: 4px;
  overflow: hidden;
  font-size: 11px;
  font-family: 'Consolas', 'Cascadia Code', monospace;
}
.diag-row {
  display: flex;
  align-items: baseline;
  gap: 8px;
  padding: 3px 8px;
  border-bottom: 1px solid color-mix(in srgb, var(--border-color) 50%, transparent);
}
.diag-row:last-child {
  border-bottom: none;
}
.diag-row:nth-child(odd) {
  background: color-mix(in srgb, var(--text-primary) 3%, transparent);
}
.diag-key {
  flex-shrink: 0;
  width: 260px;
  color: var(--text-secondary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.diag-val {
  flex: 1;
  color: var(--text-primary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.val-error {
  color: var(--status-danger);
  font-weight: 600;
}
.val-ok {
  color: var(--status-success);
}
.diag-copy-btn {
  margin-top: 8px;
  height: 28px;
  padding: 0 12px;
  font-size: 12px;
  color: var(--text-secondary);
  border: 1px solid var(--border-color);
}
</style>
