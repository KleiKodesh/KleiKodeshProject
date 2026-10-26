<script setup lang="ts">
import { ref, computed, watch, onMounted, onBeforeUnmount } from 'vue'
import { IconSearch20Regular } from '@iconify-prerendered/vue-fluent'
import MessageBanner from '@/components/MessageBanner.vue'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import LoadingAnimation from '@/components/LoadingAnimation.vue'
import HebrewBooksListItem from './HebrewBooksListItem.vue'
import TopSearchBar from '@/components/TopSearchBar.vue'
import { useHebrewBooks } from './useHebrewBooks'
import type { HebrewBook } from './hebrewBooksCatalog'
import { useInputListNavigation } from '@/composables/useInputListNavigation'
import { useLocalFileStore } from '@/stores/localFileStore'
import { useTabStore } from '@/stores/tabStore'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { storeToRefs } from 'pinia'

const localFileStore = useLocalFileStore()
const tabStore = useTabStore()
const paneNavigation = usePaneNavigation()
const { downloadErrorMessage } = storeToRefs(localFileStore)

// This tab's id, captured at mount, for per-tab query persistence.
const hebrewBooksTabId = paneNavigation.activeTabId

const {
  displayedBooks,
  isLoading,
  error,
  searchTerm,
  localFileBookIds,
  downloadingBookIds,
  load,
  search,
  openBook,
  downloadBook,
  deleteLocalFile,
  revealInFolder,
} = useHebrewBooks()

// The book whose delete button was clicked, awaiting the user's choice in the confirm
// dialog: delete permanently, move to the Recycle Bin, or cancel.
const pendingDeleteBook = ref<HebrewBook | null>(null)

function resolveDelete(toRecycleBin: boolean | null) {
  const book = pendingDeleteBook.value
  pendingDeleteBook.value = null
  if (book && toRecycleBin !== null) deleteLocalFile(book, toRecycleBin)
}

const searchInputRef = ref<HTMLInputElement>()
const scrollEl = ref<HTMLElement | null>(null)

// Combobox model: focus stays in the search input; its keydown moves the list
// highlight, Enter opens the highlighted book (Ctrl+Enter in a new tab).
const { activeIndex, onKeydown: onSearchInputKeydown } = useInputListNavigation({
  getCount: () => displayedBooks.value.length,
  onActivate: (i, openInNewTab) => openBook(displayedBooks.value[i]!, openInNewTab),
  containerElement: scrollEl,
})

// New results make the old highlight point at a different book — drop it.
watch(displayedBooks, () => {
  activeIndex.value = -1
})

// Restore the query saved on the tab so switching away and back keeps the input.
// A non-empty saved query runs the catalog search; otherwise load() shows history.
onMounted(() => {
  const saved = paneNavigation.activeTab.hebrewBooksSearchQuery
  if (saved) search(saved)
  else load()
  searchInputRef.value?.focus()
})

// On unmount, mirror the file-search / catalog rule:
//  • Tab still '/hebrewbooks' (tab switch, or a NEW tab opened via Ctrl+click) →
//    save the query so it restores when the user returns to this tab.
//  • Tab navigated in place to a book (startHbDownload set route '/pdf-view') →
//    clear it.
onBeforeUnmount(() => {
  const tab = tabStore.tabs.find((t) => t.id === hebrewBooksTabId)
  const stillHebrewBooks = tab?.route === '/hebrewbooks'
  tabStore.updateTab(hebrewBooksTabId, {
    hebrewBooksSearchQuery: stillHebrewBooks ? searchTerm.value || undefined : undefined,
  })
})

function onBookClicked(
  i: number,
  book: (typeof displayedBooks.value)[number],
  openInNewTab = false,
) {
  activeIndex.value = i
  openBook(book, openInNewTab)
}
</script>

<template>
  <div class="hb-page">
    <TopSearchBar>
      <template #left><IconSearch20Regular class="search-icon" /></template>
      <input
        ref="searchInputRef"
        :value="searchTerm"
        type="search"
        :placeholder="$t('hebrewBooks.searchPlaceholder')"
        class="search-input"
        @input="search(($event.target as HTMLInputElement).value)"
        @keydown="onSearchInputKeydown"
      />
    </TopSearchBar>

    <div ref="scrollEl" class="hb-list">
      <LoadingAnimation v-if="isLoading" />

      <div v-else-if="error" class="state">{{ error }}</div>

      <template v-else-if="displayedBooks.length">
        <!-- A plain list: rows off screen are neither laid out nor painted (.hb-row). -->
        <div v-for="(book, index) in displayedBooks" :key="book.id" class="hb-row">
          <HebrewBooksListItem
            :book="book"
            :focused="activeIndex === index"
            :has-local-file="localFileBookIds.has(String(book.id))"
            @book-clicked="(clicked, openInNewTab) => onBookClicked(index, clicked, openInNewTab)"
            :downloading="downloadingBookIds.has(String(book.id))"
            @download-clicked="downloadBook"
            @delete-clicked="pendingDeleteBook = $event"
            @reveal-clicked="revealInFolder"
          />
        </div>
      </template>

      <div v-else class="state">
        <span class="state-icon">📚</span>
        <span v-if="searchTerm">{{ $t('hebrewBooks.noBooksFound') }}</span>
        <span v-else>{{ $t('hebrewBooks.noHistory') }}</span>
      </div>
    </div>

    <MessageBanner
      v-if="downloadErrorMessage"
      class="hb-error-banner"
      :message="downloadErrorMessage"
      @dismiss="downloadErrorMessage = null"
    />

    <ConfirmDialog
      v-if="pendingDeleteBook"
      :title="$t('hebrewBooks.deleteConfirm.title', { title: pendingDeleteBook.title })"
      :desc="$t('hebrewBooks.deleteConfirm.warning')"
      :confirm-label="$t('hebrewBooks.deleteConfirm.moveToRecycleBin')"
      :extra-label="$t('hebrewBooks.deleteConfirm.deletePermanently')"
      @confirm="resolveDelete(true)"
      @extra="resolveDelete(false)"
      @cancel="resolveDelete(null)"
    />
  </div>
</template>

<style scoped>
.hb-page {
  display: flex;
  flex-direction: column;
  height: 100%;
  background: var(--bg-primary);
}

.hb-list {
  flex: 1;
  overflow-y: auto;
  overflow-x: hidden;
  outline: none;
}
.hb-row {
  content-visibility: auto;
  contain-intrinsic-size: auto 48px;
}

.state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 8px;
  height: 100%;
  color: var(--text-secondary);
  font-size: 14px;
  text-align: center;
  padding: 32px;
}
.state-icon {
  font-size: 40px;
  opacity: 0.5;
}

.search-icon {
  color: var(--text-secondary);
}
/* Fill, border, outline, colors, placeholder and the search-cancel button all come
   from the global `.search-inner input` rule; the type size comes from TopSearchBar. */
.search-input {
  flex: 1;
}

/* Docked under the list: squared top corners so it reads as part of the page edge. */
.hb-error-banner {
  border-radius: 0;
  border-inline: none;
  border-bottom: none;
}
</style>
