<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { useVirtualizer } from '@tanstack/vue-virtual'
import IconBookRtl20 from '@/components/IconBookRtl20.vue'
import { useVirtualListKeys } from '@/composables/useVirtualListKeyNav'
import type { BookRow } from '@/webview-host/queries.types'
const props = defineProps<{
  books: BookRow[]
  checkedBookIds: Set<number>
  resultCounts: Map<number, number>
  hasSearched?: boolean
}>()

const emit = defineEmits<{ toggleBook: [number]; navigateToBook: [number] }>()

const scrollEl = ref<HTMLElement | null>(null)

const virtualizer = useVirtualizer(
  computed(() => ({
    count: props.books.length,
    getScrollElement: () => scrollEl.value,
    estimateSize: () => 44,
    overscan: 8,
    measureElement: (el: Element) => el.getBoundingClientRect().height,
  })),
)

const { focusedIndex, containerFocused } = useVirtualListKeys(
  scrollEl,
  () => virtualizer.value as unknown as import('@tanstack/vue-virtual').Virtualizer<Element, Element>,
  () => props.books.length,
  (i) => {
    const book = props.books[i]
    if (book) emit('toggleBook', book.id)
  },
)

// Reset focus when the book list changes (new search query)
watch(() => props.books, () => { focusedIndex.value = -1 })

function onBookRowCheckClick(index: number) {
  focusedIndex.value = index
  const book = props.books[index]
  if (book) emit('toggleBook', book.id)
}

function onBookRowTitleClick(index: number) {
  focusedIndex.value = index
  const book = props.books[index]
  if (book) emit('navigateToBook', book.id)
}

function focusList() {
  scrollEl.value?.focus()
}

defineExpose({ focusList })
</script>

<template>
  <div v-if="!books.length" class="empty">לא נמצאו ספרים</div>
  <div v-else ref="scrollEl" class="scroller" tabindex="0">
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
        <div
          class="book-row"
          :class="{
            checked: checkedBookIds.has(books[vRow.index]!.id),
            focused: containerFocused && focusedIndex === vRow.index,
          }"
        >
          <button class="checkbox-col" @click.stop="onBookRowCheckClick(vRow.index)">
            <span v-if="checkedBookIds.has(books[vRow.index]!.id)" class="check-mark">✓</span>
            <IconBookRtl20 v-else class="book-icon" />
          </button>
          <button class="book-title" title="גלול לתוצאה הראשונה" @click.stop="onBookRowTitleClick(vRow.index)">
            <span class="title-line">
              <span class="title-text">{{ books[vRow.index]!.title }}</span>
              <span
                v-if="hasSearched && resultCounts.get(books[vRow.index]!.id)"
                class="count"
              >({{ resultCounts.get(books[vRow.index]!.id) }})</span>
            </span>
            <span v-if="books[vRow.index]!.parentPath" class="path-text">
              {{ books[vRow.index]!.parentPath }}
            </span>
          </button>
        </div>
      </div>
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
  text-align: right;
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
  text-align: right;
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
