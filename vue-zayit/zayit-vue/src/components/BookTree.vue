<template>
    <div ref="treeContainerRef"
         class="overflow-y height-fill">
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
import { ref } from 'vue'
import { useCategoryTreeStore } from '../stores/categoryTreeStore'
import BookTreeCategoryNode from './BookTreeCategoryNode.vue'
import { useListKeyboardNavigation } from '../composables/useListKeyboardNavigation'
import { Icon } from '@iconify/vue'

const emit = defineEmits<{
    returnFocus: []
}>()

const categoryTreeStore = useCategoryTreeStore()
const nodeRefs = ref<InstanceType<typeof BookTreeCategoryNode>[]>([])
const treeContainerRef = ref<HTMLElement>()

const { handleKeyDown } = useListKeyboardNavigation(treeContainerRef, {
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
    resetTree
})
</script>
