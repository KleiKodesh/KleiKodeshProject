<template>
    <Transition name="slide">
        <div class="flex-column height-fill book-toc-tree-view">
            <div class="overflow-y flex-110">
                <BookTocTreeSearch v-if="searchInput"
                                   ref="searchRef"
                                   :toc-entries="tocEntries"
                                   :search-query="searchInput"
                                   @select-line="handleSelectLine" />

                <BookTocTree v-else
                             :toc-entries="tocEntries"
                             :is-loading="isLoading"
                             ref="treeRef"
                             @select-line="handleSelectLine" />
            </div>

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
                       @keydown="handleKeyDown"
                       autofocus />
                <button @click="skipToDocument"
                        class="flex-center c-pointer"
                        title="דלג לתצוגת מסמך">
                    <Icon icon="fluent:chevron-left-28-regular" />
                </button>

            </div>
        </div>
    </Transition>
</template>

<script setup lang="ts">
import { ref, onMounted, nextTick, watch } from 'vue';
import BookTocTree from './BookTocTree.vue';
import BookTocTreeSearch from './BookTocTreeSearch.vue';
import { Icon } from '@iconify/vue';

import type { TocEntry } from '../types/BookToc';
import { useTabStore } from '../stores/tabStore';
import { dbManager } from '../data/dbManager';
import { buildTocFromFlat } from '../data/tocBuilder';

const props = defineProps<{
    bookId: number
}>();

const emit = defineEmits<{
    selectLine: [lineIndex: number]
}>();

const tabStore = useTabStore();

const searchInput = ref('');
const searchInputRef = ref<HTMLInputElement | null>(null);
const treeRef = ref<InstanceType<typeof BookTocTree> | null>(null);
const searchRef = ref<InstanceType<typeof BookTocTreeSearch> | null>(null);

// TOC state
const tocEntries = ref<TocEntry[]>([]);
const isLoading = ref(false);

// Load TOC when bookId changes
watch(() => props.bookId, async (bookId) => {
    if (bookId) {
        await loadToc(bookId);
    }
}, { immediate: true });

async function loadToc(bookId: number) {
    isLoading.value = true;
    try {
        const { tocEntriesFlat } = await dbManager.getToc(bookId);
        const { tree } = buildTocFromFlat(tocEntriesFlat);
        tocEntries.value = tree;
    } catch (error) {
        console.error('❌ Failed to load TOC:', error);
        tocEntries.value = [];
    } finally {
        isLoading.value = false;
    }
}

// Handle TOC line selection
const handleSelectLine = (lineIndex: number) => {
    emit('selectLine', lineIndex);
    tabStore.closeToc();
};

// Reset tree: clear search and collapse all
const resetTree = () => {
    searchInput.value = '';
    treeRef.value?.resetTree();
};

// Skip to document view (close TOC)
const skipToDocument = () => {
    tabStore.closeToc();
};

// Focus first item in tree or search results
const focusFirstItem = () => {
    const container = searchInput.value ? searchRef.value : treeRef.value;
    if (container) {
        const containerEl = (container as any).$el || container;
        const firstItem = containerEl.querySelector('[tabindex="0"]') as HTMLElement;
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

    // Arrow keys - focus first tree item
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


</script>

<style scoped>
.book-toc-tree-view {
    background-color: var(--bg-primary);
}

.search-bar {
    gap: 8px;
    padding: 6px 12px;
}
</style>
