<script setup lang="ts">
import { computed } from 'vue'
import { IconChevronDown20Regular } from '@iconify-prerendered/vue-fluent'
import type { CommentaryVisibilityItem } from '../bookViewTypes'
import type { CommentaryTreeNode } from './commentaryTreeTypes'
import { isTreeNode } from './commentaryTreeTypes'
import { isCommentaryNodeExpanded, setCommentaryNodeExpanded } from './uncheckedCommentaryBooks'

const props = defineProps<{
  node: CommentaryTreeNode
  depth?: number
  /** Check-tree scope of the panel this tree is filtering (see commentaryScopeKey). */
  scopeKey: string
}>()

const scopeKey = props.scopeKey

const emit = defineEmits<{
  'toggle-item': [item: CommentaryVisibilityItem]
  'toggle-node': [payload: { sectionLabel: string; subSectionLabel: string | null; shouldCheck: boolean }]
  'navigate-to-book': [bookId: number]
}>()

function collectLeafItems(node: CommentaryTreeNode): CommentaryVisibilityItem[] {
  const result: CommentaryVisibilityItem[] = []
  for (const child of node.children) {
    if (isTreeNode(child)) result.push(...collectLeafItems(child))
    else result.push(child)
  }
  return result
}

function childKey(child: CommentaryTreeNode | CommentaryVisibilityItem): string {
  return isTreeNode(child)
    ? child.label
    : `${child.bookId}::${child.sectionLabel}::${child.subSectionLabel}`
}

const leafItems = computed(() => collectLeafItems(props.node))

// Depth is shown by type weight/size/colour rather than indentation, matching
// the FTS results filter. This tree is capped at two node levels by its
// builder (section -> subsection -> book leaves), so rungs 0-1 cover it.
const rung = computed(() => Math.min(props.depth ?? 0, 1))

// Stable key for this node in the store, derived from its first leaf's path —
// section (depth 0) → sectionLabel, subsection (depth ≥ 1) → 'section::sub'.
// Persisted so expand/collapse survives line changes and tab switches.
const nodeKey = computed(() => {
  const first = leafItems.value[0]
  if (!first) return props.node.label
  return (props.depth ?? 0) >= 1 ? `${first.sectionLabel}::${first.subSectionLabel}` : first.sectionLabel
})

const expanded = computed({
  get: () => isCommentaryNodeExpanded(scopeKey, nodeKey.value),
  set: (value: boolean) => setCommentaryNodeExpanded(scopeKey, nodeKey.value, value),
})

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
  const first = leafItems.value[0]
  if (!first) return
  // A node's leaves all share the same sectionLabel; depth-1 nodes also share
  // the subSectionLabel. The panel's handler applies the node-level rule so the
  // uncheck covers FUTURE children of this category too, not just current ones.
  emit('toggle-node', {
    sectionLabel: first.sectionLabel,
    subSectionLabel: (props.depth ?? 0) >= 1 ? first.subSectionLabel : null,
    shouldCheck,
  })
}

function navigateToFirstBook() {
  const first = leafItems.value[0]
  if (first != null) emit('navigate-to-book', first.bookId)
}
</script>

<template>
  <div style="display: contents">
    <div class="row section-row" :class="[sectionState, { expanded }]" :data-rung="rung">
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
        :key="childKey(child)"
      >
        <CommentaryTreeSectionNode
          v-if="isTreeNode(child)"
          :node="child"
          :depth="(depth ?? 0) + 1"
          :scope-key="scopeKey"
          @toggle-item="emit('toggle-item', $event)"
          @toggle-node="emit('toggle-node', $event)"
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

/* ── Type ladder ───────────────────────────────────────────────────────────
   Depth reads from weight/size/colour, not indentation, so the title keeps the
   full panel width. Only 700/600/400 are used — the renderer snaps in-between
   weights (500-600 render alike, 650-700 alike), so size and colour carry the
   finer steps. Rungs differ on two axes at once (weight AND size AND colour
   here), and 11px is the floor. Books stay the lightest thing in the tree. */
.section-row[data-rung="0"] .section-title {
  font-size: 12.5px;
  font-weight: 700;
  letter-spacing: 0.01em;
  color: var(--text-primary);
}
.section-row[data-rung="1"] .section-title {
  font-size: 11.5px;
  font-weight: 600;
  color: color-mix(in srgb, var(--text-primary) 36%, var(--text-secondary));
}

.section-row.expanded {
  background: var(--expanded-row-bg);
}

.section-row.expanded:hover {
  background: var(--expanded-row-hover-bg);
}

/* font-weight pinned, not inherited: books are the ladder's lightest rung. */
.book-row { font-size: 11px; font-weight: 400; color: var(--text-secondary); }

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
