<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount, nextTick, watch } from 'vue'
import { useIntervalFn } from '@vueuse/core'
import { IconSearch20Regular } from '@iconify-prerendered/vue-fluent'
import { useBookCatalog } from './useBookCatalog'
import BookCatalogTitleBar from './BookCatalogTitleBar.vue'
import BookCatalogViewTree from './BookCatalogView.Tree.vue'
import BookCatalogViewTiles from './BookCatalogView.Tiles.vue'
import BookCatalogViewList from './BookCatalogView.List.vue'
import BookCatalogSearch from './BookCatalogSearch.vue'
import LoadingAnimation from '@/components/LoadingAnimation.vue'
import BottomSearchBar from '@/components/BottomSearchBar.vue'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { useTabStore } from '@/stores/tabStore'
import { useSettingsStore, type BooksView } from '@/stores/settingsStore'
import { storeToRefs } from 'pinia'
import type { CategoryNode } from '@/features/book-catalog/bookCatalogTree'
import type { BookRow } from '@/webview-host/queries.types'
import type { TocFsItem } from './useBookCatalogSearch'
import { getDiagnostics } from '@/webview-host/bridge'
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
// Restore the typed query saved on the tab (booksSearchQuery), so switching away
// and back keeps the input. The VSTO host seed (catalogQuery) wins if present —
// it's a one-shot search request, not a restored session. That seed watch runs
// synchronously at setup (immediate), before this onMounted, and fills
// searchQuery; so only restore when searchQuery is still empty.
onMounted(() => {
  if (!searchQuery.value) {
    const saved = paneNavigation.activeTab.booksSearchQuery
    if (saved) searchQuery.value = saved
  }
})

// On unmount, mirror the file-search rule:
//  • Tab still '/books' (tab switch, or a NEW tab opened via Ctrl+click) → save
//    the query so it restores when the user returns to this tab.
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
  navigator.clipboard.writeText(lines.join('\n')).catch(() => {})
}
const activeViewComponent = computed(() => {
  if (view.value === 'tree') return BookCatalogViewTree
  if (view.value === 'tiles') return BookCatalogViewTiles
  return BookCatalogViewList
})

const activeViewProps = computed(() => (view.value === 'tree' ? {} : { items: treeItems.value }))

type ActiveViewInstance = ComponentPublicInstance & {
  focusContainer?: () => void
  reset?: () => void
}

const activeViewRef = ref<ActiveViewInstance | null>(null)
const searchResultsRef = ref<InstanceType<typeof BookCatalogSearch> | null>(null)
const searchInputRef = ref<HTMLInputElement | null>(null)

function focusList() {
  if (isSearching.value) {
    searchResultsRef.value?.focusContainer()
    return
  }
  activeViewRef.value?.focusContainer?.()
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

function setView(v: BooksView) {
  // Assigning the store ref persists it — settingsStore watches and writes.
  view.value = v
}

onMounted(() => {
  load()
  nextTick(() => searchInputRef.value?.focus())
})

function onSelectBook(book: BookRow, openInNewTab = false) {
  paneNavigation.openOrUpdateActiveTab(
    {
      title: book.title,
      route: '/book-view',
      bookId: book.id,
      openToc: true,
    },
    openInNewTab,
  )
}
function onSelectToc(item: TocFsItem, openInNewTab = false) {
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
      @set-view="setView"
      @navigate="navigateTo"
      @navigate-to-sibling="navigateToSibling($event.atIndex, $event.node)"
      @reset="activeViewRef?.reset?.()"
    />
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
          @enter-folder="enter"
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
    <BottomSearchBar>
      <template #left><IconSearch20Regular class="search-icon" /></template>
      <input
        ref="searchInputRef"
        v-model="searchQuery"
        type="search"
        class="search-input"
        :placeholder="placeholder"
        spellcheck="true"
        autocomplete="off"
        @keydown.enter="onSearchEnter"
        @keydown.up.prevent="focusList"
        @keydown.down.prevent="focusList"
        @keydown.tab.prevent="focusList"
      />
    </BottomSearchBar>
  </div>
</template>

<style scoped>
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
.search-icon {
  color: var(--text-secondary);
}
.search-input {
  flex: 1;
  background: none;
  border: none;
  outline: none;
  font-size: 13px;
  color: var(--text-primary);
}
.search-input::placeholder {
  color: var(--text-secondary);
}
.search-input::-webkit-search-cancel-button {
  filter: grayscale(1) opacity(0.4);
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
