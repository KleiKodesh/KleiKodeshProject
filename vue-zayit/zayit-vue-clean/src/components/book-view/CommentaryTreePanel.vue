<script setup lang="ts">
import { computed, ref, watch, nextTick } from 'vue'
import CommentaryTreeViewNode from './CommentaryTreeViewNode.vue'
import { buildCommentaryTree } from './useCommentary'
import type { CommentaryGroup, CommentaryTreeNode } from './useCommentary'

const props = defineProps<{ groups: CommentaryGroup[]; selectedBookId?: number; suppressScroll?: boolean; hiddenBookIds?: Set<number> }>()

const emit = defineEmits<{ select: [node: CommentaryTreeNode]; toggle: [bookId: number]; 'open-book': [bookId: number, lineIndex: number] }>()

const tree = computed(() => buildCommentaryTree(props.groups))
const containerRef = ref<HTMLElement | null>(null)

async function scrollToSelected() {
  if (!props.selectedBookId || !containerRef.value) return
  await nextTick()
  await nextTick()
  const el = containerRef.value.querySelector('.tree-node.is-active') as HTMLElement | null
  if (!el) return
  el.scrollIntoView({ behavior: 'instant', block: 'nearest', inline: 'nearest' })
  await nextTick()
  const container = containerRef.value
  const containerRect = container.getBoundingClientRect()
  const elRect = el.getBoundingClientRect()
  const elRelTop = elRect.top - containerRect.top
  const targetScrollTop = container.scrollTop + elRelTop - (containerRect.height / 2) + (elRect.height / 2)
  container.scrollTop = targetScrollTop
}

watch(() => props.selectedBookId, () => {
  if (!props.suppressScroll) scrollToSelected()
}, { flush: 'post' })
defineExpose({ scrollToSelected })
</script>

<template>
  <div ref="containerRef" class="commentary-tree-panel">
    <div v-if="!tree.length" class="empty">אין מפרשים זמינים</div>
    <CommentaryTreeViewNode v-for="node in tree" :key="node.label"
      :node="node" :selected-book-id="selectedBookId" :hidden-book-ids="hiddenBookIds"
      @select="emit('select', $event)" @toggle="emit('toggle', $event)" @open-book="(bookId, lineIndex) => emit('open-book', bookId, lineIndex)" />
  </div>
</template>

<style scoped>
.commentary-tree-panel { height: 100%; overflow-y: auto; overflow-x: hidden; background: var(--bg-secondary); direction: rtl; min-width: 160px; }
.empty { padding: 12px 8px; text-align: center; color: var(--text-secondary); font-size: 11px; font-style: italic; }
</style>
