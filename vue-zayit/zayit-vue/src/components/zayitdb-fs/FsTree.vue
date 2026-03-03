<template>
    <div ref="treeContainerRef"
         class="overflow-y height-fill">
        <div v-if="isLoading"
             class="height-fill flex-center">
            <LoadingSpinner />
        </div>
        <div v-else-if="error"
             class="height-fill flex-center error-message">
            <div>
                <p>שגיאה בטעינת עץ הספרים:</p>
                <p class="error-details">{{ error }}</p>
            </div>
        </div>
        <div v-else-if="categoryTree.length === 0"
             class="height-fill flex-center error-message">
            <p>לא נמצאו ספרים. אנא בדוק את חיבור מסד הנתונים.</p>
        </div>
        <template v-else>
            <FsCategoryNode v-for="(category, index) in categoryTree"
                                  :key="category.id"
                                  :ref="el => { if (el) nodeRefs[index] = el as InstanceType<typeof FsCategoryNode> }"
                                  :category="category" />
        </template>
    </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { useCategoryTreeStore } from '@/data/stores/categoryTreeStore'
import { useZayitFs } from '@/components/zayitdb-fs/useZayitFs'
import FsCategoryNode from './FsCategoryNode.vue'
import LoadingSpinner from '@/components/shared/LoadingSpinner.vue'
import { useListKeyboardNavigation } from '@/components/shared/useListKeyboardNavigation'

const emit = defineEmits<{
    returnFocus: []
}>()

const categoryTreeStore = useCategoryTreeStore()
const { categoryTree } = useZayitFs()
const isLoading = ref(categoryTreeStore.isLoading)
const error = ref(categoryTreeStore.error)
const nodeRefs = ref<InstanceType<typeof FsCategoryNode>[]>([])
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