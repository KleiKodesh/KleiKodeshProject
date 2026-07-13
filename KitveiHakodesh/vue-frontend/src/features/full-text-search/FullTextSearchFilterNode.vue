<script setup lang="ts">
import { computed, inject } from 'vue'
import { IconChevronDown20Regular } from '@iconify-prerendered/vue-fluent'
import type { CategoryNode } from '@/features/book-catalog/bookCatalogTree'
import { FILTER_EXPANSION_KEY } from './fullTextSearchFilterExpansion'

const props = withDefaults(
  defineProps<{
    category: CategoryNode
    checkedBookIds: Set<number>
    resultCounts: Map<number, number>
    hasSearched?: boolean
  }>(),
  { hasSearched: false },
)

const emit = defineEmits<{
  toggleBook: [number]
  toggleCategory: [CategoryNode, boolean]
  navigateToBook: [number]
}>()

// Expansion state is owned centrally by the panel (see fullTextSearchFilterExpansion)
// so "expand all" can fill it progressively instead of mounting the whole tree at once.
const expansion = inject(FILTER_EXPANSION_KEY)!
const expanded = computed(() => expansion.isExpanded(props.category.id))
function toggleExpanded() {
  expansion.toggle(props.category.id)
}

function allBookIds(cat: CategoryNode): number[] {
  return [...cat.books.map((b) => b.id), ...cat.children.flatMap(allBookIds)]
}

// Prefer the ids precomputed at catalog load; fall back to an on-the-fly flatten
// only if this node predates the precompute pass.
const bookIds = computed(() => props.category.subtreeBookIds ?? allBookIds(props.category))
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
      :class="{ checked: isChecked, indet: isIndet, expanded }"
    >
      <button
        v-if="hasChildren"
        class="expander"
        :class="{ open: expanded }"
        @click.stop="toggleExpanded"
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
  /* Rows are a fixed 26px tall, so the browser can skip layout/paint for any row
     scrolled out of view. This keeps a fully-expanded (very tall) tree cheap. */
  content-visibility: auto;
  contain-intrinsic-size: auto 26px;
  /* Hierarchy is shown by highlighting expanded categories (like the book-view
     commentary filter) rather than by indentation. */
  --expanded-row-bg: color-mix(in srgb, var(--active-bg) 55%, transparent);
  --expanded-row-hover-bg: color-mix(in srgb, var(--active-bg) 65%, var(--hover-bg));
}
.cat-row {
  font-size: 12px;
  font-weight: 600;
  color: var(--text-primary);
}
.cat-row.expanded {
  background: var(--expanded-row-bg);
}
.cat-row.expanded:hover {
  background: var(--expanded-row-hover-bg);
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

:global(:root.dark) .row {
  --expanded-row-bg: var(--active-bg);
  --expanded-row-hover-bg: color-mix(in srgb, var(--active-bg) 70%, var(--hover-bg));
}
</style>
