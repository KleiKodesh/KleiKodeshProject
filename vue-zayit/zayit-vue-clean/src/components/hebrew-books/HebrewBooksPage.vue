<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import { IconSearch20Regular } from '@iconify-prerendered/vue-fluent'
import LoadingAnimation from '@/components/common/LoadingAnimation.vue'
import HebrewBooksListItem from './HebrewBooksListItem.vue'
import { useHebrewBooks } from './useHebrewBooks'
import { useListKeys } from '@/composables/useListKeys'

const {
  displayedBooks, isLoading, error, searchTerm, isOnline,
  load, search, openBook, downloadBook,
} = useHebrewBooks()

const searchInputRef = ref<HTMLInputElement>()
const listEl = ref<HTMLElement | null>(null)

const { focusedIndex, containerFocused } = useListKeys(
  listEl,
  () => displayedBooks.value.length,
  (i) => openBook(displayedBooks.value[i]!),
)

function updateOnline() { isOnline.value = navigator.onLine }

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
</script>

<template>
  <div class="hb-page">
    <!-- List -->
    <div ref="listEl" class="hb-list" tabindex="0">
      <LoadingAnimation v-if="isLoading" />

      <div v-else-if="error" class="state">{{ error }}</div>

      <template v-else-if="displayedBooks.length">
        <HebrewBooksListItem
          v-for="(book, i) in displayedBooks"
          :key="book.id"
          :book="book"
          :focused="containerFocused && focusedIndex === i"
          @book-clicked="focusedIndex = i; openBook(book)"
          @download-clicked="downloadBook"
        />
      </template>

      <div v-else class="state">
        <span class="state-icon">📚</span>
        <span v-if="searchTerm">לא נמצאו ספרים</span>
        <span v-else>אין היסטוריה — חפש ספר להתחיל</span>
      </div>
    </div>

    <!-- Search bar -->
    <div class="search-bar">
      <div class="search-inner">
        <IconSearch20Regular class="search-icon" />
        <input
          ref="searchInputRef"
          :value="searchTerm"
          type="search"
          :placeholder="isOnline ? 'חפש ספרים, מחברים או נושאים...' : 'נדרש חיבור לאינטרנט'"
          :disabled="!isOnline"
          class="search-input"
          dir="rtl"
          @input="search(($event.target as HTMLInputElement).value)"
          @keydown.up.prevent="listEl?.focus()"
          @keydown.down.prevent="listEl?.focus()"
          @keydown.tab.prevent="listEl?.focus()"
        />
      </div>
    </div>
  </div>
</template>

<style scoped>
.hb-page { display: flex; flex-direction: column; height: 100%; background: var(--bg-primary); }

.hb-list {
  flex: 1;
  overflow-y: auto;
  overflow-x: hidden;
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
.state-icon { font-size: 40px; opacity: 0.5; }

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
  direction: rtl;
}
.search-input::placeholder { color: var(--text-secondary); }
.search-input:disabled { opacity: 0.5; cursor: not-allowed; }
.search-input::-webkit-search-cancel-button { filter: grayscale(1) opacity(0.4); }

.downloading-overlay {
  position: absolute;
  inset: 0;
  background: color-mix(in srgb, var(--bg-primary) 80%, transparent);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 10;
}
</style>
