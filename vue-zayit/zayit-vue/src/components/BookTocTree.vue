<template>
    <div ref="containerRef"
         class="overflow-y height-fill"
         @keydown="navigator?.handleKeyDown">
        <div v-if="isLoading"
             class="flex-center height-fill">
            <Icon icon="fluent:spinner-ios-20-regular"
                  class="loading-spinner" />
            <span class="text-secondary">טוען...</span>
        </div>
        <div v-else-if="tocEntries.length === 0"
             class="flex-center height-fill">
            <Icon icon="fluent:book-open-24-regular" />
            <span class="text-secondary">אין תוכן עניינים זמין</span>
        </div>
        <template v-else>
            <BookTocTreeNode v-for="(entry, index) in tocEntries"
                             :key="entry.id"
                             :ref="el => { if (el) nodeRefs[index] = el as InstanceType<typeof BookTocTreeNode> }"
                             :entry="entry"
                             @select-line="emit('selectLine', $event)" />
        </template>
    </div>
</template>

<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue';
import { Icon } from '@iconify/vue';
import BookTocTreeNode from './BookTocTreeNode.vue';
import { KeyboardNavigator } from '../utils/KeyboardNavigator';
import type { TocEntry } from '../types/BookToc';

defineProps<{
    tocEntries: TocEntry[]
    isLoading?: boolean
}>();

const emit = defineEmits<{
    selectLine: [lineIndex: number]
}>();

const nodeRefs = ref<InstanceType<typeof BookTocTreeNode>[]>([]);
const containerRef = ref<HTMLElement>();
const navigator = ref<KeyboardNavigator>();

onMounted(() => {
    if (containerRef.value) {
        navigator.value = new KeyboardNavigator(containerRef.value);
    }
});

onUnmounted(() => {
    navigator.value?.destroy();
});

const resetTree = () => {
    nodeRefs.value.forEach(node => {
        if (node && node.reset) {
            node.reset();
        }
    });
}

defineExpose({
    resetTree
})
</script>
