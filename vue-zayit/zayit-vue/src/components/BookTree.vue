<template>
    <div ref="treeContainerRef"
         class="overflow-y height-fill"
         @keydown="navigator?.handleKeyDown">
        <div v-if="categoryTreeStore.isLoading"
             class="height-fill flex-center">
            <Icon icon="fluent:spinner-ios-20-regular"
                  class="loading-spinner" />
        </div>
        <template v-else>
            <BookTreeCategoryNode v-for="(category, index) in categoryTreeStore.categoryTree"
                                  :key="category.id"
                                  :ref="el => { if (el) nodeRefs[index] = el as InstanceType<typeof BookTreeCategoryNode> }"
                                  :category="category" />
        </template>
    </div>
</template>

<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue';

import { useCategoryTreeStore } from '../stores/categoryTreeStore';
import BookTreeCategoryNode from './BookTreeCategoryNode.vue';
import { KeyboardNavigator } from '../utils/KeyboardNavigator';
import { Icon } from '@iconify/vue';

const categoryTreeStore = useCategoryTreeStore();
const nodeRefs = ref<InstanceType<typeof BookTreeCategoryNode>[]>([]);
const treeContainerRef = ref<HTMLElement>();
const navigator = ref<KeyboardNavigator>();

onMounted(() => {
    if (treeContainerRef.value) {
        navigator.value = new KeyboardNavigator(treeContainerRef.value);
    }
});

onUnmounted(() => {
    navigator.value?.destroy();
});

const resetTree = () => {
    // Reset all root nodes (which will recursively reset their children)
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
