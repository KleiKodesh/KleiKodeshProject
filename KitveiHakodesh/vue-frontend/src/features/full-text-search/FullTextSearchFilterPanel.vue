<script setup lang="ts">
import { computed, ref, shallowRef, markRaw, onMounted, nextTick, watch } from 'vue'
import { useIntervalFn } from '@vueuse/core'
import {
  IconMinimize20Regular,
  IconDismiss12Regular,
  IconChevronDoubleDown20Regular,
  IconChevronDoubleUp20Regular,
} from '@iconify-prerendered/vue-fluent'
import { useBooksDataStore } from '@/stores/booksDataStore'
import { searchBookTitleIds } from '@/features/book-catalog/bookTitleSearch'
import LoadingAnimation from '@/components/LoadingAnimation.vue'
import TreeView from '@/components/TreeView.vue'
import type { TreeNodeItem } from '@/components/treeTypes'
import FullTextSearchFilterRow from './FullTextSearchFilterRow.vue'
import FullTextSearchFilterBookList from './FullTextSearchFilterBookList.vue'
import {
  categoryCheckStates,
  categoryResultTotals,
  createFilterTreeRows,
  expandableCategoryRowIds,
  type FilterTreeRow,
} from './fullTextSearchFilterTree'
import type { CategoryNode } from '@/features/book-catalog/bookCatalogTree'
import type { BookRow } from '@/webview-host/queries.types'
const props = withDefaults(defineProps<{
  checkedBookIds: Set<number>
  resultCounts: Map<number, number>
  hasSearched?: boolean
  atFilters: string[]
  /**
   * The tree's top rows. Absent = the library catalog. User space passes its own roots,
   * whose computer folders load when opened (`expandNode` / `collapseNode`).
   */
  nodes?: CategoryNode[]
  /** User space: the checked computer files, by path. */
  checkedFilePaths?: Set<string>
  /** User space: unchecked computer folders, by node id (an unopened one has no files to tell). */
  uncheckedFolderIds?: Set<number>
  /** The select-all box's state, for a tree that is not the catalog. Absent = counted over the catalog's books. */
  selectAllState?: { checked: boolean; indet: boolean }
  /** The footer's @ field, which picks books by title from the catalog. Off for user space. */
  bookTitleSearch?: boolean
}>(), { bookTitleSearch: true })
const emit = defineEmits<{
  toggleBook: [number]
  toggleFile: [string]
  expandNode: [CategoryNode]
  collapseNode: [CategoryNode]
  toggleCategory: [CategoryNode, boolean]
  checkAll: []
  checkVisible: [number[]]
  uncheckAll: []
  close: []
  'update:atFilters': [string[]]
  navigateToBook: [number]
}>()

const booksStore = useBooksDataStore()

const bookListRef = ref<InstanceType<typeof FullTextSearchFilterBookList> | null>(null)
const searchInputRef = ref<HTMLInputElement | null>(null)
const inputText = ref('')
const filteredBooks = shallowRef<BookRow[]>(markRaw([]))

onMounted(() => nextTick(() => searchInputRef.value?.focus()))

// ── The tree ──────────────────────────────────────────────────────────────────
// TreeView draws the rows through the windowed scroller: only the rows on screen are in
// the DOM, so expanding everything is one list change, not thousands of mounted rows.
// Totals and check states are worked out once per change for the whole tree.
const treeViewRef = ref<InstanceType<typeof TreeView> | null>(null)
const allExpanded = ref(false)

const treeNodes = computed(() => props.nodes ?? booksStore.ROOT.children)
// A tree that loads folders as they open has no whole to expand at once.
const isLazyTree = computed(() => props.nodes !== undefined)

const resultTotals = computed(() => categoryResultTotals(treeNodes.value, props.resultCounts))
const checkStates = computed(() =>
  categoryCheckStates(treeNodes.value, props.checkedBookIds, props.checkedFilePaths, props.uncheckedFolderIds),
)
const buildRows = createFilterTreeRows()
const treeRows = computed(() =>
  buildRows(treeNodes.value, !!props.hasSearched, props.resultCounts, resultTotals.value),
)

function rowCount(row: FilterTreeRow): number {
  const { item } = row
  if (item.kind === 'category') return resultTotals.value.get(item.category.id) ?? 0
  return item.kind === 'book' ? (props.resultCounts.get(item.book.id) ?? 0) : 0
}
function rowChecked(row: FilterTreeRow): boolean {
  const { item } = row
  if (item.kind === 'category') return checkStates.value.get(item.category.id)?.checked ?? false
  if (item.kind === 'book') return props.checkedBookIds.has(item.book.id)
  return !!props.checkedFilePaths?.has(item.file.path)
}
function rowIndet(row: FilterTreeRow): boolean {
  return row.item.kind === 'category' && (checkStates.value.get(row.item.category.id)?.indet ?? false)
}

function onRowToggled(node: TreeNodeItem, expanded: boolean) {
  const { item } = node as FilterTreeRow
  if (item.kind !== 'category') return
  if (expanded) emit('expandNode', item.category)
  else emit('collapseNode', item.category)
}

function onRowCheck(row: FilterTreeRow) {
  const { item } = row
  if (item.kind === 'category') emit('toggleCategory', item.category, !rowChecked(row))
  else if (item.kind === 'book') emit('toggleBook', item.book.id)
  else emit('toggleFile', item.file.path)
}

function firstNavigableBookId(cat: CategoryNode): number | null {
  // Prefer a book that has results (when search has run), otherwise first book.
  // Subcategories before own books — the catalog's order.
  for (const child of cat.children) {
    const found = firstNavigableBookId(child)
    if (found != null) return found
  }
  for (const book of cat.books) {
    if (!props.hasSearched || props.resultCounts.get(book.id)) return book.id
  }
  return null
}

/** A title clicked, or Enter on the keyboard's row: go to its first result. Space checks the row. */
function onRowSelect(node: TreeNodeItem, event?: MouseEvent | KeyboardEvent) {
  const row = node as FilterTreeRow
  if (event instanceof KeyboardEvent && event.code === 'Space') {
    onRowCheck(row)
    return
  }
  const { item } = row
  if (item.kind === 'book') emit('navigateToBook', item.book.id)
  else if (item.kind === 'category') {
    const id = firstNavigableBookId(item.category)
    if (id != null) emit('navigateToBook', id)
  }
}

function toggleExpandAll() {
  allExpanded.value = !allExpanded.value
  treeViewRef.value?.setExpanded(allExpanded.value ? expandableCategoryRowIds(treeRows.value) : [])
}

function onSelectAllClick() {
  if (isAllChecked.value) {
    emit('uncheckAll')
  } else if (isSearching.value) {
    emit('checkVisible', filteredBooks.value.map((b) => b.id))
  } else {
    emit('checkAll')
  }
}

// ── Animated placeholder ──────────────────────────────────────────────────────

const PLACEHOLDERS = ['רש"י @ רמב"ם', 'בבלי ברכות', 'תוספתא @ תנ"ך תורה']
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

// Pause when user is typing or tokens are committed
watch([inputText, () => props.atFilters.length], ([text, count]) => {
  if (text || count) pauseTyping()
  else resumeTyping()
})

const total = computed(() => booksStore.allBooks.length)
const isAllChecked = computed(() => props.selectAllState
  ? props.selectAllState.checked
  : total.value > 0 && props.checkedBookIds.size === total.value)
const isIndet = computed(() => props.selectAllState
  ? props.selectAllState.indet
  : props.checkedBookIds.size > 0 && props.checkedBookIds.size < total.value)

// Show book list when there are committed tokens OR the current input is long enough
const activeQuery = computed(() => inputText.value.trim())
const isSearching = computed(() => props.bookTitleSearch && (props.atFilters.length > 0 || activeQuery.value.length >= 2))

// The tree gives way to the book list while the @ field searches; it comes back with the
// same rows open. Read before the list replaces it (a pre-flush watcher runs before render).
const savedExpanded = shallowRef<number[]>([])
watch(isSearching, (searching) => {
  if (searching && treeViewRef.value) savedExpanded.value = treeViewRef.value.getExpanded()
})

// Books matching any committed token or the current input (if long enough), in the title
// search's rank order, token by token. Resolved async in the catalog index; the result-count
// filter below is applied separately so streaming result counts never re-query the index.
const matchedBooks = shallowRef<BookRow[]>(markRaw([]))
// Bumped on every new resolution, BEFORE any early return, so a slower earlier one is dropped.
let matchGeneration = 0

async function computeFilteredBooks(tokens: string[], currentInput: string, isStale: () => boolean): Promise<BookRow[] | null> {
  const allTokens = [
    ...tokens,
    ...(currentInput.trim().length >= 2 ? [currentInput.trim()] : []),
  ]
  if (!allTokens.length) return []
  const perToken = await Promise.all(allTokens.map((token) => searchBookTitleIds(token.trim(), isStale)))
  if (isStale()) return null
  const seen = new Set<number>()
  const result: BookRow[] = []
  for (const ids of perToken) {
    for (const id of ids ?? []) {
      const book = booksStore.allBooksMap.get(id)
      if (book && !seen.has(id)) {
        seen.add(id)
        result.push(book)
      }
    }
  }
  return result
}

function applyResultCountFilter() {
  const books = matchedBooks.value
  filteredBooks.value = markRaw(props.hasSearched
    ? books.filter((b) => (props.resultCounts.get(b.id) ?? 0) > 0)
    : books)
}

// The query the shown list answers. Until the newest one resolves the list is PENDING: it
// keeps the previous rows and never shows "no books". No debounce: the service supersedes an
// older title query itself, so each keystroke just asks again and the stale answer is dropped.
const queryKey = computed(() => JSON.stringify([props.atFilters, inputText.value.trim()]))
const resolvedKey = ref<string | null>(null)
const matchPending = computed(() => resolvedKey.value !== queryKey.value)

async function runSearch() {
  const generation = ++matchGeneration
  const key = queryKey.value
  const books = await computeFilteredBooks(props.atFilters, inputText.value, () => generation !== matchGeneration)
  if (!books) return
  matchedBooks.value = markRaw(books)
  applyResultCountFilter()
  resolvedKey.value = key
}

watch([() => props.atFilters, inputText], () => runSearch(), { immediate: true })
watch([() => props.hasSearched, () => props.resultCounts], applyResultCountFilter)

// ── Token management ──────────────────────────────────────────────────────────

function commitInput() {
  const text = inputText.value.trim()
  if (!text) return
  emit('update:atFilters', [...props.atFilters, text])
  inputText.value = ''
}

function removeToken(index: number) {
  const next = props.atFilters.filter((_, i) => i !== index)
  emit('update:atFilters', next)
  nextTick(() => searchInputRef.value?.focus())
}

function onInputKeydown(e: KeyboardEvent) {
  // Combobox model: focus stays here; arrows/paging move the book list's
  // highlight and Enter WITH a highlight toggles that book. An unconsumed
  // Enter falls through to committing the typed text as an @-token.
  if (bookListRef.value?.onSearchInputKeydown(e)) return
  if (e.key === 'Enter' || e.key === '@') {
    e.preventDefault()
    commitInput()
    return
  }
  if (e.key === 'Backspace' && inputText.value === '' && props.atFilters.length > 0) {
    e.preventDefault()
    removeToken(props.atFilters.length - 1)
    return
  }
  if (e.key === 'Escape') {
    e.preventDefault()
    emit('close')
  }
}
</script>

<template>
  <div class="panel" @keydown.esc.stop="emit('close')">
    <div class="panel-header">
      <div
        class="header-check"
        :class="{ checked: isAllChecked, indet: isIndet }"
        @click="onSelectAllClick"
      >
        <span class="check-col">
          <span class="check-mark">✓</span>
          <span class="dash-mark">–</span>
        </span>
        <span class="panel-title">{{ $t('fullTextSearch.filter.selectAll') }}</span>
      </div>
      <button
        v-if="!isSearching && !booksStore.loading && !isLazyTree"
        class="expand-all-btn c-pointer hover-bg"
        :title="allExpanded ? $t('fullTextSearch.filter.collapseAll') : $t('fullTextSearch.filter.expandAll')"
        @click.stop="toggleExpandAll"
      >
        <IconChevronDoubleUp20Regular v-if="allExpanded" />
        <IconChevronDoubleDown20Regular v-else />
      </button>
      <button class="close-btn c-pointer hover-bg" :title="$t('fullTextSearch.close')" @click.stop="emit('close')">
        <IconMinimize20Regular />
      </button>
    </div>

    <div class="panel-body">
      <LoadingAnimation v-if="booksStore.loading" />
      <template v-else>
        <FullTextSearchFilterBookList
          v-if="isSearching"
          ref="bookListRef"
          :books="filteredBooks"
          :checked-book-ids="checkedBookIds"
          :result-counts="resultCounts"
          :has-searched="hasSearched"
          :pending="matchPending"
          @toggle-book="emit('toggleBook', $event)"
          @navigate-to-book="emit('navigateToBook', $event)"
        />
        <div v-else class="tree-scroll">
          <TreeView
            ref="treeViewRef"
            :nodes="treeRows"
            :row-height="26"
            :initial-expanded="savedExpanded"
            nodes-update-in-place
            @toggle="onRowToggled"
            @select="onRowSelect"
          >
            <template #row="{ node, expanded, focused, toggle, select }">
              <FullTextSearchFilterRow
                :row="(node as FilterTreeRow)"
                :expanded="expanded"
                :focused="focused"
                :count="rowCount(node as FilterTreeRow)"
                :checked="rowChecked(node as FilterTreeRow)"
                :indet="rowIndet(node as FilterTreeRow)"
                @toggle="toggle"
                @select="select"
                @check="onRowCheck(node as FilterTreeRow)"
              />
            </template>
          </TreeView>
        </div>
      </template>
    </div>

    <div v-if="bookTitleSearch" class="panel-search">
      <div class="search-inner">
        <span
          v-for="(token, i) in atFilters"
          :key="i"
          class="token-pill"
        >
          {{ token }}
          <button class="pill-remove" @click.stop="removeToken(i)">
            <IconDismiss12Regular />
          </button>
        </span>
        <input
          ref="searchInputRef"
          v-model="inputText"
          type="text"
          class="search-input"
          :placeholder="atFilters.length ? '' : placeholder"
          @keydown="onInputKeydown"
        />
      </div>
    </div>
  </div>
</template>

<style scoped>
.panel {
  position: absolute;
  inset-inline-start: 0;
  top: 0;
  bottom: 0;
  z-index: 10;
  display: flex;
  flex-direction: column;
  min-width: 180px;
  max-width: 300px;
  /* Same surface as the book-view side panel: flush, full height, toolbar color,
     one edge line. */
  background: var(--bg-toolbar);
  --tree-bg: var(--bg-toolbar);
  border-inline-end: 1px solid var(--border-color);
  overflow: hidden;
}
/* Menu-height rows throughout (32px, matching the nav dropdown) - the old 26px band was
   sized for a toolbar strip and read as chrome rather than as the panel's own title. */
.panel-header {
  display: flex;
  align-items: center;
  height: 32px;
  padding-inline: 2px;
  border-bottom: 1px solid var(--border-color);
  flex-shrink: 0;
}
.header-check {
  display: flex;
  align-items: center;
  flex: 1;
  height: 26px;
  border-radius: 4px;
  cursor: pointer;
  font-size: 13px;
  font-weight: 600;
  color: var(--text-primary);
}
.header-check:hover {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}
.check-col {
  width: 28px;
  height: 26px;
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 11px;
  color: var(--accent-color);
}
.check-mark { display: none; }
.dash-mark  { display: none; }
.header-check.checked .check-mark { display: block; }
.header-check.indet   .dash-mark  { display: block; }
.panel-title { flex: 1; }
/* Rounded hover bands like every other menu control in the app; square ones fought the
   panel's own rounded corner right next to them. */
.close-btn,
.expand-all-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 26px;
  height: 26px;
  flex-shrink: 0;
  border-radius: 4px;
  color: var(--text-secondary);
}
.expand-all-btn {
  opacity: 0.55;
}
.expand-all-btn:hover {
  opacity: 1;
}
.expand-all-btn :deep(svg) {
  width: 14px;
  height: 14px;
}
.panel-body {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
  overflow: hidden;
}
.tree-scroll {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
}
/* The field is the panel's own footer, so it sits ON the panel surface rather than in a
   separate strip: no top rule, just breathing room around the pill. The rule was there to
   fence the field off from the tree when the panel had no edges of its own; now the
   panel's border does that job. */
.panel-search {
  padding: 6px;
  flex-shrink: 0;
}
.search-inner {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 4px;
  padding: 4px 8px;
  border-radius: 999px;
  background: var(--input-bg);
  border: 1px solid var(--border-color);
  min-height: 26px;
  cursor: text;
  /* Same inset the top search bar's pill carries (TopSearchBar), so the two fields on
     the page read as one control at two sizes. */
  box-shadow: inset 0 1px 1px color-mix(in srgb, var(--text-primary) 6%, transparent);
}
.search-inner:focus-within {
  border-color: var(--accent-color);
}
.token-pill {
  display: inline-flex;
  align-items: center;
  gap: 3px;
  padding: 0 5px 0 4px;
  height: 18px;
  border-radius: 999px;
  background: color-mix(in srgb, var(--accent-color) 18%, transparent);
  color: var(--accent-color);
  font-size: 11px;
  white-space: nowrap;
  flex-shrink: 0;
}
.pill-remove {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 12px;
  height: 12px;
  border-radius: 50%;
  color: var(--accent-color);
  opacity: 0.7;
  padding: 0;
}
.pill-remove:hover {
  opacity: 1;
  background: color-mix(in srgb, var(--accent-color) 25%, transparent);
}
.search-input {
  flex: 1;
  min-width: 60px;
  background: none;
  border: none;
  outline: none;
  font-size: 12px;
  color: var(--text-primary);
  padding: 0;
}
.search-input::placeholder {
  color: var(--text-secondary);
}
</style>
