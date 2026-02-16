<template>
    <Transition name="slide">
        <div class="flex-column height-fill book-toc-tree-view"
             :class="{ 'compact-mode': isCompactMode }"
             @click.stop>
            <div class="overflow-y flex-110">
                <BookTocTreeSearch v-if="searchInput"
                                   ref="searchRef"
                                   :toc-entries="props.tocEntries"
                                   :search-query="searchInput"
                                   :is-compact-mode="isCompactMode"
                                   @select-line="handleSelectLine"
                                   @return-focus="returnFocusToSearch" />

                <BookTocTree v-else
                             :toc-entries="props.tocEntries"
                             :is-loading="props.isLoading"
                             :is-compact-mode="isCompactMode"
                             ref="treeRef"
                             @select-line="handleSelectLine"
                             @return-focus="returnFocusToSearch" />
            </div>

            <div class="bar flex-row search-bar">
                <div class="search-input-container flex-110">
                    <input ref="searchInputRef"
                           v-model="searchInput"
                           type="text"
                           class="search-input-field"
                           placeholder="חיפוש כותרת..."
                           @keydown="handleKeyDown"
                           autofocus />
                    <button @click="resetTree"
                            class="reset-button flex-center c-pointer"
                            title="אפס עץ">
                        <Icon icon="fluent:text-bullet-list-tree-24-regular"
                              class="rtl-flip" />
                    </button>
                </div>
            </div>
        </div>
    </Transition>
</template>

<script setup lang="ts">
import { ref, onMounted, onUnmounted, nextTick, watch } from 'vue';
import { useEventListener } from '@vueuse/core';
import BookTocTree from './BookTocTree.vue';
import BookTocTreeSearch from './BookTocTreeSearch.vue';
import { Icon } from '@iconify/vue';

import type { TocEntry } from '../types/BookToc';
import { useTabStore } from '../stores/tabStore';

const props = defineProps<{
    tocEntries: TocEntry[]
    isLoading?: boolean
    isCompactMode?: boolean
}>();

const emit = defineEmits<{
    selectLine: [lineIndex: number]
}>();

const tabStore = useTabStore();

const searchInput = ref('');
const searchInputRef = ref<HTMLInputElement | null>(null);
const treeRef = ref<InstanceType<typeof BookTocTree> | null>(null);
const searchRef = ref<InstanceType<typeof BookTocTreeSearch> | null>(null);

// Handle TOC line selection
const handleSelectLine = (lineIndex: number) => {
    emit('selectLine', lineIndex);
    // Only close TOC automatically in full-width mode (first time open)
    // In compact mode, keep it open for easier navigation
    if (!props.isCompactMode) {
        tabStore.closeToc();
    }
};

// Reset tree: clear search and collapse all
const resetTree = () => {
    searchInput.value = '';
    treeRef.value?.resetTree();
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

// Return focus to search input
const returnFocusToSearch = () => {
    nextTick(() => {
        searchInputRef.value?.focus();
    });
};

// Handle keyboard shortcuts on search input
const handleKeyDown = (e: KeyboardEvent) => {
    // Escape key - close TOC or reset tree
    if (e.key === 'Escape') {
        if (searchInput.value) {
            // If there's search text, clear it first
            resetTree();
        } else {
            // If no search text, close the TOC
            tabStore.closeToc();
        }
        return;
    }

    // Tab key - focus first tree/search item
    if (e.key === 'Tab') {
        e.preventDefault();
        focusFirstItem();
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

// Global Escape key handler
useEventListener('keydown', (event: KeyboardEvent) => {
    if (event.code === 'Escape') {
        tabStore.closeToc()
    }
})

watch(() => tabStore.activeTab?.bookState?.isTocOpen, (isOpen) => {
    if (isOpen) {
        nextTick(() => {
            searchInputRef.value?.focus();
        });
    }
});

</script>

<style scoped>
.book-toc-tree-view {
    background-color: var(--bg-primary);
}

.book-toc-tree-view.compact-mode {
    background-color: rgba(var(--bg-primary-rgb), 0.95);
    width: max-content;
    min-width: 200px;
    max-width: 500px;
    border-left: 1px solid rgba(0, 0, 0, 0.1);
    height: 100%;
    position: absolute;
    right: 0;
    top: 0;
    z-index: 100;
}

.search-bar {
    gap: 8px;
    padding: 6px 12px;
    flex-shrink: 0;
}

.compact-mode .search-bar {
    padding: 4px 8px;
}

.search-input-container {
    position: relative;
    display: flex;
    align-items: center;
}

.search-input-field {
    width: 100%;
    min-width: 150px;
    padding-right: 32px;
    /* Make room for the reset button */
}

.compact-mode .search-input-field {
    min-width: 150px;
}

.reset-button {
    position: absolute;
    right: 4px;
    width: 24px;
    height: 24px;
    border: none;
    background: transparent;
    color: var(--text-secondary);
    border-radius: 6px;
}

.reset-button:hover {
    background: var(--hover-bg);
    color: var(--text-primary);
}
</style>
