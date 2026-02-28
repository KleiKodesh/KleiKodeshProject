<template>
    <div ref="treeContainerRef"
         class="overflow-y height-fill">
        <div v-if="categoryTreeStore.isLoading"
             class="height-fill flex-center">
            <LoadingSpinner />
        </div>
        <div v-else-if="categoryTreeStore.error"
             class="height-fill flex-center error-message">
            <div>
                <p>שגיאה בטעינת עץ הספרים:</p>
                <p class="error-details">{{ categoryTreeStore.error }}</p>
            </div>
        </div>
        <div v-else-if="categoryTreeStore.categoryTree.length === 0"
             class="height-fill flex-center error-message">
            <p>לא נמצאו ספרים. אנא בדוק את חיבור מסד הנתונים.</p>
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
import LoadingSpinner from './common/LoadingSpinner.vue'
import { useListKeyboardNavigation } from '../composables/useListKeyboardNavigation'

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

<style scoped>
.error-message {
    color: var(--text-secondary);
    padding: 20px;
    text-align: center;
}

.error-details {
    color: var(--error-color, #e74c3c);
    font-size: 0.9em;
    margin-top: 8px;
    font-family: monospace;
}
</style>