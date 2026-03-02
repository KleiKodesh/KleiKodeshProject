<template>
    <div class="flex-column height-fill">
        <FsTreeSearch v-if="searchInput"
                      ref="searchRef"
                      :books="allBooks"
                      :search-query="searchInput"
                      class="flex-110"
                      @return-focus="returnFocusToSearch" />
        <FsTree v-else
                ref="treeRef"
                class="flex-110"
                @return-focus="returnFocusToSearch" />

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
import FsTree from '../FsTree.vue';
import FsTreeSearch from '../FsTreeSearch.vue';
import { Icon } from '@iconify/vue';
import { useCategoryTreeStore } from '../../stores/categoryTreeStore';
import { storeToRefs } from 'pinia';

const categoryTreeStore = useCategoryTreeStore();
const { allBooks, isLoading } = storeToRefs(categoryTreeStore);

const searchInput = ref('');
const searchInputRef = ref<HTMLInputElement | null>(null);
const treeRef = ref<InstanceType<typeof FsTree> | null>(null);
const searchRef = ref<InstanceType<typeof FsTreeSearch> | null>(null);

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

// Return focus to search input
const returnFocusToSearch = () => {
    nextTick(() => {
        searchInputRef.value?.focus();
    });
};

// Handle keyboard shortcuts on search input
const handleKeyDown = (e: KeyboardEvent) => {
    // Escape key - reset tree
    if (e.key === 'Escape') {
        resetTree();
        return;
    }

    // Tab key - focus first tree/search item
    if (e.key === 'Tab') {
        e.preventDefault();
        focusFirstItem();
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