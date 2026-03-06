<template>
    <div class="commentary-tree-panel">
        <div class="tree-container">
            <div v-if="commentaryTree.length === 0" class="tree-empty">
                אין מפרשים זמינים
            </div>
            
            <div v-else class="tree-root">
                <CommentaryTreeViewNode
                    v-for="node in commentaryTree"
                    :key="node.name"
                    :node="node"
                    :depth="0"
                    :selected-group-name="selectedGroupName"
                    @select="selectNode"
                />
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import CommentaryTreeViewNode from './CommentaryTreeViewNode.vue'
import { useCommentaryTree } from './useCommentaryTree'
import type { CommentaryTreeNode } from './useCommentaryTree'

const props = defineProps<{
    commentaryGroups: any[]
    selectedGroupName?: string
}>()

const { commentaryTree } = useCommentaryTree(computed(() => props.commentaryGroups))

const emit = defineEmits<{
    (e: 'select', node: CommentaryTreeNode): void
}>()

function selectNode(node: CommentaryTreeNode) {
    emit('select', node)
}
</script>

<style scoped>
.commentary-tree-panel {
    display: flex;
    flex-direction: column;
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
    padding: 4px;
}
</style>
