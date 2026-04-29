<script setup lang="ts">
import { ref, computed } from 'vue'
import { IconChevronDown20Regular } from '@iconify-prerendered/vue-fluent'
import type { CategoryNode } from '@/features/book-catalog/bookCatalogTree'

const props = withDefaults(
  defineProps<{
    category: CategoryNode
    depth?: number
    checkedBookIds: Set<number>
    resultCounts: Map<number, number>
    hasSearched?: boolean
  }>(),
  { depth: 0, hasSearched: false },
)

const emit = defineEmits<{ toggleBook: [number]; toggleCategory: [CategoryNode, boolean] }>()

const expanded = ref(false)

function allBookIds(cat: CategoryNode): number[] {
  return [...cat.books.map((b) => b.id), ...cat.children.flatMap(allBookIds)]
}

const bookIds = computed(() => allBookIds(props.category))
const totalResults = computed(() =>
  bookIds.value.reduce((s, id) => s + (props.resultCounts.get(id) ?? 0), 0),
)
const isChecked = computed(
  () => bookIds.value.length > 0 && bookIds.value.every((id) => props.checkedBookIds.has(id)),
)
const isIndet = computed(() => {
  const n = bookIds.value.filter((id) => props.checkedBookIds.has(id)).length
  return n > 0 && n < bookIds.value.length
})
const hasChildren = computed(
  () => props.category.children.length > 0 || props.category.books.length > 0,
)
</script>

<template>
  <div v-if="!hasSearched || totalResults > 0">
    <div
      class="row cat-row"
      :class="{ checked: isChecked, indet: isIndet }"
      :style="{ paddingInlineStart: `${depth * 14}px` }"
    >
      <button
        v-if="hasChildren"
        class="expander"
        :class="{ open: expanded }"
        @click.stop="expanded = !expanded"
      >
        <span class="expander-icon"><IconChevronDown20Regular /></span>
      </button>
      <span v-else class="expander-placeholder" />
      <div class="row-body" @click="emit('toggleCategory', category, !isChecked)">
        <span class="check-col">
          <span class="check-mark">✓</span>
          <span class="dash-mark">–</span>
        </span>
        <span class="row-label">
          {{ category.title }}
          <span v-if="totalResults > 0" class="count">({{ totalResults }})</span>
        </span>
      </div>
    </div>
    <template v-if="expanded">
      <FullTextSearchFilterNode
        v-for="child in category.children"
        :key="child.id"
        :category="child"
        :depth="depth + 1"
        :checked-book-ids="checkedBookIds"
        :result-counts="resultCounts"
        :has-searched="hasSearched"
        @toggle-book="emit('toggleBook', $event)"
        @toggle-category="(c, v) => emit('toggleCategory', c, v)"
      />
      <template v-for="book in category.books" :key="book.id">
        <div
          v-if="!hasSearched || resultCounts.get(book.id)"
          class="row book-row"
          :class="{ checked: checkedBookIds.has(book.id) }"
          :style="{ paddingInlineStart: `${(depth + 1) * 14}px` }"
        >
          <span class="expander-placeholder" />
          <div class="row-body" @click="emit('toggleBook', book.id)">
            <span class="check-col">
              <span class="check-mark">✓</span>
            </span>
            <span class="row-label" :class="{ dimmed: !checkedBookIds.has(book.id) }">
              {{ book.title }}
              <span v-if="resultCounts.get(book.id)" class="count"
                >({{ resultCounts.get(book.id) }})</span
              >
            </span>
          </div>
        </div>
      </template>
    </template>
  </div>
</template>

<style scoped>
.row {
  display: flex;
  align-items: stretch;
  height: 26px;
  white-space: nowrap;
  user-select: none;
}
.row-body {
  flex: 1;
  display: flex;
  align-items: center;
  cursor: pointer;
  min-width: 0;
}
.row-body:hover {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}
.cat-row {
  font-size: 12px;
  font-weight: 600;
  color: var(--text-primary);
}
.book-row {
  font-size: 11px;
  color: var(--text-secondary);
}

.check-col {
  width: 28px;
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 11px;
  color: var(--accent-color);
}
.check-mark {
  display: none;
}
.dash-mark {
  display: none;
}
.cat-row.checked .check-mark {
  display: block;
}
.cat-row.indet .dash-mark {
  display: block;
}
.book-row.checked .check-mark {
  display: block;
}

.row-label {
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
}
.dimmed {
  opacity: 0.4;
}
.count {
  font-size: 10px;
  color: var(--text-secondary);
  margin-inline-start: 3px;
}

.expander {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 26px;
  flex-shrink: 0;
  align-self: stretch;
  color: var(--text-secondary);
  padding: 0;
  margin: 0;
  border-radius: 0;
}
.expander:hover {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}
.expander:active {
  transform: none !important;
}
.expander-icon {
  display: flex;
  transition: transform 200ms ease;
}
.expander.open .expander-icon {
  transform: rotate(180deg);
}
.expander :deep(svg) {
  width: 12px;
  height: 12px;
}
.expander-placeholder {
  width: 26px;
  flex-shrink: 0;
}
</style>
