<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { useVirtualizer } from '@tanstack/vue-virtual'
import { IconBook20Filled } from '@iconify-prerendered/vue-fluent'
import { useVirtualListKeys } from '@/composables/useVirtualListKeyNav'
import type { BookRow } from '@/components/books-fs/booksCategoryTree'

const props = defineProps<{
  books: BookRow[]
  checkedBookIds: Set<number>
  resultCounts: Map<number, number>
  hasSearched?: boolean
}>()

const emit = defineEmits<{ toggleBook: [number] }>()

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
          @click="focusedIndex = vRow.index; emit('toggleBook', books[vRow.index]!.id)"
        >
          <span class="check-col">
            <span v-if="checkedBookIds.has(books[vRow.index]!.id)" class="check-mark">✓</span>
            <IconBook20Filled v-else class="book-icon" />
          </span>
          <span class="item-text">
            <span class="item-title-row">
              <span class="item-title">{{ books[vRow.index]!.title }}</span>
              <span
                v-if="hasSearched && resultCounts.get(books[vRow.index]!.id)"
                class="count"
              >({{ resultCounts.get(books[vRow.index]!.id) }})</span>
            </span>
            <span v-if="books[vRow.index]!.parentPath" class="item-path">
              {{ books[vRow.index]!.parentPath }}
            </span>
          </span>
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
  align-items: center;
  gap: 8px;
  min-height: 40px;
  padding: 4px 10px;
  cursor: pointer;
  box-sizing: border-box;
  user-select: none;
}
.book-row:hover {
  background: color-mix(in srgb, var(--text-primary) 6%, transparent);
}
.book-row:active {
  background: color-mix(in srgb, var(--text-primary) 10%, transparent);
}
.book-row.focused {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}
.check-col {
  width: 20px;
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 11px;
  color: var(--accent-color);
}
.book-icon {
  width: 16px;
  height: 16px;
  color: #c1440e;
  opacity: 0.5;
}
.item-text {
  display: flex;
  flex-direction: column;
  gap: 1px;
  min-width: 0;
  flex: 1;
}
.item-title-row {
  display: flex;
  align-items: baseline;
  gap: 5px;
  min-width: 0;
}
.item-title {
  font-size: 12px;
  color: var(--text-primary);
  line-height: 1.3;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  min-width: 0;
  flex-shrink: 1;
}
.item-path {
  font-size: 10px;
  color: var(--text-secondary);
  opacity: 0.7;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  line-height: 1.3;
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
