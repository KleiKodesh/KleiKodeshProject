<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import type { CommentaryTreeNode } from './useCommentary'

const props = defineProps<{ node: CommentaryTreeNode; selectedBookId?: number; hiddenBookIds?: Set<number> }>()
const emit = defineEmits<{ select: [node: CommentaryTreeNode]; expandParent: []; toggle: [bookId: number]; 'open-book': [bookId: number, lineIndex: number] }>()

const isActive = computed(() => props.selectedBookId === props.node.bookId)

const isChecked = computed(() =>
  props.node.bookId == null || !(props.hiddenBookIds?.has(props.node.bookId) ?? false)
)

const sectionState = computed<'checked' | 'unchecked' | 'indeterminate'>(() => {
  if (props.node.type !== 'section') return 'checked'
  const kids = props.node.children.filter(c => c.bookId != null)
  if (!kids.length) return 'checked'
  const hidden = kids.filter(c => props.hiddenBookIds?.has(c.bookId!)).length
  if (hidden === 0) return 'checked'
  if (hidden === kids.length) return 'unchecked'
  return 'indeterminate'
})

const sectionCbEl = ref<HTMLInputElement | null>(null)
watch(sectionState, (s) => {
  if (sectionCbEl.value) sectionCbEl.value.indeterminate = s === 'indeterminate'
}, { immediate: true, flush: 'post' })

function selectBook() {
  if (props.node.bookId != null) emit('select', props.node)
}

function toggleCheck(e: MouseEvent) {
  e.stopPropagation()
  if (props.node.type === 'section') {
    const kids = props.node.children.filter(c => c.bookId != null)
    if (sectionState.value === 'indeterminate') {
      kids.filter(c => props.hiddenBookIds?.has(c.bookId!)).forEach(c => emit('toggle', c.bookId!))
    } else {
      kids.forEach(c => emit('toggle', c.bookId!))
    }
  } else if (props.node.bookId != null) {
    emit('toggle', props.node.bookId)
  }
}
</script>

<template>
  <div role="treeitem" style="display: contents">
    <!-- section header row -->
    <div v-if="node.type === 'section'" class="section-row">
      <input ref="sectionCbEl" type="checkbox"
        class="row-checkbox"
        :checked="sectionState === 'checked'"
        @click.stop="toggleCheck($event)"
        @change.stop />
      <span class="section-label">{{ node.label }}</span>
    </div>

    <!-- book leaf row -->
    <div v-else class="book-row tree-node" :class="{ 'is-active': isActive }" @click.stop="selectBook">
      <input type="checkbox" class="row-checkbox" :checked="isChecked" @click.stop="toggleCheck($event)" @change.stop />
      <span class="book-label" :class="{ dimmed: !isChecked }">{{ node.label }}</span>
    </div>

    <CommentaryTreeViewNode v-for="child in node.children" :key="child.bookId ?? child.label"
      :node="child" :selected-book-id="selectedBookId" :hidden-book-ids="hiddenBookIds"
      @select="emit('select', $event)" @expand-parent="() => {}" @toggle="emit('toggle', $event)"
      @open-book="(bookId, lineIndex) => emit('open-book', bookId, lineIndex)" />
  </div>
</template>

<style scoped>
/* ── section header ── */
.section-row {
  display: flex;
  align-items: center;
  gap: 6px;
  min-height: 28px;
  padding: 2px 8px;
  background: var(--bg-secondary);
  border-top: 1px solid var(--border-color);
  direction: rtl;
  margin-top: 4px;
  color: var(--text-primary);
  font-size: 12px;
  font-weight: 700;
  cursor: default;
  position: sticky;
  top: 0;
  z-index: 1;
}
.section-row:first-child { margin-top: 0; }
.section-row:hover { background: color-mix(in srgb, var(--text-primary) 6%, var(--bg-secondary)); }

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
  padding: 4px 24px 4px 8px;
  direction: rtl;
  transition: background 120ms;
  cursor: pointer;
  color: var(--text-secondary);
  font-size: 11px;
}
.book-row:hover { background: color-mix(in srgb, var(--text-primary) 6%, transparent); }
.book-row.is-active { background: color-mix(in srgb, var(--accent-color) 15%, transparent); color: var(--accent-color); }

.book-label {
  flex: 1;
  word-break: break-word;
  line-height: 1.3;
}
.book-label.dimmed { opacity: 0.35; }

.row-checkbox {
  flex-shrink: 0;
  width: 13px;
  height: 13px;
  margin: 0;
  cursor: pointer;
  accent-color: var(--accent-color);
}


</style>
