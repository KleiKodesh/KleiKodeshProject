<template>
    <div role="treeitem">
        <div
            class="tree-node"
            :class="{ 'active': isActive }"
            :style="{ paddingInlineStart: `${depth * 6}px` }"
            @click="handleClick"
        >
            <Icon
                v-if="hasChildren"
                :icon="isExpanded ? 'fluent:chevron-down-28-regular' : 'fluent:chevron-left-28-regular'"
                class="chevron-icon"
            />
            <div v-else class="chevron-spacer"></div>
            
            <div class="node-label">
                {{ node.hebrewName }}
                <span v-if="itemCount" class="item-count">({{ itemCount }})</span>
            </div>
        </div>

        <template v-if="isExpanded && hasChildren">
            <CommentaryTreeViewNode
                v-for="child in node.children"
                :key="`${child.name}-${child.bookId}`"
                :node="child"
                :depth="depth + 1"
                :selected-group-name="selectedGroupName"
                @select="emit('select', $event)"
            />
        </template>
    </div>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { Icon } from '@iconify/vue'
import type { CommentaryTreeNode } from './useCommentaryTree'

const props = withDefaults(defineProps<{
    node: CommentaryTreeNode
    depth?: number
    selectedGroupName?: string
}>(), {
    depth: 0
})

const emit = defineEmits<{
    (e: 'select', node: CommentaryTreeNode): void
}>()

const isExpanded = ref(true)

const hasChildren = computed(() => props.node.children && props.node.children.length > 0)

const itemCount = computed(() => {
    if (props.node.type === 'connection-type') {
        return props.node.children?.length || 0
    }
    return undefined
})

const isActive = computed(() => {
    return props.selectedGroupName === props.node.name
})

function handleClick() {
    if (hasChildren.value) {
        isExpanded.value = !isExpanded.value
    } else {
        console.log('[TreeNode] Emitting select:', {
            nodeName: props.node.name,
            nodeType: props.node.type,
            nodeBookId: props.node.bookId,
            nodeLineIndex: props.node.lineIndex
        })
        emit('select', props.node)
    }
}
</script>

<style scoped>
.tree-node {
    display: flex;
    align-items: flex-start;
    gap: 4px;
    padding: 2px 4px;
    cursor: pointer;
    border-radius: 3px;
    transition: background-color 0.2s ease;
    direction: rtl;
    text-align: right;
    min-height: 20px;
}

.tree-node:hover {
    background-color: var(--reading-bg-secondary, #f0f0f0);
}

.tree-node.active {
    background-color: var(--accent-color);
    color: var(--reading-bg-primary);
}

.chevron-icon {
    flex-shrink: 0;
    font-size: 14px;
    line-height: 1;
    margin-top: 2px;
}

.chevron-spacer {
    width: 14px;
    flex-shrink: 0;
}

.node-label {
    flex: 1;
    font-size: 12px;
    line-height: 1.2;
    min-width: 0;
    word-break: break-word;
}

.item-count {
    font-size: 10px;
    color: var(--text-secondary);
    margin-left: 2px;
}

.tree-node.active .item-count {
    color: inherit;
}
</style>
