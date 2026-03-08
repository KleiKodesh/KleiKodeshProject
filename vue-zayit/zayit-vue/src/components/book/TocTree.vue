<template>
    <div ref="containerRef"
         class="overflow-y height-fill">
        <div v-if="isLoading"
             class="flex-center height-fill">
            <LoadingSpinner text="טוען..." />
        </div>
        <div v-else-if="tocEntries.length === 0"
             class="flex-center height-fill">
            <Icon icon="fluent:book-open-24-regular" />
            <span class="text-secondary">אין תוכן עניינים זמין</span>
        </div>
        <template v-else>
            <TocTreeNode v-for="(entry, index) in tocEntries"
                             :key="entry.id"
                             :ref="el => { if (el) nodeRefs[index] = el as InstanceType<typeof TocTreeNode> }"
                             :entry="entry"
                             :is-compact-mode="$props.isCompactMode"
                             @select-line="emit('selectLine', $event)" />
        </template>
    </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { Icon } from '@iconify/vue'
import TocTreeNode from './TocTreeNode.vue'
import LoadingSpinner from '@/components/shared/LoadingSpinner.vue'
import { useListKeyboardNavigation } from '@/components/shared/useListKeyboardNavigation'
import type { TocEntry } from '@/data/types/BookToc'

defineProps<{
    tocEntries: TocEntry[]
    isLoading?: boolean
    isCompactMode?: boolean
}>()

const emit = defineEmits<{
    selectLine: [lineIndex: number]
    returnFocus: []
}>()

const nodeRefs = ref<InstanceType<typeof TocTreeNode>[]>([])
const containerRef = ref<HTMLElement>()

const { handleKeyDown } = useListKeyboardNavigation(containerRef, {
    onEscape: () => emit('returnFocus')
})

const resetTree = () => {
    nodeRefs.value.forEach(node => {
        if (node && node.reset) {
            node.reset()
        }
    })
}

defineExpose({
    resetTree,
    nodeRefs
})
</script>
