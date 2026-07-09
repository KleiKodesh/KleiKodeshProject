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

const emit = defineEmits<{
  toggleBook: [number]
  toggleCategory: [CategoryNode, boolean]
  navigateToBook: [number]
}>()

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

function firstNavigableBookId(cat: CategoryNode): number | null {
  // Prefer a book that has results (when search has run), otherwise first book
  for (const book of cat.books) {
    if (!props.hasSearched || props.resultCounts.get(book.id)) return book.id
  }
  for (const child of cat.children) {
    const found = firstNavigableBookId(child)
    if (found != null) return found
  }
  return null
}

function navigateToCategory() {
  const id = firstNavigableBookId(props.category)
  if (id != null) emit('navigateToBook', id)
}
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
      <button class="row-title" title="גלול לתוצאה הראשונה" @click.stop="navigateToCategory">
        {{ category.title }}
        <span v-if="totalResults > 0" class="count">({{ totalResults }})</span>
      </button>
      <button class="checkbox-col" @click.stop="emit('toggleCategory', category, !isChecked)">
        <span class="check-mark">✓</span>
        <span class="dash-mark">–</span>
      </button>
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
        @navigate-to-book="emit('navigateToBook', $event)"
      />
      <template v-for="book in category.books" :key="book.id">
        <div
          v-if="!hasSearched || resultCounts.get(book.id)"
          class="row book-row"
          :class="{ checked: checkedBookIds.has(book.id) }"
          :style="{ paddingInlineStart: `${(depth + 1) * 14}px` }"
        >
          <button
            class="row-title"
            :class="{ dimmed: !checkedBookIds.has(book.id) }"
            title="גלול לתוצאה הראשונה"
            @click.stop="emit('navigateToBook', book.id)"
          >
            {{ book.title }}
            <span v-if="resultCounts.get(book.id)" class="count">({{ resultCounts.get(book.id) }})</span>
          </button>
          <button class="checkbox-col" @click.stop="emit('toggleBook', book.id)">
            <span class="check-mark">✓</span>
          </button>
        </div>
      </template>
    </template>
  </div>
</template>

<style scoped>
.row {
  display: flex;
  flex-direction: row-reverse;
  align-items: stretch;
  height: 26px;
  white-space: nowrap;
  user-select: none;
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

/* Title button — fills the middle, clicking navigates for books or toggles for categories */
.row-title {
  flex: 1;
  min-width: 0;
  text-align: right;
  padding-inline-end: 8px;
  padding-inline-start: 8px;
  color: inherit;
  background: none;
  border: none;
  cursor: pointer;
  font-size: inherit;
  font-weight: inherit;
  font-family: inherit;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  border-radius: 0;
}
.row-title:hover {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}
.dimmed { opacity: 0.4; }

/* Checkbox button — right side in RTL (first in DOM, last visually due to row-reverse) */
.checkbox-col {
  width: 28px;
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

.check-mark { display: none; }
.dash-mark  { display: none; }
.cat-row.checked  .checkbox-col .check-mark { display: block; }
.cat-row.indet    .checkbox-col .dash-mark  { display: block; }
.book-row.checked .checkbox-col .check-mark { display: block; }

.count {
  font-size: 10px;
  color: var(--text-secondary);
  margin-inline-start: 3px;
}

/* Expander — left side in RTL (last in DOM, first visually due to row-reverse) */
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
.expander:active { transform: none !important; }
.expander-icon {
  display: flex;
  transition: transform 200ms ease;
}
.expander.open .expander-icon { transform: rotate(180deg); }
.expander :deep(svg) { width: 12px; height: 12px; }
.expander-placeholder {
  width: 26px;
  flex-shrink: 0;
}
</style>
