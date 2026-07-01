<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useVirtualizer } from '@tanstack/vue-virtual'
import { IconSearch20Regular, IconDismiss20Regular } from '@iconify-prerendered/vue-fluent'
import LoadingAnimation from '@/components/LoadingAnimation.vue'
import HebrewBooksListItem from './HebrewBooksListItem.vue'
import BottomSearchBar from '@/components/BottomSearchBar.vue'
import { useHebrewBooks } from './useHebrewBooks'
import { useVirtualListKeys } from '@/composables/useVirtualListKeyNav'
import { useVirtualScrollerKeys } from '@/composables/useVirtualScrollerKeys'
import { useLocalFileStore } from '@/stores/localFileStore'
import { storeToRefs } from 'pinia'

const localFileStore = useLocalFileStore()
const { downloadErrorMessage } = storeToRefs(localFileStore)

const {
  displayedBooks,
  isLoading,
  error,
  searchTerm,
  localFileBookIds,
  load,
  search,
  openBook,
  downloadBook,
  deleteLocalFile,
} = useHebrewBooks()

const searchInputRef = ref<HTMLInputElement>()
const scrollEl = ref<HTMLElement | null>(null)

const virtualizer = useVirtualizer(
  computed(() => ({
    count: displayedBooks.value.length,
    getScrollElement: () => scrollEl.value,
    estimateSize: () => 48,
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

onMounted(() => {
  load()
  searchInputRef.value?.focus()
})

function onBookClicked(i: number, book: (typeof displayedBooks.value)[number]) {
  focusedIndex.value = i
  openBook(book)
}
</script>

<template>
  <div class="hb-page">
    <div v-if="downloadErrorMessage" class="hb-error-banner">
      <span>{{ downloadErrorMessage }}</span>
      <button class="hb-error-dismiss" @click="downloadErrorMessage = null">
        <IconDismiss20Regular />
      </button>
    </div>
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
              :has-local-file="localFileBookIds.has(String(displayedBooks[vRow.index]!.id))"
              @book-clicked="onBookClicked(vRow.index, displayedBooks[vRow.index]!)"
              @download-clicked="downloadBook"
              @delete-clicked="deleteLocalFile"
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
        placeholder="חפש ספרים, מחברים או נושאים..."
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
.search-input::-webkit-search-cancel-button {
  filter: grayscale(1) opacity(0.4);
}

.hb-error-banner {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  padding: 8px 12px;
  background: color-mix(in srgb, #e53e3e 12%, var(--bg-secondary));
  border-bottom: 1px solid color-mix(in srgb, #e53e3e 30%, transparent);
  color: var(--text-primary);
  font-size: 13px;
}

.hb-error-dismiss {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 24px;
  height: 24px;
  padding: 0;
  color: var(--text-secondary);
  flex-shrink: 0;
}
</style>
