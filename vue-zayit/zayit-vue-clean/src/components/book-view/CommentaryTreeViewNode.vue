<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { IconChevronDown16Regular, IconChevronLeft16Regular } from '@iconify-prerendered/vue-fluent'
import type { CommentaryTreeNode } from './useCommentary'

const props = defineProps<{ node: CommentaryTreeNode; selectedBookId?: number; hiddenBookIds?: Set<number> }>()
const emit = defineEmits<{ select: [node: CommentaryTreeNode]; expandParent: []; toggle: [bookId: number]; 'open-book': [bookId: number, lineIndex: number] }>()

const isExpanded = ref(false)
const hasChildren = computed(() => props.node.children.length > 0)
const isActive = computed(() => props.selectedBookId === props.node.bookId)
const hasSelectedChild = computed(() => props.node.children.some(c => c.bookId === props.selectedBookId))

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

// Set indeterminate property on the native checkbox element
const sectionCbEl = ref<HTMLInputElement | null>(null)
watch(sectionState, (s) => {
  if (sectionCbEl.value) sectionCbEl.value.indeterminate = s === 'indeterminate'
}, { immediate: true, flush: 'post' })

watch([hasSelectedChild, isActive, () => props.selectedBookId], ([hasChild, active]) => {
  if (hasChild || active) { isExpanded.value = true; if (active) emit('expandParent') }
}, { immediate: true })

function toggleExpand() { isExpanded.value = !isExpanded.value }

function toggleCheck(e: MouseEvent) {
  if (e.ctrlKey && props.node.type === 'book' && props.node.bookId != null) {
    emit('open-book', props.node.bookId, props.node.firstLineIndex ?? 0)
    return
  }
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
      <button class="expander" @click.stop="toggleExpand">
        <IconChevronDown16Regular v-if="isExpanded" />
        <IconChevronLeft16Regular v-else />
      </button>
      <label class="section-label-row" @click.stop="toggleCheck($event)">
        <input ref="sectionCbEl" type="checkbox"
          :checked="sectionState === 'checked'"
          @click.stop="toggleCheck($event)"
          @change.stop />
        <span class="section-label">{{ node.label }}</span>
      </label>
    </div>

    <!-- book leaf row -->
    <div v-else class="book-row tree-node" :class="{ 'is-active': isActive }">
      <span class="indent" />
      <label class="book-label-row" title="לחץ להצגה/הסתרה • Ctrl+לחץ לניווט לספר" @click.stop="toggleCheck($event)">
        <input type="checkbox" :checked="isChecked" @click.stop="toggleCheck($event)" @change.stop />
        <span class="book-label" :class="{ dimmed: !isChecked }">{{ node.label }}</span>
      </label>
    </div>

    <template v-if="isExpanded && hasChildren">
      <CommentaryTreeViewNode v-for="child in node.children" :key="child.bookId ?? child.label"
        :node="child" :selected-book-id="selectedBookId" :hidden-book-ids="hiddenBookIds"
        @select="emit('select', $event)" @expand-parent="isExpanded = true" @toggle="emit('toggle', $event)"
        @open-book="(bookId, lineIndex) => emit('open-book', bookId, lineIndex)" />
    </template>
  </div>
</template>

<style scoped>
/* ── section header ── */
.section-row {
  display: flex;
  align-items: center;
  min-height: 28px;
  background: var(--bg-toolbar);
  border-top: 1px solid var(--border-color);
  border-bottom: 1px solid var(--border-color);
  direction: rtl;
  margin-top: 2px;
}
.section-row:first-child { margin-top: 0; }
.section-row:hover { background: color-mix(in srgb, var(--text-primary) 6%, var(--bg-toolbar)); }

.expander {
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  width: 28px;
  height: 28px;
  padding: 0;
  background: none;
  border: none;
  color: var(--text-secondary);
}
.expander:hover { background: none; color: var(--text-secondary); }
.expander:active { transform: none; }
.expander svg { width: 12px; height: 12px; display: block; }

.section-label-row {
  display: flex;
  align-items: center;
  flex: 1;
  gap: 5px;
  min-height: 28px;
  padding: 2px 6px 2px 0;
  cursor: pointer;
  color: var(--text-secondary);
  font-size: 10px;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.7px;
  min-width: 0;
}

.section-label {
  flex: 1;
  word-break: break-word;
}

/* ── book leaf ── */
.book-row {
  display: flex;
  align-items: center;
  min-height: 28px;
  direction: rtl;
  transition: background 120ms;
}
.book-row:hover { background: color-mix(in srgb, var(--text-primary) 6%, transparent); }
.book-row.is-active { background: color-mix(in srgb, var(--accent-color) 15%, transparent); color: var(--accent-color); }

.indent { display: block; width: 28px; flex-shrink: 0; }

.book-label-row {
  display: flex;
  align-items: center;
  flex: 1;
  gap: 6px;
  min-height: 28px;
  padding: 4px 8px 4px 0;
  cursor: pointer;
  color: inherit;
  font-size: 12px;
  min-width: 0;
}

.book-label {
  flex: 1;
  word-break: break-word;
  line-height: 1.3;
}
.book-label.dimmed { opacity: 0.35; }

/* ── native checkbox ── */
input[type="checkbox"] {
  flex-shrink: 0;
  width: 13px;
  height: 13px;
  margin: 0;
  cursor: pointer;
  accent-color: var(--accent-color);
}
</style>
