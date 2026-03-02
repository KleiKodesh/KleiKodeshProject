<template>
    <div class="filter-panel"
         :class="{ 'panel-open': isOpen }"
         :style="{ width: `${panelWidth}px` }"
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
import { ref, watch, onMounted } from 'vue'
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
const panelWidth = ref(200)
const checkedIds = ref(new Set(props.selectedIds))
const expandedIds = ref(new Set<string>())

onClickOutside(panelRef, () => props.isOpen && emit('close'), { ignore: props.ignoreElements || [] })

function updatePanelWidth() {
    if (!contentRef.value) return
    const labels = contentRef.value.querySelectorAll('.tree-label')
    let maxWidth = 180
    const temp = document.createElement('div')
    Object.assign(temp.style, { position: 'absolute', visibility: 'hidden', whiteSpace: 'nowrap', padding: '1px 2px', fontSize: '12px', fontFamily: getComputedStyle(contentRef.value).fontFamily })
    document.body.appendChild(temp)
    labels.forEach(label => {
        temp.textContent = label.textContent || ''
        maxWidth = Math.max(maxWidth, temp.offsetWidth)
    })
    document.body.removeChild(temp)
    panelWidth.value = Math.min(maxWidth + 80, 450)
}

watch(() => props.treeData, (newData) => {
    // Don't auto-expand - let user expand manually
    setTimeout(updatePanelWidth, 100)
}, { deep: true })

watch(() => props.isOpen, isOpen => {
    if (isOpen) {
        // Don't auto-expand on open
        setTimeout(updatePanelWidth, 100)
    }
})

watch(() => props.selectedIds, ids => checkedIds.value = new Set(ids), { deep: true })

onMounted(() => {
    if (props.isOpen) {
        // Don't auto-expand on mount
        setTimeout(updatePanelWidth, 100)
    }
})

function handleToggleExpand(nodeId: string) {
    const newExpandedIds = new Set(expandedIds.value)
    if (newExpandedIds.has(nodeId)) {
        newExpandedIds.delete(nodeId)
    } else {
        newExpandedIds.add(nodeId)
    }
    expandedIds.value = newExpandedIds
    setTimeout(updatePanelWidth, 100)
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
    transition: transform 0.2s, width 0.2s;
    box-shadow: -2px 0 8px rgba(0, 0, 0, 0.1);
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
