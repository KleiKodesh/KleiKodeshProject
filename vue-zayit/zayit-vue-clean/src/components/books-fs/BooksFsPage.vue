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
import type { BookRow } from './booksFsTree'
import type { TocFsItem } from './useBooksFsSearch'
import { useTabStore } from '@/stores/tabStore'

const tabStore = useTabStore()
const { loading, error, path, searchQuery, isSearching, treeItems, searchItems, tocSearching, load, enter, navigateTo } = useBooksFs()

const view = ref<'list' | 'tiles' | 'tree'>(tabStore.getBooksView())
const fullTreeRef = ref<InstanceType<typeof BooksFullTree> | null>(null)
const searchInputRef = ref<HTMLInputElement | null>(null)

const PLACEHOLDERS = ['חיפוש ספר...', 'בראשית פרק ד', 'בבלי ברכות דף יד']
const placeholder = ref(PLACEHOLDERS[0]!)

let phraseIdx = 0
let charIdx = 0
let pauseTicks = 0

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

watch(searchQuery, (val) => {
  if (val) pauseTyping()
  else resumeTyping()
})

function setView(v: 'list' | 'tiles' | 'tree') {
  view.value = v
  tabStore.setBooksView(v)
}

onMounted(() => {
  load()
  nextTick(() => searchInputRef.value?.focus())
})

function onSelectBook(book: BookRow) {
  tabStore.updateActiveTab({ title: book.title, route: '/book-view', bookId: book.id, openToc: true })
}

function onSelectToc(item: TocFsItem) {
  tabStore.updateActiveTab({ title: item.book.title, route: '/book-view', bookId: item.book.id, openTocEntryId: item.tocEntryId, openTocLineIndex: item.tocLineIndex ?? undefined })
}

function onSearchEnter() {
  if (isSearching.value && searchItems.value.length === 1) {
    const item = searchItems.value[0]
    if (item) onSelectBook(item.book)
  }
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
          v-show="view !== 'tree' && !isSearching"
          :items="treeItems"
          :view="view === 'tree' ? 'list' : view"
          @select-book="onSelectBook"
          @enter-folder="enter"
        />
        <template v-if="isSearching">
          <LoadingAnimation v-if="tocSearching" />
          <BooksSearchResults
            v-else
            :items="searchItems"
            :view="view"
            @select-book="onSelectBook"
            @select-toc="onSelectToc"
          />
        </template>
      </template>
    </div>

    <div class="search-bar">
      <div class="search-inner">
        <IconSearch20Regular class="search-icon" />
        <input ref="searchInputRef" v-model="searchQuery" type="search" class="search-input"
          :placeholder="placeholder"
          @keydown.enter="onSearchEnter" />
      </div>
    </div>
  </div>
</template>

<style scoped>
.books-page { display: flex; flex-direction: column; height: 100%; background: var(--bg-primary); }
.books-content { flex: 1; overflow: hidden; position: relative; }

.search-bar {
  padding: 5px 10px 6px;
  background: var(--bg-secondary);
  border-top: 1px solid var(--border-color);
}
.search-inner {
  display: flex;
  align-items: center;
  gap: 6px;
  background: color-mix(in srgb, var(--text-secondary) 12%, transparent);
  border-radius: 10px;
  padding: 6px 10px;
}
.search-icon { color: var(--text-secondary); flex-shrink: 0; }
.search-input {
  flex: 1;
  background: none;
  border: none;
  outline: none;
  font-size: 14px;
  color: var(--text-primary);
}
.search-input::placeholder { color: var(--text-secondary); }
.search-input::-webkit-search-cancel-button { filter: grayscale(1) opacity(0.4); }

.state.error { padding: 32px 16px; text-align: center; color: #ff3b30; font-size: 15px; }
</style>
