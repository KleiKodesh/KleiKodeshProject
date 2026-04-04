<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useVirtualizer } from '@tanstack/vue-virtual'
import { IconSearch20Regular } from '@iconify-prerendered/vue-fluent'
import LoadingAnimation from '@/components/common/LoadingAnimation.vue'
import HebrewBooksListItem from './HebrewBooksListItem.vue'
import BottomSearchBar from '@/components/common/BottomSearchBar.vue'
import { useHebrewBooks } from './useHebrewBooks'
import { useVirtualListKeys } from '@/composables/useVirtualListKeyNav'
import { useVirtualScrollerKeys } from '@/composables/useVirtualScrollerKeys'

const {
  displayedBooks,
  isLoading,
  error,
  searchTerm,
  isOnline,
  load,
  search,
  openBook,
  downloadBook,
} = useHebrewBooks()

const searchInputRef = ref<HTMLInputElement>()
const scrollEl = ref<HTMLElement | null>(null)

const virtualizer = useVirtualizer(
  computed(() => ({
    count: displayedBooks.value.length,
    getScrollElement: () => scrollEl.value,
    estimateSize: () => 64,
    overscan: 8,
  })),
)

const { focusedIndex, containerFocused } = useVirtualListKeys(
  scrollEl,
  () =>
    virtualizer.value as unknown as import('@tanstack/vue-virtual').Virtualizer<Element, Element>,
  () => displayedBooks.value.length,
  (i) => openBook(displayedBooks.value[i]!),
)

useVirtualScrollerKeys(
  scrollEl,
  () =>
    virtualizer.value as unknown as import('@tanstack/vue-virtual').Virtualizer<Element, Element>,
  () => displayedBooks.value.length,
)

function updateOnline() {
  isOnline.value = navigator.onLine
}

onMounted(() => {
  load()
  window.addEventListener('online', updateOnline)
  window.addEventListener('offline', updateOnline)
  searchInputRef.value?.focus()
})

onUnmounted(() => {
  window.removeEventListener('online', updateOnline)
  window.removeEventListener('offline', updateOnline)
})

function onBookClicked(i: number, book: (typeof displayedBooks.value)[number]) {
  focusedIndex.value = i
  openBook(book)
}
</script>

<template>
  <div class="hb-page">
    <div ref="scrollEl" class="hb-list" tabindex="0">
      <LoadingAnimation v-if="isLoading" />

      <div v-else-if="error" class="state">{{ error }}</div>

      <template v-else-if="displayedBooks.length">
        <div :style="{ height: `${virtualizer.getTotalSize()}px`, position: 'relative' }">
          <div
            v-for="vRow in virtualizer.getVirtualItems()"
            :key="String(vRow.key)"
            :ref="(el) => el && virtualizer.measureElement(el as Element)"
            :data-index="vRow.index"
            :style="{
              position: 'absolute',
              top: 0,
              left: 0,
              right: 0,
              transform: `translateY(${vRow.start}px)`,
            }"
          >
            <HebrewBooksListItem
              :book="displayedBooks[vRow.index]!"
              :focused="containerFocused && focusedIndex === vRow.index"
              @book-clicked="onBookClicked(vRow.index, displayedBooks[vRow.index]!)"
              @download-clicked="downloadBook"
            />
          </div>
        </div>
      </template>

      <div v-else class="state">
        <span class="state-icon">📚</span>
        <span v-if="searchTerm">לא נמצאו ספרים</span>
        <span v-else>אין היסטוריה — חפש ספר להתחיל</span>
      </div>
    </div>

    <BottomSearchBar>
      <template #left><IconSearch20Regular class="search-icon" /></template>
      <input
        ref="searchInputRef"
        :value="searchTerm"
        type="search"
        :placeholder="isOnline ? 'חפש ספרים, מחברים או נושאים...' : 'נדרש חיבור לאינטרנט'"
        :disabled="!isOnline"
        class="search-input"
        dir="rtl"
        @input="search(($event.target as HTMLInputElement).value)"
        @keydown.up.prevent="scrollEl?.focus()"
        @keydown.down.prevent="scrollEl?.focus()"
        @keydown.tab.prevent="scrollEl?.focus()"
      />
    </BottomSearchBar>
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
.search-input {
  flex: 1;
  background: none;
  border: none;
  outline: none;
  font-size: 13px;
  color: var(--text-primary);
  direction: rtl;
}
.search-input::placeholder {
  color: var(--text-secondary);
}
.search-input:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
.search-input::-webkit-search-cancel-button {
  filter: grayscale(1) opacity(0.4);
}
</style>
