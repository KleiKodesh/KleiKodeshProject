<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { IconChevronLeft20Regular, IconChevronDown20Regular } from '@iconify-prerendered/vue-fluent'
import type { CommentaryTreeNode } from './useCommentary'

const props = defineProps<{
  node: CommentaryTreeNode
  hiddenBookIds?: Set<number>
  depth?: number
}>()
const emit = defineEmits<{
  toggle: [bookId: number]
}>()

const expanded = ref(false)

// Collect all leaf bookIds recursively under a node
function collectBookIds(node: CommentaryTreeNode): number[] {
  if (node.type === 'book' && node.bookId != null) return [node.bookId]
  return node.children.flatMap(collectBookIds)
}

const leafIds = computed(() => collectBookIds(props.node))

const isChecked = computed(
  () => props.node.bookId == null || !(props.hiddenBookIds?.has(props.node.bookId) ?? false),
)

const sectionState = computed<'checked' | 'unchecked' | 'indeterminate'>(() => {
  if (props.node.type !== 'section') return 'checked'
  if (!leafIds.value.length) return 'checked'
  const hidden = leafIds.value.filter((id) => props.hiddenBookIds?.has(id)).length
  if (hidden === 0) return 'checked'
  if (hidden === leafIds.value.length) return 'unchecked'
  return 'indeterminate'
})

const sectionCbEl = ref<HTMLInputElement | null>(null)
watch(
  sectionState,
  (s) => {
    if (sectionCbEl.value) sectionCbEl.value.indeterminate = s === 'indeterminate'
  },
  { immediate: true, flush: 'post' },
)

function toggleCheck(e: MouseEvent) {
  e.stopPropagation()
  if (props.node.type === 'section') {
    if (sectionState.value === 'indeterminate') {
      // show all hidden leaves
      leafIds.value.filter((id) => props.hiddenBookIds?.has(id)).forEach((id) => emit('toggle', id))
    } else {
      // toggle all leaves
      leafIds.value.forEach((id) => emit('toggle', id))
    }
  } else if (props.node.bookId != null) {
    emit('toggle', props.node.bookId)
  }
}
</script>

<template>
  <div role="treeitem" style="display: contents">
    <!-- section header row -->
    <div v-if="node.type === 'section'" class="section-row" @click.stop="toggleCheck($event)">
      <button class="expander" @click.stop="expanded = !expanded">
        <IconChevronDown20Regular v-if="expanded" />
        <IconChevronLeft20Regular v-else />
      </button>
      <input
        ref="sectionCbEl"
        type="checkbox"
        class="row-checkbox"
        :checked="sectionState === 'checked'"
        @click.stop="toggleCheck($event)"
        @change.stop
      />
      <span class="section-label">{{ node.label }}</span>
    </div>

    <!-- children (sub-sections or books) shown when expanded -->
    <template v-if="node.type === 'section' && expanded">
      <CommentaryTreeViewNode
        v-for="child in node.children"
        :key="child.bookId ?? child.label"
        :node="child"
        :depth="(depth ?? 0) + 1"
        :hidden-book-ids="hiddenBookIds"
        :class="{ 'sub-section': child.type === 'section' }"
        @toggle="emit('toggle', $event)"
      />
    </template>

    <!-- book leaf -->
    <div v-if="node.type === 'book'" class="book-row">
      <span class="expander-placeholder" />
      <input
        type="checkbox"
        class="row-checkbox"
        :checked="isChecked"
        @click.stop="toggleCheck($event)"
        @change.stop
      />
      <span class="book-label" :class="{ dimmed: !isChecked }">{{ node.label }}</span>
    </div>
  </div>
</template>

<style scoped>
/* ── section header ── */
.section-row {
  display: flex;
  align-items: center;
  min-height: 28px;
  padding: 0;
  padding-inline-end: 8px;
  background: var(--bg-secondary);
  border-top: 1px solid var(--border-color);
  direction: rtl;
  margin-top: 4px;
  color: var(--text-primary);
  font-size: 12px;
  font-weight: 700;
  cursor: pointer;
  position: sticky;
  top: 0;
  z-index: 1;
  gap: 6px;
}
.section-row:first-child {
  margin-top: 0;
}
.section-row:hover {
  background: color-mix(in srgb, var(--text-primary) 6%, var(--bg-secondary));
}

/* sub-section rows are indented and lighter */
.sub-section .section-row {
  padding-inline-end: 8px;
  font-weight: 600;
  font-size: 11px;
  color: var(--text-secondary);
  position: static;
  margin-top: 0;
  border-top: none;
}

.expander {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  flex-shrink: 0;
  color: var(--text-secondary);
  padding: 0;
  border-radius: 0;
}

.section-label {
  flex: 1;
  word-break: break-word;
}

/* ── book leaf ── */
.book-row {
  display: flex;
  align-items: center;
  gap: 6px;
  min-height: 28px;
  padding-block: 4px;
  padding-inline-start: 0;
  padding-inline-end: 8px;
  direction: rtl;
  transition: background 120ms;
  color: var(--text-secondary);
  font-size: 11px;
}
.expander-placeholder {
  width: 28px;
  height: 28px;
  flex-shrink: 0;
}
.book-row:hover {
  background: color-mix(in srgb, var(--text-primary) 6%, transparent);
}

.book-label {
  flex: 1;
  word-break: break-word;
  line-height: 1.3;
}
.book-label.dimmed {
  opacity: 0.35;
}

.row-checkbox {
  flex-shrink: 0;
  width: 13px;
  height: 13px;
  margin: 0;
  cursor: pointer;
  accent-color: var(--accent-color);
}
</style>
