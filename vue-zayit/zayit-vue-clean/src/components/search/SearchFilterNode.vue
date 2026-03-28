<script setup lang="ts">
import { ref, computed } from 'vue'
import { IconChevronLeft20Regular, IconBookOpen20Regular } from '@iconify-prerendered/vue-fluent'
import type { CategoryNode } from '@/components/books-fs/booksFsTree'

const props = withDefaults(defineProps<{
  category: CategoryNode
  depth?: number
  checkedBookIds: Set<number>
  resultCounts: Map<number, number>
  hasSearched?: boolean
}>(), { depth: 0, hasSearched: false })

const emit = defineEmits<{
  toggleBook: [bookId: number]
  toggleCategory: [category: CategoryNode, checked: boolean]
}>()

const expanded = ref(false)

function allBookIds(cat: CategoryNode): number[] {
  return [...cat.books.map(b => b.id), ...cat.children.flatMap(allBookIds)]
}

const bookIds = computed(() => allBookIds(props.category))

const totalResults = computed(() =>
  bookIds.value.reduce((s, id) => s + (props.resultCounts.get(id) ?? 0), 0)
)

const isChecked = computed(() =>
  bookIds.value.length > 0 && bookIds.value.every(id => props.checkedBookIds.has(id))
)

const isIndeterminate = computed(() => {
  const n = bookIds.value.filter(id => props.checkedBookIds.has(id)).length
  return n > 0 && n < bookIds.value.length
})

function setIndeterminate(el: HTMLInputElement | null) {
  if (el) el.indeterminate = isIndeterminate.value
}
</script>

<template>
  <div v-if="!hasSearched || totalResults > 0" class="node">
    <div
      class="row"
      :style="{ paddingInlineStart: `${8 + depth * 18}px` }"
      @click="expanded = !expanded"
      @keydown.enter="expanded = !expanded"
    >
      <IconChevronLeft20Regular
        v-if="category.children.length > 0 || category.books.length > 0"
        class="chevron"
        :class="{ open: expanded }"
      />
      <input
        type="checkbox"
        class="cb"
        :checked="isChecked"
        :ref="el => setIndeterminate(el as HTMLInputElement | null)"
        @click.stop
        @change="emit('toggleCategory', category, !isChecked)"
      />
      <span class="label">{{ category.title }}</span>
      <span v-if="totalResults > 0" class="count">({{ totalResults }})</span>
    </div>

    <template v-if="expanded">
      <SearchFilterNode
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
          :style="{ paddingInlineStart: `${26 + (depth + 1) * 18}px` }"
          @click="emit('toggleBook', book.id)"
          @keydown.enter="emit('toggleBook', book.id)"
        >
          <input type="checkbox" class="cb" :checked="checkedBookIds.has(book.id)" @click.stop @change="emit('toggleBook', book.id)" />
          <IconBookOpen20Regular class="book-icon" />
          <span class="label">{{ book.title }}</span>
          <span v-if="resultCounts.get(book.id)" class="count">({{ resultCounts.get(book.id) }})</span>
        </div>
      </template>
    </template>
  </div>
</template>

<style scoped>
.node { user-select: none; }

.row {
  display: flex;
  align-items: center;
  gap: 5px;
  padding-block: 4px;
  padding-inline-end: 8px;
  min-height: 32px;
  cursor: pointer;
  transition: background 0.1s;
}

.row:hover  { background: color-mix(in srgb, var(--text-primary) 6%, transparent); }
.row:active { background: color-mix(in srgb, var(--text-primary) 10%, transparent); }

.chevron {
  flex-shrink: 0;
  color: var(--text-secondary);
  transition: transform 0.15s;
}
.chevron.open { transform: rotate(-90deg); }

.cb {
  width: 14px;
  height: 14px;
  flex-shrink: 0;
  cursor: pointer;
}

.label { flex: 1; font-size: 13px; line-height: 1.4; }

.book-icon { flex-shrink: 0; color: #C1440E; }

.count { font-size: 11px; color: var(--text-secondary); }
</style>
