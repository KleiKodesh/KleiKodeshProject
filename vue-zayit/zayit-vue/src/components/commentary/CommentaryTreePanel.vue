<template>
    <div class="commentary-tree-panel flex-column">
        <div ref="treeContainer" class="tree-container">
            <div v-if="commentaryTree.length === 0" class="tree-empty">
                אין מפרשים זמינים
            </div>
            <div v-else class="tree-root">
                <CommentaryTreeViewNode v-for="node in commentaryTree"
                                        :key="node.name"
                                        :node="node"
                                        :depth="0"
                                        :selected-book-id="selectedBookId"
                                        @select="emit('select', $event)" />
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { computed, ref, watch, nextTick } from 'vue'
import CommentaryTreeViewNode from './CommentaryTreeViewNode.vue'
import { useCommentaryTree } from './useCommentaryTree'
import { scrollToElementCenter } from '@/components/shared/useScrollToElement'
import type { CommentaryTreeNode } from './useCommentaryTree'

const props = defineProps<{
    commentaryGroups: any[]
    selectedBookId?: number
}>()

const emit = defineEmits<{
    (e: 'select', node: CommentaryTreeNode): void
}>()

const { commentaryTree } = useCommentaryTree(computed(() => props.commentaryGroups))
const treeContainer = ref<HTMLElement | null>(null)

async function scrollToSelected() {
    if (!props.selectedBookId || !treeContainer.value) return
    await nextTick()
    await nextTick()
    const activeNode = treeContainer.value.querySelector('.tree-node.selected-accent-subtle') as HTMLElement
    if (activeNode) await scrollToElementCenter(activeNode)
}

watch(() => props.selectedBookId, scrollToSelected, { flush: 'post' })

defineExpose({
    scrollToSelected
})
</script>

<style scoped>
.commentary-tree-panel {
    height: 100%;
    background-color: var(--reading-bg-secondary, #f5f5f5);
    overflow: hidden;
}

.tree-container {
    flex: 1;
    overflow-y: auto;
    overflow-x: hidden;
}

.tree-empty {
    padding: 8px;
    text-align: center;
    color: var(--text-secondary);
    font-size: 11px;
    font-style: italic;
    direction: rtl;
}

.tree-root {
    padding: 0;
}
</style>
