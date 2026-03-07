<template>
    <div role="treeitem">
        <div class="tree-node hover-bg focus-accent click-effect"
             :class="{
                'selected-accent-subtle': isActive,
                'connection-type-node': node.type === 'connection-type'
            }"
             :style="{ paddingInlineStart: `${depth * 6}px` }"
             tabindex="0"
             @click="handleClick"
             @keydown.enter.stop="handleClick"
             @keydown.space.stop.prevent="handleClick">
            <Icon v-if="hasChildren"
                  :icon="isExpanded ? 'fluent:chevron-down-28-regular' : 'fluent:chevron-left-28-regular'"
                  class="chevron-icon" />
            <div v-else
                 class="chevron-spacer"></div>

            <div class="node-label">
                {{ node.hebrewName }}
                <span v-if="itemCount"
                      class="item-count">({{ itemCount }})</span>
            </div>
        </div>

        <template v-if="isExpanded && hasChildren">
            <CommentaryTreeViewNode v-for="child in node.children"
                                    :key="`${child.name}-${child.bookId}`"
                                    :node="child"
                                    :depth="depth + 1"
                                    :selected-book-id="selectedBookId"
                                    @select="emit('select', $event)"
                                    @expand-parent="isExpanded = true" />
        </template>
    </div>
</template>

<script setup lang="ts">
import { computed, ref, watch, nextTick } from 'vue'
import { Icon } from '@iconify/vue'
import type { CommentaryTreeNode } from './useCommentaryTree'

const props = withDefaults(defineProps<{
    node: CommentaryTreeNode
    depth?: number
    selectedBookId?: number
}>(), {
    depth: 0
})

const emit = defineEmits<{
    (e: 'select', node: CommentaryTreeNode): void
    (e: 'expand-parent'): void
}>()

const isExpanded = ref(false)
const isManuallyCollapsed = ref(false)

const hasChildren = computed(() => props.node.children && props.node.children.length > 0)

const itemCount = computed(() => {
    if (props.node.type === 'connection-type') {
        return props.node.children?.length || 0
    }
    return undefined
})

const isActive = computed(() => {
    return props.selectedBookId === props.node.bookId
})

const hasSelectedChild = computed(() => {
    if (!hasChildren.value) return false
    return props.node.children.some(child => child.bookId === props.selectedBookId)
})

// Auto-expand if this node contains the selected book
watch([hasSelectedChild, isActive, () => props.selectedBookId], async ([hasChild, active]) => {
    if (hasChild || active) {
        isExpanded.value = true
        isManuallyCollapsed.value = false
        if (active) {
            emit('expand-parent')
        }
    }
}, { immediate: true })

function handleClick() {
    if (hasChildren.value) {
        isExpanded.value = !isExpanded.value
        // Track manual collapse only for connection-type nodes
        if (props.node.type === 'connection-type') {
            isManuallyCollapsed.value = !isExpanded.value
        }
    } else {
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

.tree-node.connection-type-node {
    position: sticky;
    top: 0;
    background-color: var(--reading-bg-secondary);
    z-index: 10;
    font-weight: 600;
    border-radius: 0;
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

.tree-node.selected-accent-subtle .item-count {
    color: inherit;
}
</style>
