<script setup lang="ts">
import { ref, computed, onMounted, nextTick, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { useElementSize } from '@vueuse/core'
import { IconFolderAdd20Regular, IconLibrary20Regular, IconDelete20Regular } from '@iconify-prerendered/vue-fluent'
import { useCardGridLayout } from '@/composables/useCardGridLayout'
import BookCatalogTitleBar from '@/features/book-catalog/BookCatalogTitleBar.vue'
import BookCatalogViewList from '@/features/book-catalog/BookCatalogView.List.vue'
import BookCatalogViewCards from '@/features/book-catalog/BookCatalogView.Cards.vue'
import BookCatalogViewTiles from '@/features/book-catalog/BookCatalogView.Tiles.vue'
import BookCatalogViewTree from '@/features/book-catalog/BookCatalogView.Tree.vue'
import type { CategoryNode, LocalFileItem } from '@/features/book-catalog/bookCatalogTree'
import LoadingAnimation from '@/components/LoadingAnimation.vue'
import MessageBanner from '@/components/MessageBanner.vue'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import UserSpaceCatalogPicker from './UserSpaceCatalogPicker.vue'
import BookCatalogSearch from '@/features/book-catalog/BookCatalogSearch.vue'
import type { TocFsItem } from '@/features/book-catalog/useBookCatalogSearch'
import { useUserSpaceSearch } from './useUserSpaceSearch'
import { useSearchIndexStatus, useLocalFileIndexBuildPolling } from '@/composables/useSearchIndexStatus'
import { useUserSpace } from './useUserSpace'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { useTabStore } from '@/stores/tabStore'
import { useSettingsStore, type BooksView } from '@/stores/settingsStore'
import { useBooksDataStore } from '@/stores/booksDataStore'
import { useUserSpaceStore, userSpaceRootKey } from '@/stores/userSpaceStore'
import { opensInFastDocView } from '@/utils/fastDocViewFiles'
import { pickFolder, restoreLocalFile, checkUserSpaceFolder } from '@/webview-host/bridge'
import type { BookRow } from '@/webview-host/queries.types'
import type { ComponentPublicInstance } from 'vue'
import { t } from '@/i18n'

// The catalog page's shape - same address bar, same four views - over the user's own
// roots (see useUserSpace), and the catalog's search over them (useUserSpaceSearch).

const paneNavigation = usePaneNavigation()
const tabStore = useTabStore()
const userSpaceStore = useUserSpaceStore()
const { loading: catalogLoading } = storeToRefs(useBooksDataStore())
const { loaded: rootsLoaded, roots } = storeToRefs(userSpaceStore)
const { ROOT, path, treeItems, currentError, rootKeyOf, ensureLoaded, unload, unloadAll, enter, navigateTo, navigateToSibling, removeRoot, load } =
  useUserSpace()

const { userSpaceView: view } = storeToRefs(useSettingsStore())
const VIEW_COMPONENTS = {
  list: BookCatalogViewList,
  cards: BookCatalogViewCards,
  tiles: BookCatalogViewTiles,
  tree: BookCatalogViewTree,
}
const activeViewComponent = computed(() => VIEW_COMPONENTS[view.value])
const activeViewProps = computed(() =>
  view.value === 'tree' ? { root: ROOT.value, path: path.value } : { items: treeItems.value },
)
const isEmpty = computed(() => rootsLoaded.value && !ROOT.value.children.length)

function setView(v: BooksView) {
  view.value = v
}

// Cards view: the address bar shares the grid's width (see BookCatalogPage).
const contentRef = ref<HTMLElement | null>(null)
const { width: contentWidth } = useElementSize(contentRef)
const gridLayout = useCardGridLayout(() => contentWidth.value - 24, view)
const gridStyle = computed(() =>
  view.value === 'cards' && contentWidth.value > 0
    ? { '--catalog-content-width': `${gridLayout.value.width}px`, '--catalog-columns': gridLayout.value.columns }
    : undefined,
)

// ── Keyboard: one combobox, as on the catalog page ──────────────────────────
type ActiveViewInstance = ComponentPublicInstance & {
  onSearchInputKeydown: (event: KeyboardEvent) => boolean
  highlightFirst: () => void
  reset?: () => void
}
const activeViewRef = ref<ActiveViewInstance | null>(null)
const searchInputRef = ref<HTMLInputElement | null>(null)
const searchQuery = ref('')

// Root pills under the bar: narrow the search to some roots. None chosen = every root. The
// choice goes to the service, so the narrowing happens inside the indexes like the rest.
const selectedRootKeys = ref<string[]>([])
const searchRoots = computed(() =>
  selectedRootKeys.value.length
    ? roots.value.filter((r) => selectedRootKeys.value.includes(userSpaceRootKey(r)))
    : roots.value,
)
function toggleRootPill(key: string) {
  selectedRootKeys.value = selectedRootKeys.value.includes(key)
    ? selectedRootKeys.value.filter((k) => k !== key)
    : [...selectedRootKeys.value, key]
}
// A removed root drops out of the selection, so it cannot leave the search narrowed to nothing.
watch(roots, (list) => {
  const keys = new Set(list.map(userSpaceRootKey))
  selectedRootKeys.value = selectedRootKeys.value.filter((k) => keys.has(k))
})

const { results: searchItems, searching: searchBusy, isSearching } = useUserSpaceSearch(searchQuery, searchRoots)
const { statusText: catalogStatusText, isUnavailable: isCatalogUnavailable } = useSearchIndexStatus('catalogToc', t)
// UserSpace searches the same file index as the local file search, so it says the same
// "index is being built (NN%)" while that index is not ready.
const { status: fileIndexStatus, statusText: fileStatusText } = useSearchIndexStatus('localFiles', t)
useLocalFileIndexBuildPolling(searchBusy)
const isIndexUnavailable = computed(() => isCatalogUnavailable.value)
const indexStatusText = computed(() =>
  catalogStatusText.value ?? (fileIndexStatus.value.kind === 'building' ? fileStatusText.value : null))
const searchResultsRef = ref<InstanceType<typeof BookCatalogSearch> | null>(null)
// Search results have no tree shape: in tree mode they list (as on the catalog page).
const searchResultsView = computed(() => (view.value === 'tree' ? 'list' : view.value))
const editingSearch = ref(false)
const showSearch = computed(() => editingSearch.value || !!searchQuery.value)

function focusSearchInput() {
  void nextTick(() => searchInputRef.value?.focus())
}
function openSearch() {
  editingSearch.value = true
  focusSearchInput()
}
function onSearchBlur() {
  if (!searchQuery.value) editingSearch.value = false
}
function onSearchKeydown(event: KeyboardEvent) {
  if (event.code === 'Escape' && !searchQuery.value) {
    editingSearch.value = false
    return
  }
  if (isSearching.value) {
    searchResultsRef.value?.onSearchInputKeydown(event)
    return
  }
  const pathBefore = path.value
  if (activeViewRef.value?.onSearchInputKeydown(event) && path.value !== pathBefore)
    void nextTick(() => activeViewRef.value?.highlightFirst())
}

// ── Navigation ───────────────────────────────────────────────────────────────
function onEnterFolder(node: CategoryNode) {
  editingSearch.value = false
  enter(node)
  focusSearchInput()
}
function onExpandFolder(node: CategoryNode) {
  void ensureLoaded(node)
}
function onCollapseFolder(node: CategoryNode) {
  unload(node)
}
function onNavigate(index: number) {
  navigateTo(index)
  focusSearchInput()
}
function onNavigateToSibling(atIndex: number, node: CategoryNode) {
  navigateToSibling(atIndex, node)
  focusSearchInput()
}
function onGoHome() {
  editingSearch.value = false
  navigateTo(0)
  activeViewRef.value?.reset?.()
  unloadAll()
  focusSearchInput()
}

onMounted(() => {
  void userSpaceStore.claimDefaultFolders().then((rejected) => {
    // Same notice as a folder the user picked that file search cannot reach.
    if (rejected.length > 0) notice.value = `${t('userSpace.folderNotSupported')} ${t(`userSpace.folderReason.${rejected[0]!.reason || 'notIndexedDrive'}`)}`
  })
  void load()
  // The field only exists while showSearch is true, so swap it in before focusing.
  openSearch()
})

// ── Opening ──────────────────────────────────────────────────────────────────
function onSelectBook(book: BookRow, openInNewTab = false) {
  paneNavigation.openOrUpdateActiveTab({ title: book.title, route: '/book-view', bookId: book.id }, openInNewTab)
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

// Same steps as the file-search page's open: FastDocView types open straight off the
// path; the rest are authorized by the service first and routed by what it serves.
const thisTabId = paneNavigation.activeTabId
async function onSelectFile(file: LocalFileItem, openInNewTab = false) {
  const dot = file.name.lastIndexOf('.')
  const title = file.title || (dot > 0 ? file.name.substring(0, dot) : file.name)
  const ext = dot > 0 ? file.name.substring(dot).toLowerCase() : ''
  const route = opensInFastDocView(file.path) ? '/fast-doc-view' : ext === '.htm' || ext === '.html' ? '/html-view' : '/pdf-view'
  // Patched by id: the active tab may change while restoreLocalFile awaits.
  const targetTabId = openInNewTab ? paneNavigation.openTab({ route, title }).id : thisTabId

  if (route === '/fast-doc-view') {
    tabStore.updateTab(targetTabId, {
      route,
      title,
      localFileName: file.name,
      localFilePath: file.path,
      localFileVirtualUrl: undefined,
      isOtzariaAddin: false,
    })
    return
  }
  const restored = await restoreLocalFile(file.path)
  if (!restored?.url) return
  const servedRoute = restored.kind === 'html' ? '/html-view' : restored.kind === 'pdf' ? '/pdf-view' : route
  tabStore.updateTab(targetTabId, {
    route: servedRoute as '/html-view' | '/pdf-view',
    title,
    localFileName: file.name,
    localFilePath: file.path,
    localFileVirtualUrl: restored.url,
    isOtzariaAddin: false,
  })
}

// ── Adding and removing roots ────────────────────────────────────────────────
const notice = ref<string | null>(null)
const pickingFromCatalog = ref(false)

// Only folders the file search covers can be added for now - the search could not reach the
// rest. The message names the reason, so a refusal is never a mystery.
async function addComputerFolder() {
  const picked = await pickFolder(t('userSpace.addComputerFolder'))
  focusSearchInput()
  if (!picked) return
  const check = await checkUserSpaceFolder(picked)
  if (!check.covered) {
    notice.value = `${t('userSpace.folderNotSupported')} ${t(`userSpace.folderReason.${check.reason || 'notIndexedDrive'}`)}`
    return
  }
  if (!userSpaceStore.addComputerFolder(picked)) notice.value = t('userSpace.alreadyAdded')
}
function addWholeCatalog() {
  pickingFromCatalog.value = false
  if (!userSpaceStore.addWholeCatalog(t('userSpace.wholeCatalog'))) notice.value = t('userSpace.alreadyAdded')
  focusSearchInput()
}
function addCatalogFolder(node: CategoryNode) {
  pickingFromCatalog.value = false
  if (!userSpaceStore.addCatalogCategory(node.id, node.title)) notice.value = t('userSpace.alreadyAdded')
  focusSearchInput()
}

// Remove: on a root's own row/card, shown on hover or keyboard highlight; asks first.
const removing = ref<{ key: string; title: string } | null>(null)
const canRemove = (node: CategoryNode) => rootKeyOf(node) !== null
function onRemoveFolder(node: CategoryNode) {
  const key = rootKeyOf(node)
  if (key) removing.value = { key, title: node.title }
}
function confirmRemove() {
  if (removing.value) removeRoot(removing.value.key)
  removing.value = null
  focusSearchInput()
}
</script>

<template>
  <div class="user-space-page" :style="gridStyle">
    <BookCatalogTitleBar
      :class="{ 'matches-grid': gridStyle }"
      :view="view"
      :path="path"
      :show-search="showSearch"
      @set-view="setView"
      @navigate="onNavigate"
      @navigate-to-sibling="onNavigateToSibling($event.atIndex, $event.node)"
      @open-search="openSearch"
      @go-home="onGoHome"
    >
      <template #search>
        <input
          ref="searchInputRef"
          v-model="searchQuery"
          type="search"
          class="search-input"
          :placeholder="$t('userSpace.searchPlaceholder')"
          autocomplete="off"
          @keydown="onSearchKeydown"
          @blur="onSearchBlur"
        />
      </template>
      <template #actions>
        <button class="bar-btn pill-btn" :title="$t('userSpace.addCatalogFolder')" @mousedown.prevent @click="pickingFromCatalog = true">
          <IconLibrary20Regular />
        </button>
        <button class="bar-btn pill-btn" :title="$t('userSpace.addComputerFolder')" @mousedown.prevent @click="addComputerFolder">
          <IconFolderAdd20Regular />
        </button>
      </template>
    </BookCatalogTitleBar>
    <!-- Shown while searching, with more than one root to choose between. -->
    <div v-if="isSearching && roots.length > 1" class="root-pills" role="group" :aria-label="$t('userSpace.rootPills')">
      <button
        class="root-pill"
        :class="{ active: !selectedRootKeys.length }"
        @mousedown.prevent
        @click="selectedRootKeys = []"
      >{{ $t('userSpace.allRoots') }}</button>
      <button
        v-for="root in roots"
        :key="userSpaceRootKey(root)"
        class="root-pill"
        :class="{ active: selectedRootKeys.includes(userSpaceRootKey(root)) }"
        :title="root.kind === 'computer' ? root.path : userSpaceStore.rootTitle(root)"
        data-testid="user-space-root-pill"
        @mousedown.prevent
        @click="toggleRootPill(userSpaceRootKey(root))"
      >{{ userSpaceStore.rootTitle(root) }}</button>
    </div>
    <MessageBanner v-if="notice" class="notice" :message="notice" @dismiss="notice = null" />
    <div ref="contentRef" class="user-space-content">
      <LoadingAnimation v-if="catalogLoading || !rootsLoaded" />
      <p v-else-if="isEmpty" class="empty">{{ $t('userSpace.empty') }}</p>
      <template v-else>
        <MessageBanner
          v-if="currentError && view !== 'tree'"
          class="notice"
          :message="$t('userSpace.folderReadError', { error: currentError })"
          :dismissible="false"
        />
        <component
          :is="activeViewComponent"
          ref="activeViewRef"
          v-show="!isSearching"
          v-bind="activeViewProps"
          :can-remove="canRemove"
          @select-book="onSelectBook"
          @select-file="onSelectFile"
          @enter-folder="onEnterFolder"
          @expand-folder="onExpandFolder"
          @collapse-folder="onCollapseFolder"
          @remove-folder="onRemoveFolder"
        />
        <template v-if="isSearching">
          <MessageBanner
            v-if="isIndexUnavailable && indexStatusText && !searchItems.length"
            class="notice"
            :message="indexStatusText"
            :dismissible="false"
          />
          <LoadingAnimation v-else-if="searchBusy && !searchItems.length" :text="indexStatusText ?? undefined" />
          <BookCatalogSearch
            v-else
            ref="searchResultsRef"
            :items="searchItems"
            :view="searchResultsView"
            :searching="searchBusy"
            @select-book="onSelectBook"
            @select-toc="onSelectToc"
            @select-file="onSelectFile"
          />
        </template>
      </template>
    </div>
    <UserSpaceCatalogPicker v-if="pickingFromCatalog" @pick="addCatalogFolder" @pick-all="addWholeCatalog" @cancel="pickingFromCatalog = false" />
    <ConfirmDialog
      v-if="removing"
      :title="$t('userSpace.removeConfirm', { title: removing.title })"
      :desc="$t('userSpace.removeConfirmDesc')"
      @confirm="confirmRemove"
      @cancel="removing = null"
    />
  </div>
</template>

<style scoped>
.user-space-page {
  display: flex;
  flex-direction: column;
  height: 100%;
  background: var(--bg-primary);
}
.matches-grid {
  padding-inline: calc((100% - var(--catalog-content-width)) / 2);
}
.user-space-content {
  flex: 1;
  overflow: hidden;
  position: relative;
}
.empty {
  padding: 32px 16px;
  color: var(--text-secondary);
  font-size: 14px;
  text-align: center;
}
/* Root pills: the halachic-units page's chip look, in a wrapping row under the bar. */
.root-pills {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  padding: 6px 12px 2px;
}
.root-pill {
  height: 26px;
  max-width: 220px;
  padding: 0 12px;
  background: var(--bg-primary);
  border: 1px solid var(--border-color);
  border-radius: 13px;
  color: var(--text-secondary);
  font: inherit;
  font-size: 12px;
  cursor: pointer;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.root-pill:hover {
  color: var(--text-primary);
  border-color: var(--text-secondary);
}
.root-pill.active {
  background: color-mix(in srgb, var(--accent-color) 15%, var(--bg-primary));
  border-color: var(--accent-color);
  color: var(--accent-color);
  font-weight: 600;
}
.notice {
  margin: 8px 12px 0;
}
/* Same scale as the view menu beside them (BookCatalogTitleBar .pill-btn). */
.pill-btn {
  width: 20px;
  height: 20px;
  padding: 0;
}
</style>
