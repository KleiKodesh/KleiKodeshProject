<script setup lang="ts">
import { ref, watch } from 'vue'
import IconBookRtl20 from '@/components/IconBookRtl20.vue'
import WindowedScroller from '@/components/WindowedScroller.vue'
import type { WindowedScrollerApi } from '@/components/windowedScrollerTypes'
import { useInputListNavigation } from '@/composables/useInputListNavigation'
import type { BookRow } from '@/webview-host/queries.types'
const props = defineProps<{
  books: BookRow[]
  checkedBookIds: Set<number>
  resultCounts: Map<number, number>
  hasSearched?: boolean
  /** The newest query has not answered yet: "no books" would be a guess, so show nothing. */
  pending?: boolean
}>()

const emit = defineEmits<{ toggleBook: [number]; navigateToBook: [number] }>()

// Windowed: a filter query can match thousands of books, so only the rows near the
// viewport exist.
const windowedScroller = ref<WindowedScrollerApi | null>(null)
// New results are a new list: re-keying the scroller starts it back at the top.
const generation = ref(0)

// Combobox model: focus stays in the panel's filter input; the panel forwards
// its keydown events here (onSearchInputKeydown). Enter with a highlight
// toggles the book's inclusion, same as clicking its checkbox.
const { activeIndex, onKeydown: onSearchInputKeydown } = useInputListNavigation({
  getCount: () => props.books.length,
  onActivate: (i) => {
    const book = props.books[i]
    if (book) emit('toggleBook', book.id)
  },
  scrollIntoView: (index) => windowedScroller.value?.revealIndex(index),
})

// Reset the highlight when the book list changes (new search query)
watch(() => props.books, () => {
  activeIndex.value = -1
  generation.value++
})

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
  <div v-if="!books.length && pending" class="empty" />
  <div v-else-if="!books.length" class="empty">{{ $t('fullTextSearch.filter.noBooks') }}</div>
  <div v-else class="scroller" dir="rtl">
    <WindowedScroller ref="windowedScroller" :key="generation" :count="books.length" class="windowed">
      <template #row="{ index }">
        <div
          v-if="books[index]"
          class="book-row"
          data-nav-item
          :class="{
            checked: checkedBookIds.has(books[index]!.id),
            focused: activeIndex === index,
          }"
        >
          <button class="checkbox-col" @click.stop="onBookRowCheckClick(index)">
            <span v-if="checkedBookIds.has(books[index]!.id)" class="check-mark">✓</span>
            <IconBookRtl20 v-else class="book-icon" />
          </button>
          <button class="book-title" :title="$t('fullTextSearch.filter.scrollToFirst')" @click.stop="onBookRowTitleClick(index)">
            <span class="title-line">
              <span class="title-text">{{ books[index]!.title }}</span>
              <span v-if="hasSearched && resultCounts.get(books[index]!.id)" class="count">({{ resultCounts.get(books[index]!.id) }})</span>
            </span>
            <span v-if="books[index]!.parentPath" class="path-text">{{ books[index]!.parentPath }}</span>
          </button>
        </div>
      </template>
    </WindowedScroller>
  </div>
</template>

<style scoped>
.scroller {
  flex: 1;
  min-height: 0;
  position: relative;
  outline: none;
}
/* Absolutely filled so the scroller has a definite height inside the flex column. */
.scroller > :deep(.windowed-scroller) {
  position: absolute;
  inset: 0;
  height: auto;
}
.book-row {
  display: flex;
  align-items: stretch;
  min-height: 40px;
  user-select: none;
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
