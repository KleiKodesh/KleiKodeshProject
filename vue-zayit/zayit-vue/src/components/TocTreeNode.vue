<template>
    <div role="treeitem">
        <div class="flex-row hover-bg focus-accent click-effect c-pointer tree-node"
             :class="{ 'compact': isCompactMode }"
             tabindex="0"
             :style="{ paddingInlineStart: `${(isCompactMode ? 12 : 20) + depth * (isCompactMode ? 16 : 20)}px` }"
             @click="toggleExpand"
             @keydown.enter.stop="handleSelect"
             @keydown.space.stop.prevent="toggleExpand">

            <Icon icon="fluent:chevron-left-28-regular"
                  v-if="entry.hasChildren"
                  :class="{ 'rotate-90': isExpanded, 'compact-icon': isCompactMode }" />
            <div class="flex-110 node-title"
                 :class="{ 'compact-text': isCompactMode }"
                 @click="handleSelect">{{ entry.text }}</div>
        </div>

        <template v-if="isExpanded && entry.children">
            <TocTreeNode v-for="(child, index) in entry.children"
                         :key="child.id"
                         :ref="(el: any) => { if (el) childRefs[index] = el }"
                         :entry="child"
                         :depth="depth + 1"
                         :is-compact-mode="isCompactMode"
                         @select-line="emit('selectLine', $event)" />
        </template>
    </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { Icon } from '@iconify/vue'
import type { TocEntry } from '../types/BookToc'
import { useTabStore } from '../stores/tabStore'
import { storeToRefs } from 'pinia'

type TocTreeNodeInstance = {
    expand: () => void
    collapse: () => void
    reset: () => void
    entry: TocEntry
}

const props = withDefaults(defineProps<{
    entry: TocEntry
    depth?: number
    isCompactMode?: boolean
}>(), {
    depth: 0,
    isCompactMode: false
})

const emit = defineEmits<{
    selectLine: [lineIndex: number]
}>()

const isExpanded = ref(props.entry.isExpanded || false)
const childRefs = ref<TocTreeNodeInstance[]>([])

const toggleExpand = () => {
    if (props.entry.hasChildren) {
        isExpanded.value = !isExpanded.value
    }
}

const handleSelect = () => {
    emit('selectLine', props.entry.lineIndex)
}

const expand = () => {
    isExpanded.value = true
}

const collapse = () => {
    isExpanded.value = false
}

const reset = () => {
    isExpanded.value = false
    childRefs.value.forEach((child: TocTreeNodeInstance) => {
        if (child && child.reset) {
            child.reset()
        }
    })
}

defineExpose({
    expand,
    collapse,
    reset,
    childRefs
})
</script>

<style scoped>
.node-title {
    margin: -12px 0px;
    padding: 12px 0px;
    line-height: 1.4;
    white-space: nowrap;
    min-width: 0;
}

.tree-node.compact {
    min-height: 32px;
}

.compact-text {
    font-size: 0.9em;
    margin: -8px 0px;
    padding: 8px 0px;
    line-height: 1.3;
}

.compact-icon {
    font-size: 0.9em;
}
</style>
