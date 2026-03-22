<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { IconChevronDown20Regular, IconChevronLeft20Regular } from '@iconify-prerendered/vue-fluent'
import type { CommentaryTreeNode } from './useCommentary'

const props = defineProps<{ node: CommentaryTreeNode; selectedBookId?: number }>()
const emit = defineEmits<{ select: [node: CommentaryTreeNode]; expandParent: [] }>()

const isExpanded = ref(false)
const hasChildren = computed(() => props.node.children.length > 0)
const isActive = computed(() => props.selectedBookId === props.node.bookId)
const hasSelectedChild = computed(() => props.node.children.some(c => c.bookId === props.selectedBookId))

watch([hasSelectedChild, isActive, () => props.selectedBookId], ([hasChild, active]) => {
  if (hasChild || active) {
    isExpanded.value = true
    if (active) emit('expandParent')
  }
}, { immediate: true })

function handleClick() {
  if (hasChildren.value) isExpanded.value = !isExpanded.value
  else emit('select', props.node)
}
</script>

<template>
  <div role="treeitem">
    <div class="tree-node c-pointer hover-bg"
      :class="{ 'is-active': isActive, 'is-section': node.type === 'section' }"
      tabindex="0"
      @click="handleClick"
      @keydown.enter.stop="handleClick"
      @keydown.space.stop.prevent="handleClick">
      <IconChevronDown20Regular v-if="hasChildren && isExpanded" class="chevron" />
      <IconChevronLeft20Regular v-else-if="hasChildren" class="chevron" />
      <span class="node-label">{{ node.label }}</span>
    </div>
    <template v-if="isExpanded && hasChildren">
      <CommentaryTreeViewNode v-for="child in node.children" :key="child.bookId ?? child.label"
        :node="child" :selected-book-id="selectedBookId"
        @select="emit('select', $event)" @expand-parent="isExpanded = true" />
    </template>
  </div>
</template>

<style scoped>
.tree-node { display: flex; align-items: center; gap: 4px; padding: 6px 8px; direction: rtl; text-align: right; min-height: 28px; }
.tree-node.is-section { position: sticky; top: 0; z-index: 1; background: var(--bg-secondary); font-size: 11px; font-weight: 700; color: var(--text-secondary); border-bottom: 1px solid var(--border-color); text-transform: uppercase; letter-spacing: 0.8px; padding: 8px; }
.tree-node:not(.is-section) { font-size: 12.5px; }
.tree-node.is-active { background: var(--accent-subtle, color-mix(in srgb, var(--accent-color) 15%, transparent)); color: var(--accent-color); }
.chevron { flex-shrink: 0; width: 14px; height: 14px; color: var(--text-secondary); }
.node-label { flex: 1; line-height: 1.3; word-break: break-word; }
</style>
