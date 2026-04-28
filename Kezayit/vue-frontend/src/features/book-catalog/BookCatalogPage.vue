<script setup lang="ts">
import { ref, computed, onMounted, nextTick, watch } from 'vue'
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
import { useTabStore } from '@/stores/tabStore'
import type { BookRow, CategoryNode } from '@/utils/booksCategoryTree'
import type { TocFsItem } from './useBookCatalogSearch'
import { getDiagnostics } from '@/host/bridge'
import type { ComponentPublicInstance } from 'vue'

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
} = useBookCatalog()

const view = ref<'list' | 'tiles' | 'tree'>('list')
onMounted(async () => {
  view.value = await tabStore.getBooksView()
})

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
    <BookCatalogTitleBar
      :view="view"
      :path="path"
      :is-searching="isSearching"
      @set-view="setView"
      @navigate="navigateTo"
      @reset="activeViewRef?.reset?.()"
    />
    <div class="books-content">
      <LoadingAnimation v-if="loading" />
      <div v-else-if="error" class="state error">
        <span class="error-msg">{{ error }}</span>
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
        <template v-if="isSearching">
          <LoadingAnimation v-if="tocSearching" />
          <BookCatalogSearch
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
.error-msg {
  display: block;
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
  color: #ff3b30;
  font-weight: 600;
}
.val-ok {
  color: #34c759;
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
