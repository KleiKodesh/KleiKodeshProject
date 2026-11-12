<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { useSettingsStore } from '@/stores/settingsStore'
import type { CategoryNode } from '@/features/book-catalog/bookCatalogTree'
import MessageBanner from '@/components/MessageBanner.vue'
import FullTextSearchLayout from '@/features/full-text-search/FullTextSearchLayout.vue'
import FullTextSearchBar from '@/features/full-text-search/FullTextSearchBar.vue'
import FullTextSearchOptionsPanel from '@/features/full-text-search/FullTextSearchOptionsPanel.vue'
import FullTextSearchFilterPanel from '@/features/full-text-search/FullTextSearchFilterPanel.vue'
import { useFullTextSearch } from '@/features/full-text-search/useFullTextSearch'
import { useFullTextSearchPanelsClose } from '@/features/full-text-search/useFullTextSearchPanelsClose'
import type { FullTextSearchSortOrder } from '@/features/full-text-search/fullTextSearchTypes'
import { useUserSpace } from './useUserSpace'

/**
 * Full-text search over UserSpace: the library search's page (same frame, bar, options and
 * filter panel), with UserSpace's roots in the filter instead of the catalog.
 *
 * IN DEVELOPMENT (dev builds only): there is no UserSpace index yet, so a search only says so.
 */

const paneNavigation = usePaneNavigation()
const tabId = paneNavigation.activeTabId
const settings = useSettingsStore()
const { ROOT, ensureLoaded, unload, load } = useUserSpace()

// Only the option state is used - nothing is searched until the index exists.
const { maxWordDistance, requireOrdered, allWords, setAllWords, perWord, isOptionsActive } = useFullTextSearch()

const searchQuery = ref('')
const sortOrder = ref<FullTextSearchSortOrder>('catalogOrder')
const searched = ref(false)
const isFilterOpen = ref(false)
const isOptionsOpen = ref(false)
const showOptionsCard = computed(() => !searched.value && !isOptionsOpen.value)

const searchBarRef = ref<InstanceType<typeof FullTextSearchBar> | null>(null)
const filterPanelRef = ref<HTMLElement | null>(null)
const optionsPanelRef = ref<HTMLElement | null>(null)
useFullTextSearchPanelsClose({ searchBarRef, filterPanelRef, optionsPanelRef, isFilterOpen, isOptionsOpen })

function onSearch(q: string) {
  if (!q.trim()) return
  searched.value = true
}
function onClear() {
  searched.value = false
}

// ── Filter: every book and file in UserSpace, checked by default ─────────────
// Books are those of the catalog roots. Files are kept as what the user UNCHECKED, by path:
// a computer folder's files exist only while it is open, and closing and reopening it must
// not bring back files the user had unchecked.
const checkedBookIds = ref(new Set<number>())
const uncheckedFilePaths = ref(new Set<string>())
/** Unchecked computer folders, by node id - an unopened folder has no files to say so. */
const uncheckedFolderIds = ref(new Set<number>())
const noResultCounts = new Map<number, number>()

function subtreeBookIds(node: CategoryNode): number[] {
  return node.subtreeBookIds ?? [...node.books.map((b) => b.id), ...node.children.flatMap(subtreeBookIds)]
}
function loadedFilePaths(node: CategoryNode): string[] {
  return [...(node.files ?? []).map((f) => f.path), ...node.children.flatMap(loadedFilePaths)]
}
/** A computer folder and its loaded computer subfolders. */
function computerFolderIds(node: CategoryNode): number[] {
  return node.files === undefined ? [] : [node.id, ...node.children.flatMap(computerFolderIds)]
}
const allBookIds = computed(() => [...new Set(ROOT.value.children.flatMap(subtreeBookIds))])
// ROOT is rebuilt whenever a folder's contents arrive, so these follow every load.
const allFilePaths = computed(() => loadedFilePaths(ROOT.value))
const checkedFilePaths = computed(() => new Set(allFilePaths.value.filter((p) => !uncheckedFilePaths.value.has(p))))

function checkAll() {
  checkedBookIds.value = new Set(allBookIds.value)
  uncheckedFilePaths.value = new Set()
  uncheckedFolderIds.value = new Set()
}
function uncheckAll() {
  checkedBookIds.value = new Set()
  uncheckedFilePaths.value = new Set(allFilePaths.value)
  uncheckedFolderIds.value = new Set(ROOT.value.children.flatMap(computerFolderIds))
}
const selectAllState = computed(() => {
  const total = allBookIds.value.length + allFilePaths.value.length
  const checked = checkedBookIds.value.size + checkedFilePaths.value.size
  const foldersOff = uncheckedFolderIds.value.size > 0
  return { checked: total > 0 && checked === total && !foldersOff, indet: (checked > 0 && checked < total) || (checked > 0 && foldersOff) }
})

function toggleBook(id: number) {
  const next = new Set(checkedBookIds.value)
  if (!next.delete(id)) next.add(id)
  checkedBookIds.value = next
}
function toggleFile(path: string) {
  const next = new Set(uncheckedFilePaths.value)
  if (!next.delete(path)) next.add(path)
  uncheckedFilePaths.value = next
}
function onToggleCategory(node: CategoryNode, on: boolean) {
  const books = new Set(checkedBookIds.value)
  const files = new Set(uncheckedFilePaths.value)
  const folders = new Set(uncheckedFolderIds.value)
  for (const id of subtreeBookIds(node)) on ? books.add(id) : books.delete(id)
  for (const path of loadedFilePaths(node)) on ? files.delete(path) : files.add(path)
  for (const id of computerFolderIds(node)) on ? folders.delete(id) : folders.add(id)
  checkedBookIds.value = books
  uncheckedFilePaths.value = files
  uncheckedFolderIds.value = folders
}

// A folder opened in the filter loads its contents, as on the UserSpace page. Inside an
// unchecked folder everything arrives unchecked, subfolders included.
async function onExpandNode(node: CategoryNode) {
  await ensureLoaded(node)
  if (!uncheckedFolderIds.value.has(node.id)) return
  uncheckedFilePaths.value = new Set([...uncheckedFilePaths.value, ...(node.files ?? []).map((f) => f.path)])
  uncheckedFolderIds.value = new Set([...uncheckedFolderIds.value, ...node.children.flatMap(computerFolderIds)])
}
function onCollapseNode(node: CategoryNode) {
  unload(node)
}

// Books of roots added or removed while the page is open follow the roots.
watch(allBookIds, (ids, before) => {
  const added = ids.filter((id) => !before?.includes(id))
  const kept = new Set(ids)
  checkedBookIds.value = new Set([...[...checkedBookIds.value].filter((id) => kept.has(id)), ...added])
})

onMounted(async () => {
  await load()
  checkAll()
  searchBarRef.value?.focus({ silent: true })
})
</script>

<template>
  <FullTextSearchLayout
    :show-options-card="showOptionsCard"
    :is-options-open="isOptionsOpen"
    :is-filter-open="isFilterOpen"
    :instance-key="`us-${tabId}`"
    @close-filter="isFilterOpen = false"
  >
    <template #bar>
      <FullTextSearchBar
        ref="searchBarRef"
        v-model:search-query="searchQuery"
        v-model:sort-order="sortOrder"
        :is-searching="false"
        :result-count="0"
        :total-result-count="0"
        :filter-count="checkedBookIds.size + checkedFilePaths.size"
        :at-filter-count="0"
        :is-options-open="isOptionsOpen"
        :is-options-active="isOptionsActive"
        :show-options-toggle="!showOptionsCard"
        @search="onSearch"
        @clear="onClear"
        @toggle-filter="isFilterOpen = !isFilterOpen"
        @toggle-options="isOptionsOpen = !isOptionsOpen"
      />
    </template>
    <template #options>
      <FullTextSearchOptionsPanel
        v-if="isOptionsOpen"
        ref="optionsPanelRef"
        :max-word-distance="maxWordDistance"
        :require-ordered="requireOrdered"
        :context-words="settings.searchContextMarginWords"
        :all-words="allWords"
        :per-word="perWord"
        :query="searchQuery"
        @update:max-word-distance="maxWordDistance = $event"
        @update:require-ordered="requireOrdered = $event"
        @update:context-words="settings.searchContextMarginWords = $event"
        @set-all-words="setAllWords"
        @update:per-word="perWord = $event"
        @close="isOptionsOpen = false"
      />
    </template>
    <template #card-options>
      <FullTextSearchOptionsPanel
        as-card
        :max-word-distance="maxWordDistance"
        :require-ordered="requireOrdered"
        :context-words="settings.searchContextMarginWords"
        :all-words="allWords"
        :per-word="perWord"
        :query="searchQuery"
        @update:max-word-distance="maxWordDistance = $event"
        @update:require-ordered="requireOrdered = $event"
        @update:context-words="settings.searchContextMarginWords = $event"
        @set-all-words="setAllWords"
        @update:per-word="perWord = $event"
      />
    </template>
    <template #results>
      <MessageBanner class="not-indexed" severity="info" :dismissible="false" :message="$t('userSpace.searchNotIndexed')" />
    </template>
    <template #filter>
      <FullTextSearchFilterPanel
        ref="filterPanelRef"
        :nodes="ROOT.children"
        :checked-book-ids="checkedBookIds"
        :checked-file-paths="checkedFilePaths"
        :unchecked-folder-ids="uncheckedFolderIds"
        :select-all-state="selectAllState"
        :book-title-search="false"
        :result-counts="noResultCounts"
        :at-filters="[]"
        @toggle-book="toggleBook"
        @toggle-file="toggleFile"
        @toggle-category="onToggleCategory"
        @expand-node="onExpandNode"
        @collapse-node="onCollapseNode"
        @check-all="checkAll"
        @uncheck-all="uncheckAll"
        @close="isFilterOpen = false"
      />
    </template>
  </FullTextSearchLayout>
</template>

<style scoped>
.not-indexed {
  flex: none;
  margin: 12px;
}
</style>
