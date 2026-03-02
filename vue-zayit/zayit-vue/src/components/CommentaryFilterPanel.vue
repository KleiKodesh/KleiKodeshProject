<template>
    <div class="filter-panel"
         :class="{ 'panel-open': isOpen }"
         ref="panelRef">
        <div class="panel-content"
             ref="contentRef">
            <div v-if="isLoading"
                 class="panel-center">
                <LoadingSpinner text="טוען..." />
            </div>
            <div v-else-if="treeData.length">
                <CommentaryCheckedTreeNode v-for="node in treeData"
                                           :key="node.id"
                                           :node="node"
                                           :depth="0"
                                           :checked-ids="checkedIds"
                                           :expanded-ids="expandedIds"
                                           @toggle-check="handleToggleCheck"
                                           @toggle-expand="handleToggleExpand" />
            </div>
            <div v-else
                 class="panel-center">
                <span class="empty-text">אין פרשנים זמינים</span>
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { ref, watch } from 'vue'
import { onClickOutside } from '@vueuse/core'
import CommentaryCheckedTreeNode, { type TreeNode } from './CommentaryCheckedTreeNode.vue'
import LoadingSpinner from './common/LoadingSpinner.vue'

const props = defineProps<{
    isOpen: boolean
    treeData: TreeNode[]
    isLoading: boolean
    selectedIds: Set<string>
    ignoreElements?: (HTMLElement | null)[]
}>()

const emit = defineEmits<{ close: [], 'update:selectedIds': [ids: Set<string>] }>()

const panelRef = ref<HTMLElement>()
const contentRef = ref<HTMLElement>()
const checkedIds = ref(new Set(props.selectedIds))
const expandedIds = ref(new Set<string>())
const isTogglingFromButton = ref(false)

onClickOutside(panelRef, (event) => {
    console.log('👆 Click outside detected on filter panel')
    console.log('   - Panel open:', props.isOpen)
    console.log('   - Click target:', event.target)
    console.log('   - Ignore elements:', props.ignoreElements)
    console.log('   - Is toggling from button:', isTogglingFromButton.value)

    // Don't close if we're in the middle of toggling from the button
    if (isTogglingFromButton.value) {
        console.log('   - Skipping close (button toggle in progress)')
        isTogglingFromButton.value = false
        return
    }

    if (props.isOpen) {
        console.log('   - Emitting close event')
        emit('close')
    }
}, { ignore: props.ignoreElements || [] })

// Watch for panel opening to set the toggle flag
watch(() => props.isOpen, (newValue, oldValue) => {
    if (newValue && !oldValue) {
        // Panel just opened, likely from button click
        isTogglingFromButton.value = true
        // Reset after a short delay
        setTimeout(() => {
            isTogglingFromButton.value = false
        }, 100)
    }
})

watch(() => props.selectedIds, ids => checkedIds.value = new Set(ids), { deep: true })

watch(() => props.ignoreElements, (elements) => {
    console.log('🎯 Ignore elements updated:', elements)
}, { deep: true, immediate: true })

function handleToggleExpand(nodeId: string) {
    const newExpandedIds = new Set(expandedIds.value)
    if (newExpandedIds.has(nodeId)) {
        newExpandedIds.delete(nodeId)
    } else {
        newExpandedIds.add(nodeId)
    }
    expandedIds.value = newExpandedIds
}

function handleToggleCheck(nodeId: string) {
    const newIds = new Set(checkedIds.value)
    const node = findNode(props.treeData, nodeId)
    if (!node) return
    const descendants = getDescendants(node)
    if (newIds.has(nodeId)) {
        newIds.delete(nodeId)
        descendants.forEach(id => newIds.delete(id))
    } else {
        newIds.add(nodeId)
        descendants.forEach(id => newIds.add(id))
    }
    checkedIds.value = newIds
    emit('update:selectedIds', newIds)
}

function findNode(nodes: TreeNode[], id: string): TreeNode | null {
    for (const node of nodes) {
        if (node.id === id) return node
        if (node.children) {
            const found = findNode(node.children, id)
            if (found) return found
        }
    }
    return null
}

function getDescendants(node: TreeNode): string[] {
    return node.children?.flatMap(child => [child.id, ...getDescendants(child)]) || []
}
</script>

<style scoped>
.filter-panel {
    position: absolute;
    inset: 0 0 0 auto;
    background: var(--bg-primary);
    border-left: 1px solid var(--border-color);
    z-index: 100;
    transform: translateX(100%);
    transition: transform 0.2s;
    box-shadow: -2px 0 8px rgba(0, 0, 0, 0.1);
    width: fit-content;
    max-width: 450px;
}

.panel-open {
    transform: translateX(0);
}

.panel-content {
    height: 100%;
    overflow-y: auto;
    overflow-x: hidden;
    direction: rtl;
}

.panel-center {
    display: flex;
    align-items: center;
    justify-content: center;
    height: 100%;
}

.empty-text {
    font-size: 13px;
    color: var(--text-secondary);
}
</style>
