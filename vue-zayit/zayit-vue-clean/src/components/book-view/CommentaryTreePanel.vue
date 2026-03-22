<script setup lang="ts">
import { computed, ref, watch, nextTick } from 'vue'
import CommentaryTreeViewNode from './CommentaryTreeViewNode.vue'
import { buildCommentaryTree } from './useCommentary'
import type { CommentaryGroup, CommentaryTreeNode } from './useCommentary'

const props = defineProps<{ groups: CommentaryGroup[]; selectedBookId?: number }>()
const emit = defineEmits<{ select: [node: CommentaryTreeNode] }>()

const tree = computed(() => buildCommentaryTree(props.groups))
const containerRef = ref<HTMLElement | null>(null)

async function scrollToSelected() {
  if (!props.selectedBookId || !containerRef.value) return
  await nextTick()
  const el = containerRef.value.querySelector('.tree-node.is-active') as HTMLElement | null
  el?.scrollIntoView({ block: 'nearest' })
}

watch(() => props.selectedBookId, scrollToSelected, { flush: 'post' })
defineExpose({ scrollToSelected })
</script>

<template>
  <div ref="containerRef" class="commentary-tree-panel">
    <div v-if="!tree.length" class="empty">אין מפרשים זמינים</div>
    <CommentaryTreeViewNode v-for="node in tree" :key="node.label"
      :node="node" :selected-book-id="selectedBookId"
      @select="emit('select', $event)" />
  </div>
</template>

<style scoped>
.commentary-tree-panel { height: 100%; overflow-y: auto; overflow-x: hidden; background: var(--bg-secondary); direction: rtl; min-width: 160px; }
.empty { padding: 12px 8px; text-align: center; color: var(--text-secondary); font-size: 11px; font-style: italic; }
</style>
