<script setup lang="ts">
import { computed, ref, onMounted, nextTick, watch } from 'vue'
import { useDebounceFn } from '@vueuse/core'
import { IconMinimize20Regular } from '@iconify-prerendered/vue-fluent'
import { useBooksDataStore } from '@/stores/booksDataStore'
import { normalize } from '@/utils/normalizeText'
import LoadingAnimation from '@/components/common/LoadingAnimation.vue'
import SearchFilterNode from './SearchFilterNode.vue'
import SearchFilterBookList from './SearchFilterBookList.vue'
import type { CategoryNode, BookRow } from '@/components/books-fs/booksCategoryTree'

const props = defineProps<{
  checkedBookIds: Set<number>
  resultCounts: Map<number, number>
  hasSearched?: boolean
  filterBookQuery: string
}>()
const emit = defineEmits<{
  toggleBook: [number]
  toggleCategory: [CategoryNode, boolean]
  checkAll: []
  uncheckAll: []
  close: []
  'update:filterBookQuery': [string]
}>()

const booksStore = useBooksDataStore()

const bookListRef = ref<InstanceType<typeof SearchFilterBookList> | null>(null)
const searchInputRef = ref<HTMLInputElement | null>(null)
const filteredBooks = ref<BookRow[]>([])

onMounted(() => nextTick(() => searchInputRef.value?.focus()))

const total = computed(() => booksStore.allBooks.length)
const isAllChecked = computed(() => total.value > 0 && props.checkedBookIds.size === total.value)
const isIndet = computed(
  () => props.checkedBookIds.size > 0 && props.checkedBookIds.size < total.value,
)

// Show the book list only when the debounced results have been computed
const isSearching = computed(() => props.filterBookQuery.trim().length >= 2)

function toWords(raw: string): string[] {
  return normalize(raw.trim()).split(/\s+/).filter((w) => w.length > 0)
}

function matchBooks(words: string[]): BookRow[] {
  if (!words.length) return []
  const exactWords = words.slice(0, -1)
  const prefixWord = words[words.length - 1]!
  return booksStore.allBooks.filter((b) => {
    const pathWords = b.searchWords ?? (b.searchPath ?? '').split(/\s+/)
    const exactOk = exactWords.every((qw) => pathWords.some((pw) => pw === qw))
    const prefixOk = pathWords.some((pw) => pw.includes(prefixWord))
    return exactOk && prefixOk
  })
}

const runSearch = useDebounceFn((q: string) => {
  if (q.trim().length < 2) {
    filteredBooks.value = []
    return
  }
  filteredBooks.value = matchBooks(toWords(q))
}, 200)

watch(() => props.filterBookQuery, (q) => runSearch(q), { immediate: true })
</script>

<template>
  <div class="panel" @keydown.esc="emit('close')">
    <div class="panel-header">
      <div
        class="header-check"
        :class="{ checked: isAllChecked, indet: isIndet }"
        @click="isAllChecked ? emit('uncheckAll') : emit('checkAll')"
      >
        <span class="check-col">
          <span class="check-mark">✓</span>
          <span class="dash-mark">–</span>
        </span>
        <span class="panel-title">בחר הכל</span>
      </div>
      <button class="close-btn c-pointer hover-bg" title="סגור" @click.stop="emit('close')">
        <IconMinimize20Regular />
      </button>
    </div>

    <div class="panel-body">
      <LoadingAnimation v-if="booksStore.loading" />
      <template v-else>
        <SearchFilterBookList
          v-if="isSearching"
          ref="bookListRef"
          :books="filteredBooks"
          :checked-book-ids="checkedBookIds"
          :result-counts="resultCounts"
          :has-searched="hasSearched"
          @toggle-book="emit('toggleBook', $event)"
        />
        <template v-else>
          <div class="tree-scroll">
            <SearchFilterNode
              v-for="cat in booksStore.ROOT.children"
              :key="cat.id"
              :category="cat"
              :checked-book-ids="checkedBookIds"
              :result-counts="resultCounts"
              :has-searched="hasSearched"
              @toggle-book="emit('toggleBook', $event)"
              @toggle-category="(c, v) => emit('toggleCategory', c, v)"
            />
          </div>
        </template>
      </template>
    </div>

    <div class="panel-search">
      <div class="search-inner">
        <input
          ref="searchInputRef"
          type="search"
          name="filter-book-search"
          class="search-input"
          placeholder="חיפוש ספר..."
          :value="filterBookQuery"
          @input="emit('update:filterBookQuery', ($event.target as HTMLInputElement).value)"
          @keydown.down.prevent="bookListRef?.focusList()"
          @keydown.up.prevent="bookListRef?.focusList()"
          @keydown.tab.prevent="bookListRef?.focusList()"
          @keydown.esc.prevent="emit('close')"
        />
      </div>
    </div>
  </div>
</template>

<style scoped>
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
  background: var(--bg-secondary);
  border-left: 1px solid var(--border-color);
}
.panel-header {
  display: flex;
  align-items: center;
  height: 26px;
  border-bottom: 1px solid var(--border-color);
  flex-shrink: 0;
}
.header-check {
  display: flex;
  align-items: center;
  flex: 1;
  height: 26px;
  cursor: pointer;
  font-size: 12px;
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
.check-mark {
  display: none;
}
.dash-mark {
  display: none;
}
.header-check.checked .check-mark {
  display: block;
}
.header-check.indet .dash-mark {
  display: block;
}
.panel-title {
  flex: 1;
}
.close-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 26px;
  height: 26px;
  flex-shrink: 0;
  border-radius: 0;
  color: var(--text-secondary);
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
.panel-search {
  padding: 5px 6px 6px;
  border-top: 1px solid var(--border-color);
  flex-shrink: 0;
}
.search-inner {
  display: flex;
  align-items: center;
  padding: 4px 8px;
  border-radius: 999px;
  background: var(--input-bg);
  border: 1px solid var(--border-color);
}
.search-input {
  flex: 1;
  width: 0;
  min-width: 0;
  background: none;
  border: none;
  outline: none;
  font-size: 12px;
  color: var(--text-primary);
  direction: rtl;
}
.search-input::placeholder {
  color: var(--text-secondary);
}
.search-input::-webkit-search-cancel-button {
  filter: grayscale(1) opacity(0.4);
}
</style>
