<script setup lang="ts">
import { computed, ref, reactive, provide, onMounted, onBeforeUnmount, nextTick, watch } from 'vue'
import { useDebounceFn, useIntervalFn } from '@vueuse/core'
import {
  IconMinimize20Regular,
  IconDismiss12Regular,
  IconChevronDoubleDown20Regular,
  IconChevronDoubleUp20Regular,
} from '@iconify-prerendered/vue-fluent'
import { useBooksDataStore } from '@/stores/booksDataStore'
import { normalize } from '@/utils/normalizeText'
import { normalizeBookPath } from '@/features/book-catalog/bookCatalogSearchNormalizer'
import { filterBooksByWords } from '@/features/book-catalog/bookCatalogSearch'
import LoadingAnimation from '@/components/LoadingAnimation.vue'
import FullTextSearchFilterNode from './FullTextSearchFilterNode.vue'
import FullTextSearchFilterBookList from './FullTextSearchFilterBookList.vue'
import { FILTER_EXPANSION_KEY } from './fullTextSearchFilterExpansion'
import type { CategoryNode } from '@/features/book-catalog/bookCatalogTree'
import type { BookRow } from '@/webview-host/queries.types'
const props = defineProps<{
  checkedBookIds: Set<number>
  resultCounts: Map<number, number>
  hasSearched?: boolean
  atFilters: string[]
}>()
const emit = defineEmits<{
  toggleBook: [number]
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
const filteredBooks = ref<BookRow[]>([])

onMounted(() => nextTick(() => searchInputRef.value?.focus()))

// ── Expand / collapse all ─────────────────────────────────────────────────────
// Expansion is owned here as one reactive Set and shared with every node via
// provide/inject. Nodes render children only when their id is in the set, so
// "expand all" can grow the tree gradually rather than in one blocking frame.
const expandedIds = reactive(new Set<number>())
const allExpanded = ref(false)

provide(FILTER_EXPANSION_KEY, {
  isExpanded: (id: number) => expandedIds.has(id),
  toggle: (id: number) => {
    if (expandedIds.has(id)) expandedIds.delete(id)
    else expandedIds.add(id)
  },
})

// Pre-order list of every category that has something to reveal. Pre-order means
// a parent always precedes its descendants, so revealing ids in order fills the
// tree top-down.
function collectExpandableIds(nodes: CategoryNode[], out: number[]): number[] {
  for (const node of nodes) {
    if (node.children.length || node.books.length) out.push(node.id)
    collectExpandableIds(node.children, out)
  }
  return out
}

let expandRaf: number | null = null
function stopExpanding() {
  if (expandRaf !== null) {
    cancelAnimationFrame(expandRaf)
    expandRaf = null
  }
}

// Reveal ids in rAF-batched chunks so the browser paints between batches and the
// UI stays responsive even for a catalog with thousands of entries.
function expandAll() {
  stopExpanding()
  allExpanded.value = true
  const ids = collectExpandableIds(booksStore.ROOT.children, [])
  let i = 0
  const BATCH = 150
  const step = () => {
    const end = Math.min(i + BATCH, ids.length)
    for (; i < end; i++) expandedIds.add(ids[i]!)
    expandRaf = i < ids.length ? requestAnimationFrame(step) : null
  }
  expandRaf = requestAnimationFrame(step)
}

function collapseAll() {
  stopExpanding()
  expandedIds.clear()
  allExpanded.value = false
}

function toggleExpandAll() {
  if (allExpanded.value) collapseAll()
  else expandAll()
}

onBeforeUnmount(stopExpanding)

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
const isAllChecked = computed(() => total.value > 0 && props.checkedBookIds.size === total.value)
const isIndet = computed(
  () => props.checkedBookIds.size > 0 && props.checkedBookIds.size < total.value,
)

// Show book list when there are committed tokens OR the current input is long enough
const activeQuery = computed(() => inputText.value.trim())
const isSearching = computed(() => props.atFilters.length > 0 || activeQuery.value.length >= 2)

// Union of all committed tokens + current input text (if long enough)
function computeFilteredBooks(tokens: string[], currentInput: string): BookRow[] {
  const allTokens = [
    ...tokens,
    ...(currentInput.trim().length >= 2 ? [currentInput.trim()] : []),
  ]
  if (!allTokens.length) return []
  const seen = new Set<number>()
  const result: BookRow[] = []
  for (const token of allTokens) {
    const words = normalizeBookPath(normalize(token.trim())).split(/\s+/).filter((w) => w.length > 0)
    for (const book of filterBooksByWords(booksStore.allBooks, words)) {
      if (!seen.has(book.id)) {
        seen.add(book.id)
        result.push(book)
      }
    }
  }
  return result
}

const runSearch = useDebounceFn(() => {
  const books = computeFilteredBooks(props.atFilters, inputText.value)
  filteredBooks.value = props.hasSearched
    ? books.filter((b) => (props.resultCounts.get(b.id) ?? 0) > 0)
    : books
}, 150)

watch(
  [() => props.atFilters, inputText, () => props.hasSearched, () => props.resultCounts],
  () => runSearch(),
  { immediate: true },
)

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
        <span class="panel-title">בחר הכל</span>
      </div>
      <button
        v-if="!isSearching && !booksStore.loading"
        class="expand-all-btn c-pointer hover-bg"
        :title="allExpanded ? 'כווץ הכל' : 'הרחב הכל'"
        @click.stop="toggleExpandAll"
      >
        <IconChevronDoubleUp20Regular v-if="allExpanded" />
        <IconChevronDoubleDown20Regular v-else />
      </button>
      <button class="close-btn c-pointer hover-bg" title="סגור" @click.stop="emit('close')">
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
          @toggle-book="emit('toggleBook', $event)"
          @navigate-to-book="emit('navigateToBook', $event)"
        />
        <div v-else class="tree-scroll">
          <FullTextSearchFilterNode
            v-for="cat in booksStore.ROOT.children"
            :key="cat.id"
            :category="cat"
            :checked-book-ids="checkedBookIds"
            :result-counts="resultCounts"
            :has-searched="hasSearched"
            @toggle-book="emit('toggleBook', $event)"
            @toggle-category="(c, v) => emit('toggleCategory', c, v)"
            @navigate-to-book="emit('navigateToBook', $event)"
          />
        </div>
      </template>
    </div>

    <div class="panel-search">
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
/* A menu panel, not a wall: the app's dropdown surface (--bg-secondary + 1px border +
   4px radius + drop shadow, per AppTitleBarNavDropdown) inset from the edges of the area
   it fills, so it reads as a sheet floating over the results rather than a second column
   fused to them. It still fills the full height of that area - the inset is a margin, and
   the list inside is what grows. */
.panel {
  position: absolute;
  right: 0;
  top: 0;
  bottom: 0;
  z-index: 10;
  display: flex;
  flex-direction: column;
  min-width: 180px;
  max-width: 300px;
  margin: 6px;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 6px;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.18);
  /* The rounded corners have to clip the header band and the scrolling list, or both
     square themselves off against the radius. */
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
  overflow-y: auto;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
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
  direction: rtl;
  padding: 0;
}
.search-input::placeholder {
  color: var(--text-secondary);
}
</style>
