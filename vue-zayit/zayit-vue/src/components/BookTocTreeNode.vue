<template>
    <div role="treeitem">
        <div class="flex-row hover-bg focus-accent click-effect c-pointer tree-node"
             tabindex="0"
             :style="{ paddingInlineStart: `${20 + entry.level * 20}px` }"
             @click="toggleExpand"
             @keydown.enter.stop="handleSelect"
             @keydown.space.stop.prevent="toggleExpand">

            <Icon icon="fluent:chevron-left-28-regular"
                  v-if="entry.hasChildren"
                  :class="{ 'rotate-90': isExpanded }" />
            <div class="flex-110 line-1.4 node-title"
                 @click="handleSelect">{{ entry.text }}</div>
        </div>

        <template v-if="isExpanded && entry.children">
            <BookTocTreeNode v-for="(child, index) in entry.children"
                             :key="child.id"
                             :ref="(el: any) => { if (el) childRefs[index] = el }"
                             :entry="child"
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

type BookTocTreeNodeInstance = {
    collapse: () => void
    reset: () => void
}

const props = defineProps<{
    entry: TocEntry
}>()

const emit = defineEmits<{
    selectLine: [lineIndex: number]
}>()

const isExpanded = ref(props.entry.isExpanded || false)
const childRefs = ref<BookTocTreeNodeInstance[]>([])

const toggleExpand = () => {
    if (props.entry.hasChildren) {
        isExpanded.value = !isExpanded.value
    }
}

const handleSelect = () => {
    emit('selectLine', props.entry.lineIndex)
}

const collapse = () => {
    isExpanded.value = false
}

const reset = () => {
    isExpanded.value = false
    childRefs.value.forEach((child: BookTocTreeNodeInstance) => {
        if (child && child.reset) {
            child.reset()
        }
    })
}

defineExpose({
    collapse,
    reset
})
</script>

<style scoped>
.node-title {
    margin: -12px 0px;
    padding: 12px 0px;
}
</style>
