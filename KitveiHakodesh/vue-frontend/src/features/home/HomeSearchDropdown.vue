<script setup lang="ts">
import { ref, computed } from 'vue'
import {
  IconLibrary20Filled,
  IconBookOpen20Filled,
  IconArrowSync20Regular,
  IconDocument20Filled,
  IconDocumentPdf20Filled,
  IconDocumentText20Filled,
  IconDocumentGlobe20Filled,
  IconDelete20Regular,
  IconHome20Regular,
  IconDocument20Regular,
  IconDocumentText20Regular,
  IconSearch20Regular,
  IconLibrary20Regular,
  IconDocumentPdf20Regular,
  IconApps20Regular,
  IconPuzzlePiece20Regular,
} from '@iconify-prerendered/vue-fluent'
import IconBookRtl20 from '@/components/IconBookRtl20.vue'
import { useListKeys } from '@/composables/useListKeyNav'
import { wantsNewTab, withNewTabHint } from '@/composables/useOpenInNewTab'
import type {
  CatalogSearchResult,
  HebrewBooksSearchResult,
  FileSearchResult,
  SearchSourcePriority,
} from './useHomeSearch'
import type { TocFsItem } from '@/features/book-catalog/useBookCatalogSearch'
import type { HebrewBook } from '@/features/hebrewbooks/hebrewBooksCatalog'
import type { RecentTab } from '@/stores/recentTabs'
import type { RecentlyOpenedEntry } from '@/stores/recentlyOpenedStore'

const props = defineProps<{
  catalogResults: CatalogSearchResult[]
  /**
   * TOC heuristics results — from the zero-results fallback (books empty) or
   * the additive keyword trigger (shown below the book results).
   */
  catalogTocResults: TocFsItem[]
  hebrewBooksResults: HebrewBooksSearchResult[]
  fileResults: FileSearchResult[]
  sourcePriority: SearchSourcePriority
  isLoadingCatalogToc: boolean
  isLoadingHebrewBooks: boolean
  isLoadingFiles: boolean
  // Position and size passed from parent via getBoundingClientRect. The
  // dropdown spans exactly the anchor width (left+right pinned) — the parent
  // decides what to anchor to (the input, or the whole shell on narrow panes).
  anchorTop: number
  anchorLeft: number
  anchorRight: number
  maxHeight: number
  /**
   * Optional tab list (address-bar mode). These are RECENT tabs — the parallel
   * list that keeps an entry after its tab closes — so the section shows open and
   * closed tabs together, distinguished by `open`. The parent passes them only
   * when it wants them shown (empty query / no search results), so in practice
   * the dropdown shows either tabs or results, not both.
   */
  tabs?: RecentTab[]
  activeTabId?: string
  /**
   * Optional recently-opened documents (address-bar mode, same collection as
   * the home-page tiles). Rendered below the tab section under the same
   * show-only-when-no-results rule.
   */
  recentEntries?: RecentlyOpenedEntry[]
}>()

const emit = defineEmits<{
  selectCatalogBook: [bookId: number, bookTitle: string, openInNewTab: boolean]
  selectCatalogToc: [item: TocFsItem, openInNewTab: boolean]
  selectHebrewBook: [book: HebrewBook, openInNewTab: boolean]
  selectFile: [item: FileSearchResult, openInNewTab: boolean]
  selectTab: [id: string]
  forgetTab: [id: string]
  selectRecent: [entry: RecentlyOpenedEntry, openInNewTab: boolean]
  dropdownFocused: []
  dropdownBlurred: []
}>()

const dropdownRef = ref<HTMLElement | null>(null)

// Positioning: pin left+right to the anchor so the dropdown spans exactly the
// anchor width.
const dropdownStyle = computed(() => ({
  top: props.anchorTop + 'px',
  left: props.anchorLeft + 'px',
  right: props.anchorRight + 'px',
  maxHeight: props.maxHeight + 'px',
}))

// The three sections in priority order. The prioritized source comes first;
// the other two follow in their natural default order — local file results
// rank above HebrewBooks, which is the remote/last-resort source.
const sectionOrder = computed<SearchSourcePriority[]>(() => {
  const priority = props.sourcePriority
  const all: SearchSourcePriority[] = ['catalog', 'files', 'hebrewbooks']
  return [priority, ...all.filter((source) => source !== priority)]
})

const allItems = computed(() => {
  const items: Array<
    | { kind: 'tab'; id: string }
    | { kind: 'recent'; entry: RecentlyOpenedEntry }
    | { kind: 'catalog'; bookId: number; title: string }
    | { kind: 'catalogToc'; item: TocFsItem }
    | { kind: 'hebrewBooks'; book: HebrewBook }
    | { kind: 'file'; item: FileSearchResult }
  > = []
  for (const tab of props.tabs ?? []) {
    items.push({ kind: 'tab', id: tab.id })
  }
  for (const entry of props.recentEntries ?? []) {
    items.push({ kind: 'recent', entry })
  }
  for (const source of sectionOrder.value) {
    if (source === 'catalog') {
      for (const item of props.catalogResults) {
        items.push({ kind: 'catalog', bookId: item.book.id, title: item.book.title })
      }
      for (const item of props.catalogTocResults) {
        items.push({ kind: 'catalogToc', item })
      }
    } else if (source === 'hebrewbooks') {
      for (const item of props.hebrewBooksResults) {
        items.push({ kind: 'hebrewBooks', book: item.book })
      }
    } else {
      for (const item of props.fileResults) {
        items.push({ kind: 'file', item })
      }
    }
  }
  return items
})

function activateItem(index: number, openInNewTab = false) {
  const item = allItems.value[index]
  if (!item) return
  if (item.kind === 'tab') emit('selectTab', item.id)
  else if (item.kind === 'recent') emit('selectRecent', item.entry, openInNewTab)
  else if (item.kind === 'catalog') emit('selectCatalogBook', item.bookId, item.title, openInNewTab)
  else if (item.kind === 'catalogToc') emit('selectCatalogToc', item.item, openInNewTab)
  else if (item.kind === 'hebrewBooks') emit('selectHebrewBook', item.book, openInNewTab)
  else emit('selectFile', item.item, openInNewTab)
}

const { focusedIndex, containerFocused } = useListKeys(
  dropdownRef,
  () => allItems.value.length,
  activateItem,
)

defineExpose({
  focus: () => dropdownRef.value?.focus(),
  element: dropdownRef,
})

function onDropdownFocus() {
  if (focusedIndex.value < 0) focusedIndex.value = 0
  emit('dropdownFocused')
}

function onDropdownBlur(e: FocusEvent) {
  if (e.relatedTarget !== null && !dropdownRef.value?.contains(e.relatedTarget as Node)) {
    emit('dropdownBlurred')
  }
}

type FileIconInfo = { component: unknown; color: string }

function getFileIcon(item: FileSearchResult): FileIconInfo {
  if (item.addinName) return { component: IconPuzzlePiece20Regular, color: '#7b5ea7' }
  const extension = item.fileName.toLowerCase().split('.').pop()
  switch (extension) {
    case 'pdf':
      return { component: IconDocumentPdf20Filled, color: '#F40F02' }
    case 'html':
    case 'htm':
    case 'mht':
    case 'mhtml':
      return { component: IconDocumentGlobe20Filled, color: '#0097fb' }
    case 'txt':
      return { component: IconDocumentText20Filled, color: '#9e9e9e' }
    default:
      return { component: IconDocument20Filled, color: '#3478f6' }
  }
}

// Route → icon for the recently-opened rows — the 20px versions of the
// home-page tile icons (RECENTLY_OPENED_ICON_MAP), same colors.
function getRecentIcon(entry: RecentlyOpenedEntry): FileIconInfo {
  if (entry.route === '/html-view' && entry.isOtzariaAddin)
    return { component: IconPuzzlePiece20Regular, color: '#7b5ea7' }
  switch (entry.route) {
    case '/pdf-view':
      return { component: IconDocumentPdf20Filled, color: '#F40F02' }
    case '/html-view':
      return { component: IconDocumentGlobe20Filled, color: '#0097fb' }
    case '/txt-view':
      return { component: IconDocumentText20Filled, color: '#9e9e9e' }
    default: // '/book-view'
      return { component: IconBookRtl20, color: '#c1440e' }
  }
}

// Route → icon for the open-tab rows (same mapping the old title-bar tab
// dropdown used). The book icon keeps its warm accent color.
function getTabIcon(route: string): FileIconInfo {
  switch (route) {
    case '/':
      return { component: IconHome20Regular, color: '' }
    case '/book-view':
    case '/hebrewbooks':
      return { component: IconBookRtl20, color: '#c1440e' }
    case '/pdf-view':
      return { component: IconDocumentPdf20Regular, color: '' }
    case '/txt-view':
      return { component: IconDocumentText20Regular, color: '' }
    case '/search':
      return { component: IconSearch20Regular, color: '' }
    case '/books':
      return { component: IconLibrary20Regular, color: '' }
    case '/workspaces':
      return { component: IconApps20Regular, color: '' }
    default:
      return { component: IconDocument20Regular, color: '' }
  }
}
</script>

<template>
  <Teleport to="body">
    <div
      ref="dropdownRef"
      class="home-search-dropdown"
      tabindex="0"
      :style="dropdownStyle"
      @click.stop
      @focus="onDropdownFocus"
      @blur="onDropdownBlur"
    >
      <!-- ── Tabs (address-bar mode: empty query / no results) ──
           Open AND closed tabs in one list: closing a tab does not remove it, it
           only clears `open`, so the row stays here until LRU evicts it. Closed
           rows dim the icon and carry no × (there is nothing to close).
           Headerless — it is the only list here, so a header would just cost a row. -->
      <template v-if="tabs && tabs.length > 0">
        <div
          v-for="tab in tabs"
          :key="tab.id"
          role="option"
          class="home-search-dropdown__item"
          :class="{
            'is-focused': containerFocused && focusedIndex === allItems.findIndex((i) => i.kind === 'tab' && i.id === tab.id),
            'is-active-tab': tab.id === activeTabId,
            'is-closed-tab': !tab.open,
          }"
          data-nav-item
          :title="tab.tocPath ? `${tab.title} · ${tab.tocPath}` : tab.title"
          @click="emit('selectTab', tab.id)"
        >
          <component
            :is="getTabIcon(tab.route).component"
            class="home-search-dropdown__item-icon"
            :style="getTabIcon(tab.route).color ? { color: getTabIcon(tab.route).color } : undefined"
          />
          <span class="home-search-dropdown__item-title">
            {{ tab.title }}<span v-if="tab.tocPath" class="home-search-dropdown__item-toc"> · {{ tab.tocPath }}</span>
          </span>
          <!-- Removes the row from the list (and closes the tab if it is open).
               Closing a tab only demotes its row, so this is the one gesture that
               actually forgets a place — hence a trash icon, not a dismiss ×. -->
          <button
            class="home-search-dropdown__tab-close"
            title="הסר מהרשימה"
            @click.stop="emit('forgetTab', tab.id)"
          >
            <IconDelete20Regular />
          </button>
        </div>
      </template>

      <!-- ── Recently-opened section (address-bar mode, below the tabs) ── -->
      <template v-if="recentEntries && recentEntries.length > 0">
        <div class="home-search-dropdown__section-header">נפתחו לאחרונה</div>
        <div
          v-for="entry in recentEntries"
          :key="entry.key"
          role="option"
          class="home-search-dropdown__item"
          :class="{ 'is-focused': containerFocused && focusedIndex === allItems.findIndex((i) => i.kind === 'recent' && i.entry.key === entry.key) }"
          data-nav-item
          :title="withNewTabHint(entry.title)"
          @click="emit('selectRecent', entry, wantsNewTab($event))"
          @auxclick.middle="emit('selectRecent', entry, wantsNewTab($event))"
        >
          <component
            :is="getRecentIcon(entry).component"
            class="home-search-dropdown__item-icon"
            :style="{ color: getRecentIcon(entry).color }"
          />
          <span class="home-search-dropdown__item-title">{{ entry.title }}</span>
        </div>
      </template>

      <template v-for="source in sectionOrder" :key="source">

        <!-- ── Book catalog section ── -->
        <template v-if="source === 'catalog' && (catalogResults.length > 0 || catalogTocResults.length > 0 || isLoadingCatalogToc)">
          <div class="home-search-dropdown__section-header">
            ספרים
            <IconArrowSync20Regular v-if="isLoadingCatalogToc" class="home-search-dropdown__spinner" />
          </div>
          <div
            v-for="item in catalogResults"
            :key="item.book.id"
            role="option"
            class="home-search-dropdown__item"
            :class="{ 'is-focused': containerFocused && focusedIndex === allItems.findIndex((i) => i.kind === 'catalog' && i.bookId === item.book.id) }"
            data-nav-item
            :title="withNewTabHint(item.book.parentPath ? `${item.book.title}\n${item.book.parentPath}` : item.book.title)"
            @click="emit('selectCatalogBook', item.book.id, item.book.title, wantsNewTab($event))"
            @auxclick.middle="emit('selectCatalogBook', item.book.id, item.book.title, wantsNewTab($event))"
          >
            <IconLibrary20Filled class="home-search-dropdown__item-icon home-search-dropdown__item-icon--catalog" />
            <span class="home-search-dropdown__item-title">{{ item.book.title }}</span>
            <span v-if="item.book.parentPath" class="home-search-dropdown__item-path">
              {{ item.book.parentPath }}
            </span>
          </div>
          <!-- TOC heuristics fallback results (only present when no title matched) -->
          <div
            v-for="item in catalogTocResults"
            :key="item.uid"
            role="option"
            class="home-search-dropdown__item"
            :class="{ 'is-focused': containerFocused && focusedIndex === allItems.findIndex((i) => i.kind === 'catalogToc' && i.item.uid === item.uid) }"
            data-nav-item
            :title="withNewTabHint(`${item.book.title} ${item.tocPath}`)"
            @click="emit('selectCatalogToc', item, wantsNewTab($event))"
            @auxclick.middle="emit('selectCatalogToc', item, wantsNewTab($event))"
          >
            <IconLibrary20Filled class="home-search-dropdown__item-icon home-search-dropdown__item-icon--catalog" />
            <span class="home-search-dropdown__item-title">{{ item.book.title }} {{ item.tocPath }}</span>
            <span v-if="item.book.parentPath" class="home-search-dropdown__item-path">
              {{ item.book.parentPath }}
            </span>
          </div>
        </template>

        <!-- ── HebrewBooks section ── -->
        <template v-if="source === 'hebrewbooks' && (hebrewBooksResults.length > 0 || isLoadingHebrewBooks)">
          <div class="home-search-dropdown__section-header">
            היברו-בוקס
            <IconArrowSync20Regular v-if="isLoadingHebrewBooks" class="home-search-dropdown__spinner" />
          </div>
          <div
            v-for="item in hebrewBooksResults"
            :key="item.book.id"
            role="option"
            class="home-search-dropdown__item"
            :class="{ 'is-focused': containerFocused && focusedIndex === allItems.findIndex((i) => i.kind === 'hebrewBooks' && i.book.id === item.book.id) }"
            data-nav-item
            :title="withNewTabHint(item.book.author ? `${item.book.title}\n${item.book.author}` : item.book.title)"
            @click="emit('selectHebrewBook', item.book, wantsNewTab($event))"
            @auxclick.middle="emit('selectHebrewBook', item.book, wantsNewTab($event))"
          >
            <IconBookOpen20Filled class="home-search-dropdown__item-icon home-search-dropdown__item-icon--hebrewbooks" />
            <span class="home-search-dropdown__item-title">{{ item.book.title }}</span>
            <span v-if="item.book.author" class="home-search-dropdown__item-path">
              {{ item.book.author }}
            </span>
          </div>
        </template>

        <!-- ── File search section ── -->
        <template v-if="source === 'files' && (fileResults.length > 0 || isLoadingFiles)">
          <div class="home-search-dropdown__section-header">
            קבצים
            <IconArrowSync20Regular v-if="isLoadingFiles" class="home-search-dropdown__spinner" />
          </div>
          <div
            v-for="item in fileResults"
            :key="item.fullPath"
            role="option"
            class="home-search-dropdown__item"
            :class="{ 'is-focused': containerFocused && focusedIndex === allItems.findIndex((i) => i.kind === 'file' && i.item.fullPath === item.fullPath) }"
            data-nav-item
            :title="withNewTabHint(`${item.addinName || item.fileName}\n${item.fullPath}`)"
            @click="emit('selectFile', item, wantsNewTab($event))"
            @auxclick.middle="emit('selectFile', item, wantsNewTab($event))"
          >
            <component
              :is="getFileIcon(item).component"
              class="home-search-dropdown__item-icon"
              :style="{ color: getFileIcon(item).color }"
            />
            <span class="home-search-dropdown__item-title">{{ item.addinName || item.fileName }}</span>
            <span class="home-search-dropdown__item-path">{{ item.fullPath }}</span>
          </div>
        </template>

      </template>
    </div>
  </Teleport>
</template>

<style scoped>
.home-search-dropdown {
  position: fixed;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  box-shadow: 0 4px 20px rgba(0, 0, 0, 0.25), 0 1px 4px rgba(0, 0, 0, 0.12);
  overflow-y: auto;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
  z-index: 1000;
  outline: none;
}

.home-search-dropdown__section-header {
  display: flex;
  align-items: center;
  gap: 6px;
  height: 24px;
  padding: 0 12px;
  font-size: 11px;
  font-weight: 600;
  color: var(--text-secondary);
  background: color-mix(in srgb, var(--text-primary) 4%, transparent);
  border-bottom: 1px solid var(--border-color);
}

.home-search-dropdown__spinner {
  animation: spin 1s linear infinite;
  opacity: 0.6;
}

@keyframes spin {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
}

.home-search-dropdown__item {
  display: flex;
  align-items: center;
  gap: 8px;
  width: 100%;
  /* Row height tracks the title bar's density mode (32px compact / 40px normal). */
  height: var(--title-bar-height);
  padding: 0 12px;
  text-align: right;
  background: transparent;
  border-bottom: 1px solid color-mix(in srgb, var(--border-color) 50%, transparent);
  cursor: pointer;
  overflow: hidden;
  box-sizing: border-box;
}

.home-search-dropdown__item:last-child {
  border-bottom: none;
}

.home-search-dropdown__item:hover,
.home-search-dropdown__item.is-focused {
  background: color-mix(in srgb, var(--text-primary) 6%, transparent);
}

.home-search-dropdown__item:active {
  background: color-mix(in srgb, var(--text-primary) 10%, transparent);
}

.home-search-dropdown__item-icon {
  flex-shrink: 0;
}

.home-search-dropdown__item-icon--catalog {
  color: #b5451b;
}

.home-search-dropdown__item-icon--hebrewbooks {
  color: #d94f1e;
}

.home-search-dropdown__item-title {
  flex: 1;
  min-width: 0;
  font-size: 13px;
  color: var(--text-primary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.home-search-dropdown__item-path {
  flex-shrink: 0;
  max-width: 40%;
  font-size: 11px;
  color: var(--text-secondary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

/* ── Open-tab rows (address-bar mode) ── */
.home-search-dropdown__item.is-active-tab {
  background: color-mix(in srgb, var(--accent-color) 10%, transparent);
}

/* A closed tab is still a tab — same row, just quieter, so the open ones read
   first. Only the icon fades; the title keeps full contrast to stay legible. */
.home-search-dropdown__item.is-closed-tab .home-search-dropdown__item-icon {
  opacity: 0.45;
}

/* TOC path after the tab title: greyed but the SAME size as the title. */
.home-search-dropdown__item-toc {
  color: var(--text-secondary);
  opacity: 0.8;
}

.home-search-dropdown__tab-close {
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  width: 24px;
  height: 24px;
  border-radius: 4px;
  color: var(--text-secondary);
}
.home-search-dropdown__tab-close svg {
  width: 16px;
  height: 16px;
}
.home-search-dropdown__tab-close:hover {
  color: var(--text-primary);
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}
</style>
