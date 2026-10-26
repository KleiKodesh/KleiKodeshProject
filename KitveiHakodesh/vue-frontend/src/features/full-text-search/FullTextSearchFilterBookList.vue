<script setup lang="ts">
import { ref, watch } from 'vue'
import IconBookRtl20 from '@/components/IconBookRtl20.vue'
import { useInputListNavigation } from '@/composables/useInputListNavigation'
import type { BookRow } from '@/webview-host/queries.types'
const props = defineProps<{
  books: BookRow[]
  checkedBookIds: Set<number>
  resultCounts: Map<number, number>
  hasSearched?: boolean
}>()

const emit = defineEmits<{ toggleBook: [number]; navigateToBook: [number] }>()

// A plain list: every book is a row, and the browser skips the layout and paint of the
// rows off screen (`content-visibility: auto` below) — no virtualizer, nothing to
// measure or position. A few thousand rows at most.
const scrollEl = ref<HTMLElement | null>(null)

// Combobox model: focus stays in the panel's filter input; the panel forwards
// its keydown events here (onSearchInputKeydown). Enter with a highlight
// toggles the book's inclusion, same as clicking its checkbox.
const { activeIndex, onKeydown: onSearchInputKeydown } = useInputListNavigation({
  getCount: () => props.books.length,
  onActivate: (i) => {
    const book = props.books[i]
    if (book) emit('toggleBook', book.id)
  },
  containerElement: scrollEl,
})

// Reset the highlight when the book list changes (new search query)
watch(() => props.books, () => { activeIndex.value = -1 })

function onBookRowCheckClick(index: number) {
  activeIndex.value = index
  const book = props.books[index]
  if (book) emit('toggleBook', book.id)
}

function onBookRowTitleClick(index: number) {
  activeIndex.value = index
  const book = props.books[index]
  if (book) emit('navigateToBook', book.id)
}

defineExpose({ onSearchInputKeydown })
</script>

<template>
  <div v-if="!books.length" class="empty">{{ $t('fullTextSearch.filter.noBooks') }}</div>
  <div v-else ref="scrollEl" class="scroller" dir="rtl">
    <div
      v-for="(book, index) in books"
      :key="book.id"
      class="book-row"
      data-nav-item
      :class="{
        checked: checkedBookIds.has(book.id),
        focused: activeIndex === index,
      }"
    >
      <button class="checkbox-col" @click.stop="onBookRowCheckClick(index)">
        <span v-if="checkedBookIds.has(book.id)" class="check-mark">✓</span>
        <IconBookRtl20 v-else class="book-icon" />
      </button>
      <button class="book-title" :title="$t('fullTextSearch.filter.scrollToFirst')" @click.stop="onBookRowTitleClick(index)">
        <span class="title-line">
          <span class="title-text">{{ book.title }}</span>
          <span v-if="hasSearched && resultCounts.get(book.id)" class="count">({{ resultCounts.get(book.id) }})</span>
        </span>
        <span v-if="book.parentPath" class="path-text">{{ book.parentPath }}</span>
      </button>
    </div>
  </div>
</template>

<style scoped>
.scroller {
  flex: 1;
  overflow-y: auto;
  overflow-x: hidden;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
  outline: none;
}
.book-row {
  display: flex;
  align-items: stretch;
  min-height: 40px;
  user-select: none;
  /* Rows off screen are neither laid out nor painted; the placeholder size keeps
     the scrollbar honest until a row is reached. */
  content-visibility: auto;
  contain-intrinsic-size: auto 44px;
}
.book-row.focused {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}

/* Checkbox button — fixed width, toggles inclusion */
.checkbox-col {
  width: 36px;
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 11px;
  color: var(--accent-color);
  padding: 0;
  background: none;
  border: none;
  cursor: pointer;
  border-radius: 0;
}
.checkbox-col:hover {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}
.checkbox-col:active { transform: none !important; }

.book-icon {
  width: 16px;
  height: 16px;
  color: #c1440e;
  opacity: 0.5;
}
.check-mark { font-size: 11px; }

/* Title button — fills the rest, clicking navigates to first result */
.book-title {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  justify-content: center;
  align-items: flex-start;
  gap: 1px;
  padding: 4px 8px 4px 4px;
  background: none;
  border: none;
  cursor: pointer;
  font-family: inherit;
  border-radius: 0;
  text-align: start;
}
.book-title:hover {
  background: color-mix(in srgb, var(--text-primary) 6%, transparent);
}
.book-title:active {
  background: color-mix(in srgb, var(--text-primary) 10%, transparent);
}

.title-line {
  display: flex;
  align-items: baseline;
  gap: 5px;
  min-width: 0;
  width: 100%;
}
.title-text {
  font-size: 12px;
  color: var(--text-primary);
  line-height: 1.3;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  min-width: 0;
  flex-shrink: 1;
}
.path-text {
  font-size: 10px;
  color: var(--text-secondary);
  opacity: 0.7;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  line-height: 1.3;
  width: 100%;
  text-align: start;
}
.count {
  font-size: 10px;
  color: var(--text-secondary);
  flex-shrink: 0;
}
.empty {
  padding: 16px 14px;
  font-size: 12px;
  color: var(--text-secondary);
  text-align: center;
}
</style>
