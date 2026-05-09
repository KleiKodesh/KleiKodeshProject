<script setup lang="ts">
import { computed, ref } from 'vue'
import { IconChevronDown20Regular } from '@iconify-prerendered/vue-fluent'
import type { CommentaryVisibilityItem } from './bookViewTypes'
import type { CommentaryTreeNode } from './commentaryTreeTypes'
import { isTreeNode } from './commentaryTreeTypes'

const props = defineProps<{
  node: CommentaryTreeNode
  depth?: number
}>()

const emit = defineEmits<{
  'toggle-item': [item: CommentaryVisibilityItem]
  'navigate-to-book': [bookId: number]
}>()

const expanded = ref(false)

function collectLeafItems(node: CommentaryTreeNode): CommentaryVisibilityItem[] {
  const result: CommentaryVisibilityItem[] = []
  for (const child of node.children) {
    if (isTreeNode(child)) result.push(...collectLeafItems(child))
    else result.push(child)
  }
  return result
}

const leafItems = computed(() => collectLeafItems(props.node))

const sectionState = computed<'checked' | 'unchecked' | 'indeterminate'>(() => {
  if (!leafItems.value.length) return 'checked'
  const checkedCount = leafItems.value.filter((item) => item.isChecked).length
  if (checkedCount === leafItems.value.length) return 'checked'
  if (checkedCount === 0) return 'unchecked'
  return 'indeterminate'
})

function toggleCheckbox(event: MouseEvent) {
  event.stopPropagation()
  const shouldCheck = sectionState.value !== 'checked'
  leafItems.value.forEach((item) => { item.isChecked = shouldCheck })
}

function navigateToFirstBook() {
  const first = leafItems.value[0]
  if (first != null) emit('navigate-to-book', first.bookId)
}
</script>

<template>
  <div style="display: contents">
    <div class="row section-row" :class="[sectionState, { expanded }]">
      <button class="expander" :class="{ open: expanded }" @click.stop="expanded = !expanded">
        <span class="expander-icon"><IconChevronDown20Regular /></span>
      </button>
      <button class="section-title" @click="navigateToFirstBook">
        {{ node.label }}
      </button>
      <button class="checkbox-col" @click="toggleCheckbox">
        <span class="check-mark">&#10003;</span>
        <span class="dash-mark">&#8211;</span>
      </button>
    </div>

    <template v-if="expanded">
      <template
        v-for="child in node.children"
        :key="isTreeNode(child) ? child.label : `${child.bookId}::${child.sectionLabel}::${child.subSectionLabel}`"
      >
        <CommentaryTreeSectionNode
          v-if="isTreeNode(child)"
          :node="child"
          :depth="(depth ?? 0) + 1"
          @toggle-item="emit('toggle-item', $event)"
          @navigate-to-book="emit('navigate-to-book', $event)"
        />
        <div
          v-else
          class="row book-row"
          :class="{ unchecked: !child.isChecked }"
        >
          <button class="book-title" @click="emit('navigate-to-book', child.bookId)">
            {{ child.bookTitle }}
          </button>
          <button class="checkbox-col" @click="emit('toggle-item', child)">
            <span class="check-mark">&#10003;</span>
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
  flex-shrink: 0;
  white-space: nowrap;
  color: var(--text-primary);
  --expanded-row-bg: color-mix(in srgb, var(--active-bg) 55%, transparent);
  --expanded-row-hover-bg: color-mix(in srgb, var(--active-bg) 65%, var(--hover-bg));
}

.section-row { font-size: 12px; font-weight: 600; }

.section-row.expanded {
  background: var(--expanded-row-bg);
}

.section-row.expanded:hover {
  background: var(--expanded-row-hover-bg);
}

.book-row { font-size: 11px; color: var(--text-secondary); }

/* ── Expander button (left side in RTL) ── */
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

.expander:hover  { background: color-mix(in srgb, var(--text-primary) 8%, transparent); }
.expander:active { transform: none !important; }

.expander-icon {
  display: flex;
  transition: transform 200ms ease;
}

.expander.open .expander-icon { transform: rotate(180deg); }
.expander :deep(svg) { width: 12px; height: 12px; }

/* ── Title button (middle, clickable for navigation) ── */
.section-title,
.book-title {
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

.section-title:hover,
.book-title:hover {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}

.book-title.dimmed { opacity: 0.4; }

/* ── Checkbox column (right side in RTL) ── */
.checkbox-col {
  width: 28px;
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 11px;
  color: var(--accent-color);
  padding: 0;
  margin: 0;
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

.section-row.checked .checkbox-col .check-mark { display: block; }
.section-row.indeterminate .checkbox-col .dash-mark  { display: block; }
.book-row:not(.unchecked) .checkbox-col .check-mark { display: block; }

:global(:root.dark) .row {
  --expanded-row-bg: var(--active-bg);
  --expanded-row-hover-bg: color-mix(in srgb, var(--active-bg) 70%, var(--hover-bg));
}
</style>
