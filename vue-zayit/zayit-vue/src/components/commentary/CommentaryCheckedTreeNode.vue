<template>
    <div class="tree-node">
        <div class="tree-node-content hover-bg click-effect c-pointer"
             :class="{ 'is-category': hasChildren }"
             @click="handleClick">
            <!-- Expand/Collapse Arrow -->
            <Icon v-if="hasChildren"
                  :icon="isExpanded ? 'fluent:chevron-down-16-filled' : 'fluent:chevron-left-16-filled'"
                  class="tree-arrow"
                  @click.stop="toggleExpand" />
            <span v-else
                  class="tree-arrow-spacer"></span>

            <!-- Checkbox -->
            <input type="checkbox"
                   class="tree-checkbox"
                   :checked="isChecked"
                   :indeterminate.prop="isIndeterminate"
                   @click.stop
                   @change="handleCheckChange" />

            <!-- Label -->
            <span class="tree-label"
                  :class="{ 'category-label': hasChildren }">{{ node.label }}</span>
        </div>

        <!-- Children (conditionally shown) -->
        <div v-if="hasChildren && isExpanded"
             class="tree-children">
            <CommentaryCheckedTreeNode v-for="child in node.children"
                                       :key="child.id"
                                       :node="child"
                                       :depth="depth + 1"
                                       :checked-ids="checkedIds"
                                       :expanded-ids="expandedIds"
                                       @toggle-check="$emit('toggle-check', $event)"
                                       @toggle-expand="$emit('toggle-expand', $event)" />
            <!-- Separator after category -->
            <div class="category-separator"></div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { Icon } from '@iconify/vue'

export interface TreeNode {
    id: string
    label: string
    count?: number
    children?: TreeNode[]
    bookId?: number
}

const props = defineProps<{
    node: TreeNode
    depth: number
    checkedIds: Set<string>
    expandedIds: Set<string>
}>()

const emit = defineEmits<{
    'toggle-check': [nodeId: string]
    'toggle-expand': [nodeId: string]
}>()

const hasChildren = computed(() => props.node.children && props.node.children.length > 0)
const isExpanded = computed(() => props.expandedIds.has(props.node.id))

const isChecked = computed(() => {
    if (props.checkedIds.has(props.node.id)) return true
    if (hasChildren.value && props.node.children) {
        return props.node.children.every(child => props.checkedIds.has(child.id))
    }
    return false
})

const isIndeterminate = computed(() => {
    if (!hasChildren.value) return false
    const childIds = getAllDescendantIds(props.node)
    const checkedCount = childIds.filter(id => props.checkedIds.has(id)).length
    return checkedCount > 0 && checkedCount < childIds.length
})

function handleClick() {
    handleCheckChange()
}

function toggleExpand() {
    emit('toggle-expand', props.node.id)
}

function handleCheckChange() {
    emit('toggle-check', props.node.id)
}

function getAllDescendantIds(node: TreeNode): string[] {
    const ids: string[] = []
    if (node.children) {
        node.children.forEach(child => {
            ids.push(child.id)
            ids.push(...getAllDescendantIds(child))
        })
    }
    return ids
}
</script>

<style scoped>
.tree-node {
    user-select: none;
    padding: 0 !important;
    margin: 0 !important;
    gap: 0 !important;
    min-height: unset !important;
}

.tree-node-content {
    display: flex;
    align-items: center;
    gap: 6px;
    padding: 6px 12px;
    border-radius: 2px;
    transition: background-color 0.1s ease;
}

.tree-node-content.is-category {
    /* Same padding as regular items */
}

.tree-arrow {
    width: 16px;
    height: 16px;
    flex-shrink: 0;
    color: var(--text-secondary);
    cursor: pointer;
}

.tree-arrow-spacer {
    width: 16px;
    flex-shrink: 0;
}

.tree-checkbox {
    cursor: pointer;
    width: 14px;
    height: 14px;
    flex-shrink: 0;
    margin: 0;
}

.tree-label {
    font-size: 13px;
    color: var(--text-primary);
    white-space: nowrap;
    line-height: 1.2;
}

.tree-label.category-label {
    font-weight: 500;
    font-size: 13.5px;
}

.category-separator {
    height: 1px;
    background: var(--border-color);
    margin: 2px 0;
    opacity: 0.5;
}

@media (hover: none) and (pointer: coarse) {
    .tree-node-content {
        padding: 10px 16px;
        gap: 8px;
    }

    .tree-node-content.is-category {
        /* Same padding as regular items */
    }

    .tree-arrow {
        width: 20px;
        height: 20px;
    }

    .tree-arrow-spacer {
        width: 20px;
    }

    .tree-checkbox {
        width: 18px;
        height: 18px;
    }

    .tree-label {
        font-size: 15px;
    }

    .tree-label.category-label {
        font-size: 16px;
    }

    .category-separator {
        margin: 6px 0;
    }
}
</style>
