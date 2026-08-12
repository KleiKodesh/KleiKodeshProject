<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import {
  IconLibrary20Filled,
  IconBookOpen20Filled,
  IconArrowSync20Regular,
  IconDelete20Regular,
} from '@iconify-prerendered/vue-fluent'
import {
  documentIcon,
  iconKeyForRoute,
  iconKeyForFileName,
  type DocumentIconKey,
} from '@/utils/documentIcons'
import { useInputListNavigation } from '@/composables/useInputListNavigation'
import { wantsNewTab, withNewTabHint } from '@/composables/useOpenInNewTab'
import type {
  CatalogSearchResult,
  HebrewBooksSearchResult,
  FileSearchResult,
  SearchSourcePriority,
} from './useHomeSearch'
import type { TocFsItem } from '@/features/book-catalog/useBookCatalogSearch'
import type { HebrewBook } from '@/features/hebrewbooks/hebrewBooksCatalog'
import type { NavLocation } from '@/stores/navLocation'
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
   * Optional recents list (address-bar mode) — LOCATIONS the reader has visited,
   * most recent first. Not tabs: selecting one navigates the current tab, and
   * nothing here reflects which tabs are open. The parent passes them only when it
   * wants them shown (empty query / no search results), so in practice the dropdown
   * shows either recents or results, not both.
   */
  tabs?: NavLocation[]
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
  selectTab: [id: string, openInNewTab: boolean]
  forgetTab: [id: string]
  selectRecent: [entry: RecentlyOpenedEntry, openInNewTab: boolean]
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
  if (item.kind === 'tab') emit('selectTab', item.id, openInNewTab)
  else if (item.kind === 'recent') emit('selectRecent', item.entry, openInNewTab)
  else if (item.kind === 'catalog') emit('selectCatalogBook', item.bookId, item.title, openInNewTab)
  else if (item.kind === 'catalogToc') emit('selectCatalogToc', item.item, openInNewTab)
  else if (item.kind === 'hebrewBooks') emit('selectHebrewBook', item.book, openInNewTab)
  else emit('selectFile', item.item, openInNewTab)
}

// Combobox model: focus stays in the parent's input; the parent forwards its
// keydown events here and this component moves the highlight through allItems.
const { activeIndex, onKeydown: onSearchInputKeydown } = useInputListNavigation({
  getCount: () => allItems.value.length,
  onActivate: activateItem,
  containerElement: dropdownRef,
})

// Any change to the flattened item list (typing, async sources landing) makes
// the old index point at a different row — drop the highlight.
watch(allItems, () => {
  activeIndex.value = -1
})

defineExpose({
  onSearchInputKeydown,
  element: dropdownRef,
})

// All three of these read the ONE shared mapping in utils/documentIcons — the
// same table the home tiles use, so a document looks identical wherever it is
// listed. They used to be separate local copies that had drifted apart.
type FileIconInfo = { component: unknown; color: string }

function iconInfo(key: DocumentIconKey): FileIconInfo {
  const icon = documentIcon(key)
  return { component: icon.icon20, color: icon.color }
}

function getFileIcon(item: FileSearchResult): FileIconInfo {
  return iconInfo(iconKeyForFileName(item.fileName, !!item.addinName))
}

function getRecentIcon(entry: RecentlyOpenedEntry): FileIconInfo {
  return iconInfo(iconKeyForRoute(entry.route, entry.isOtzariaAddin))
}

function getTabIcon(route: string): FileIconInfo {
  return iconInfo(iconKeyForRoute(route))
}
</script>

<template>
  <Teleport to="body">
    <div ref="dropdownRef" class="home-search-dropdown" :style="dropdownStyle" @click.stop>
      <!-- ── Recents (address-bar mode: empty query / no results) ──
           Locations the reader has been, most recent first. Selecting one navigates
           the CURRENT tab (Ctrl/middle-click opens a new one), like any address-bar
           row. Headerless — it is the only list here, so a header would cost a row. -->
      <template v-if="tabs && tabs.length > 0">
        <div
          v-for="tab in tabs"
          :key="tab.id"
          role="option"
          class="home-search-dropdown__item"
          :class="{
            'is-focused': activeIndex === allItems.findIndex((i) => i.kind === 'tab' && i.id === tab.id),
          }"
          data-nav-item
          :title="withNewTabHint(tab.tocPath ? `${tab.title} · ${tab.tocPath}` : tab.title)"
          @click="emit('selectTab', tab.id, wantsNewTab($event))"
          @auxclick.middle="emit('selectTab', tab.id, wantsNewTab($event))"
        >
          <component
            :is="getTabIcon(tab.route).component"
            class="home-search-dropdown__item-icon"
            :style="getTabIcon(tab.route).color ? { color: getTabIcon(tab.route).color } : undefined"
          />
          <span class="home-search-dropdown__item-title">
            {{ tab.title }}<span v-if="tab.tocPath" class="home-search-dropdown__item-toc"> · {{ tab.tocPath }}</span>
          </span>
          <!-- Forgets the location. Closes nothing: locations and tabs are
               independent, so this is "remove from history", hence a trash icon. -->
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
          :class="{ 'is-focused': activeIndex === allItems.findIndex((i) => i.kind === 'recent' && i.entry.key === entry.key) }"
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
            קטלוג הספרים
            <IconArrowSync20Regular v-if="isLoadingCatalogToc" class="home-search-dropdown__spinner" />
          </div>
          <div
            v-for="item in catalogResults"
            :key="item.book.id"
            role="option"
            class="home-search-dropdown__item"
            :class="{ 'is-focused': activeIndex === allItems.findIndex((i) => i.kind === 'catalog' && i.bookId === item.book.id) }"
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
            :class="{ 'is-focused': activeIndex === allItems.findIndex((i) => i.kind === 'catalogToc' && i.item.uid === item.uid) }"
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
            :class="{ 'is-focused': activeIndex === allItems.findIndex((i) => i.kind === 'hebrewBooks' && i.book.id === item.book.id) }"
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
            :class="{ 'is-focused': activeIndex === allItems.findIndex((i) => i.kind === 'file' && i.item.fullPath === item.fullPath) }"
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
