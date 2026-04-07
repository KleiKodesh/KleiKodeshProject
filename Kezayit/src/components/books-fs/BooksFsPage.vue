<script setup lang="ts">
import { ref, onMounted, nextTick, watch } from 'vue'
import { useIntervalFn } from '@vueuse/core'
import { IconSearch20Regular } from '@iconify-prerendered/vue-fluent'
import { useBooksFs } from './useBooksFs'
import BooksFsTitleBar from './BooksFsTitleBar.vue'
import BooksTreeView from './BooksTreeView.vue'
import BooksFullTree from './BooksFullTree.vue'
import BooksSearchResults from './BooksSearchResults.vue'
import LoadingAnimation from '@/components/common/LoadingAnimation.vue'
import BottomSearchBar from '@/components/common/BottomSearchBar.vue'
import { useTabStore } from '@/stores/tabStore'
import type { BookRow } from './booksCategoryTree'
import type { TocFsItem } from './useBooksFsSearch'

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
} = useBooksFs()

const view = ref<'list' | 'tiles' | 'tree'>('list')
onMounted(async () => {
  view.value = await tabStore.getBooksView()
})
const fullTreeRef = ref<InstanceType<typeof BooksFullTree> | null>(null)
const booksTreeRef = ref<InstanceType<typeof BooksTreeView> | null>(null)
const searchResultsRef = ref<InstanceType<typeof BooksSearchResults> | null>(null)
const searchInputRef = ref<HTMLInputElement | null>(null)

function focusList() {
  if (isSearching.value) {
    searchResultsRef.value?.focusContainer()
    return
  }
  if (view.value === 'tree') {
    fullTreeRef.value?.containerRef?.focus()
    return
  }
  booksTreeRef.value?.focusContainer()
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

function setView(v: 'list' | 'tiles' | 'tree') {
  view.value = v
  tabStore.setBooksView(v)
}

onMounted(() => {
  load()
  nextTick(() => searchInputRef.value?.focus())
})

function onSelectBook(book: BookRow) {
  tabStore.updateActiveTab({
    title: book.title,
    route: '/book-view',
    bookId: book.id,
    openToc: true,
  })
}
function onSelectToc(item: TocFsItem) {
  tabStore.updateActiveTab({
    title: item.book.title,
    route: '/book-view',
    bookId: item.book.id,
    openTocEntryId: item.tocEntryId,
    openTocLineIndex: item.tocLineIndex ?? undefined,
  })
}
function onSearchEnter() {
  if (isSearching.value && searchItems.value.length === 1) onSelectBook(searchItems.value[0]!.book)
}
</script>

<template>
  <div class="books-page">
    <BooksFsTitleBar
      :view="view"
      :path="path"
      :is-searching="isSearching"
      @set-view="setView"
      @navigate="navigateTo"
      @reset="fullTreeRef?.reset()"
    />
    <div class="books-content">
      <LoadingAnimation v-if="loading" />
      <div v-else-if="error" class="state error">{{ error }}</div>
      <template v-else>
        <BooksFullTree
          ref="fullTreeRef"
          v-show="view === 'tree' && !isSearching"
          @select-book="onSelectBook"
        />
        <BooksTreeView
          ref="booksTreeRef"
          v-show="view !== 'tree' && !isSearching"
          :items="treeItems"
          :view="view === 'tree' ? 'list' : view"
          @select-book="onSelectBook"
          @enter-folder="enter"
        />
        <template v-if="isSearching">
          <LoadingAnimation v-if="tocSearching" />
          <BooksSearchResults
            ref="searchResultsRef"
            v-else
            :items="searchItems"
            :view="view"
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
  color: #ff3b30;
  font-size: 15px;
}
</style>
