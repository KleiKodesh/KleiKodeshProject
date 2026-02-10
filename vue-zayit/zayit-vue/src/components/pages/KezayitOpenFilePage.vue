<template>
    <div class="flex-column height-fill">
        <BookTreeSearch v-if="searchInput"
                        ref="searchRef"
                        :books="allBooks"
                        :search-query="searchInput"
                        class="flex-110" />
        <BookTree v-else
                  ref="treeRef"
                  class="flex-110" />

        <div class="bar flex-row search-bar">
            <button @click="resetTree"
                    class="flex-center c-pointer"
                    title="אפס עץ">
                <Icon icon="fluent:text-bullet-list-tree-24-regular"
                      class="rtl-flip" />
            </button>
            <input ref="searchInputRef"
                   v-model="searchInput"
                   type="text"
                   class="flex-110"
                   placeholder="חיפוש..."
                   @keydown="handleKeyDown" />
        </div>
    </div>
</template>

<script setup lang="ts">
import { ref, onMounted, nextTick, watch } from 'vue';
import BookTree from '../BookTree.vue';
import BookTreeSearch from '../BookTreeSearch.vue';
import { Icon } from '@iconify/vue';
import { useCategoryTreeStore } from '../../stores/categoryTreeStore';
import { storeToRefs } from 'pinia';

const categoryTreeStore = useCategoryTreeStore();
const { allBooks, isLoading } = storeToRefs(categoryTreeStore);

const searchInput = ref('');
const searchInputRef = ref<HTMLInputElement | null>(null);
const treeRef = ref<InstanceType<typeof BookTree> | null>(null);
const searchRef = ref<InstanceType<typeof BookTreeSearch> | null>(null);

// Reset tree: clear search and collapse all
const resetTree = () => {
    searchInput.value = '';
    treeRef.value?.resetTree();
};

// Focus first item in tree or search results
const focusFirstItem = () => {
    const container = searchInput.value ? searchRef.value?.$el : treeRef.value?.$el;
    if (container) {
        const firstItem = container.querySelector('[tabindex="0"]') as HTMLElement;
        firstItem?.focus();
    }
};

// Handle keyboard shortcuts on search input
const handleKeyDown = (e: KeyboardEvent) => {
    // Escape key - reset tree
    if (e.key === 'Escape') {
        resetTree();
        return;
    }

    // Arrow keys - focus first tree/search item
    if (e.key === 'ArrowUp' || e.key === 'ArrowDown') {
        e.preventDefault();
        focusFirstItem();
    }
};

// Focus search input on mount
onMounted(() => {
    nextTick(() => {
        searchInputRef.value?.focus();
    });
});

// Restore focus when tree finishes loading
watch(isLoading, (newIsLoading, oldIsLoading) => {
    // When loading changes from true to false (tree finished loading)
    if (oldIsLoading && !newIsLoading) {
        nextTick(() => {
            searchInputRef.value?.focus();
        });
    }
});
</script>

<style scoped>
.search-bar {
    gap: 8px;
    padding: 6px 12px;
}
</style>