<template>
    <div role="treeitem">
        <div class="tree-node flex-row hover-bg focus-accent click-effect touch-interactive c-pointer"
             :class="{
                'selected-accent-subtle': isActive,
                'connection-type-node': node.type === 'connection-type'
            }"
             tabindex="0"
             @click="handleClick"
             @keydown.enter.stop="handleClick"
             @keydown.space.stop.prevent="handleClick">
            <Icon v-if="hasChildren"
                  :icon="isExpanded ? 'fluent:chevron-down-28-regular' : 'fluent:chevron-left-28-regular'"
                  class="chevron-icon" />
            <div class="node-label">
                {{ node.hebrewName }}
            </div>
        </div>

        <template v-if="isExpanded && hasChildren">
            <CommentaryTreeViewNode v-for="child in node.children"
                                    :key="`${child.name}-${child.bookId || child.category}`"
                                    :node="child"
                                    :selected-book-id="selectedBookId"
                                    @select="emit('select', $event)"
                                    @expand-parent="isExpanded = true" />
        </template>
    </div>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { Icon } from '@iconify/vue'
import type { CommentaryTreeNode } from './useCommentaryTree'

const props = defineProps<{
    node: CommentaryTreeNode
    selectedBookId?: number
}>()

const emit = defineEmits<{
    (e: 'select', node: CommentaryTreeNode): void
    (e: 'expand-parent'): void
}>()

const isExpanded = ref(false)

const hasChildren = computed(() => props.node.children && props.node.children.length > 0)

const isActive = computed(() => props.selectedBookId === props.node.bookId)

const hasSelectedChild = computed(() => {
    if (!hasChildren.value) return false
    return props.node.children.some(child => child.bookId === props.selectedBookId)
})

watch([hasSelectedChild, isActive, () => props.selectedBookId], ([hasChild, active, bookId]) => {
    if (bookId && (hasChild || active)) {
        isExpanded.value = true
        if (active) emit('expand-parent')
    }
}, { immediate: true })

function handleClick() {
    if (hasChildren.value) {
        isExpanded.value = !isExpanded.value
    } else {
        emit('select', props.node)
    }
}
</script>

<style scoped>
.tree-node {
    gap: 4px;
    padding: 4px 8px;
    border-radius: 0;
    transition: background-color 0.2s ease;
    direction: rtl;
    text-align: right;
    min-height: 28px;
}

.tree-node.connection-type-node {
    position: sticky;
    top: 0;
    background-color: var(--bg-secondary);
    z-index: 10;
    font-weight: 700;
    border-radius: 0;
    font-size: 11px;
    padding: 8px;
    border-bottom: 1px solid var(--border-color);
    text-transform: uppercase;
    letter-spacing: 0.8px;
    color: var(--text-secondary);
    margin-top: 4px;
}

.tree-node.connection-type-node:first-child {
    margin-top: 0;
}

.tree-node:not(.connection-type-node) {
    font-size: 12.5px;
    padding: 6px 8px;
}

@media (hover: hover) {
    .tree-node.connection-type-node:hover {
        filter: brightness(0.95);
    }
}

:root.dark .tree-node.connection-type-node:hover {
    filter: brightness(1.1);
}

.chevron-icon {
    flex-shrink: 0;
    font-size: 15px;
    line-height: 1;
}

.node-label {
    flex: 1;
    font-size: 12.5px;
    line-height: 1.3;
    min-width: 0;
    word-break: break-word;
}
</style>
