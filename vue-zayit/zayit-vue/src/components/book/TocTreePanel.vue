<template>
    <Transition name="slide">
        <div class="flex-column height-fill book-toc-tree-view"
             :class="{ 'compact-mode': isCompactMode }"
             @click.stop>
            <div class="flex-110 flex-column toc-content">
                <!-- Loading state -->
                <div v-if="props.isLoading"
                     class="flex-center height-fill">
                    <LoadingSpinner text="טוען..." />
                </div>

                <!-- Unified search results -->
                <TocTreeSearch v-else-if="searchInput"
                               ref="searchRef"
                               :toc-entries="props.tocEntries"
                               :search-query="searchInput"
                               :is-compact-mode="isCompactMode"
                               class="overflow-y flex-110"
                               @select-line="handleSelectLine"
                               @return-focus="returnFocusToSearch" />

                <!-- Split view: Regular TOC and Alt TOC -->
                <template v-else>
                    <!-- Regular TOC -->
                    <div v-if="regularTocEntries.length > 0"
                         class="toc-section overflow-y">
                        <TocTree :toc-entries="regularTocEntries"
                                 :is-loading="props.isLoading"
                                 :is-compact-mode="isCompactMode"
                                 ref="regularTreeRef"
                                 @select-line="handleSelectLine"
                                 @return-focus="returnFocusToSearch" />
                    </div>

                    <!-- Alt TOC (חלוקה אחרת) -->
                    <div v-if="altTocEntries.length > 0 && props.showAltToc !== false"
                         class="toc-section overflow-y alt-toc-section">
                        <TocTree :toc-entries="altTocEntries"
                                 :is-loading="props.isLoading"
                                 :is-compact-mode="isCompactMode"
                                 ref="altTreeRef"
                                 @select-line="handleSelectLine"
                                 @return-focus="returnFocusToSearch" />
                    </div>
                </template>
            </div>

            <div class="bar flex-row search-bar">
                <div class="search-input-container"
                     :class="{ 'flex-110': !isCompactMode }">
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
import { ref, computed, onMounted, nextTick, watch } from 'vue';
import { useEventListener } from '@vueuse/core';
import TocTree from './TocTree.vue';
import TocTreeSearch from './TocTreeSearch.vue';
import LoadingSpinner from '@/components/shared/LoadingSpinner.vue';
import { Icon } from '@iconify/vue';
import { scrollToElementCentered } from '@/components/shared/useScrollToElement';

import type { TocEntry } from '@/data/types/BookToc';
import { useTabs } from '@/components/workspace/useTabs';

const props = defineProps<{
    tocEntries: TocEntry[]
    isLoading?: boolean
    isCompactMode?: boolean
    currentTocEntryId?: number
    showAltToc?: boolean
}>();

const emit = defineEmits<{
    selectLine: [lineIndex: number]
}>();

const { activeTab, closeToc } = useTabs();

const searchInput = ref('');
const searchInputRef = ref<HTMLInputElement | null>(null);
const regularTreeRef = ref<InstanceType<typeof TocTree> | null>(null);
const altTreeRef = ref<InstanceType<typeof TocTree> | null>(null);
const searchRef = ref<InstanceType<typeof TocTreeSearch> | null>(null);

// Split TOC entries into regular and alt
const regularTocEntries = computed(() => {
    return props.tocEntries.filter(entry => !entry.isAltToc);
});

const altTocEntries = computed(() => {
    return props.tocEntries.filter(entry => entry.isAltToc);
});

// Handle TOC line selection
const handleSelectLine = (lineIndex: number) => {
    emit('selectLine', lineIndex);
    if (!props.isCompactMode) {
        closeToc();
    }
};

// Reset tree: clear search and collapse all
const resetTree = () => {
    searchInput.value = '';
    regularTreeRef.value?.resetTree();
    altTreeRef.value?.resetTree();
};

// Focus first item in tree or search results
const focusFirstItem = () => {
    const container = searchInput.value ? searchRef.value : regularTreeRef.value;
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
    if (e.key === 'Escape') {
        if (searchInput.value) {
            resetTree();
        } else {
            closeToc();
        }
        return;
    }

    if (e.key === 'Enter') {
        if (!props.isCompactMode) {
            closeToc();
        }
        return;
    }

    if (e.key === 'Tab') {
        e.preventDefault();
        focusFirstItem();
        return;
    }

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
        closeToc()
    }
})

watch(() => activeTab.value?.bookState?.isTocOpen, (isOpen) => {
    if (isOpen) {
        nextTick(() => {
            searchInputRef.value?.focus();

            if (props.currentTocEntryId) {
                autoSelectTocEntry(props.currentTocEntryId);
            }
        });
    }
});

watch(() => props.currentTocEntryId, (tocEntryId) => {
    if (activeTab.value?.bookState?.isTocOpen && tocEntryId) {
        autoSelectTocEntry(tocEntryId);
    }
});

// Auto-select and scroll to the TOC entry (only for regular TOC, not alt TOC)
function autoSelectTocEntry(tocEntryId: number) {
    // Only auto-select in regular TOC tree, ignore alt TOC entries
    const entry = findEntryById(regularTocEntries.value, tocEntryId);
    if (!entry || !regularTreeRef.value) return;

    const path = findEntryPath(regularTocEntries.value, tocEntryId);
    if (!path) return;

    expandNodesAlongPath(regularTreeRef.value, path);

    nextTick(async () => {
        const treeEl = (regularTreeRef.value as any)?.$el || regularTreeRef.value;
        if (treeEl) {
            const previousHighlights = treeEl.querySelectorAll('.highlight-current');
            previousHighlights.forEach((el: Element) => el.classList.remove('highlight-current'));

            const allNodes = treeEl.querySelectorAll('[role="treeitem"]');
            for (const node of allNodes) {
                const nodeText = node.querySelector('.node-title')?.textContent;
                if (entry && nodeText === entry.text) {
                    const treeNodeDiv = node.querySelector('.tree-node') as HTMLElement;

                    if (treeNodeDiv) {
                        treeNodeDiv.classList.add('highlight-current');
                    } else {
                        (node as HTMLElement).classList.add('highlight-current');
                    }

                    await scrollToElementCentered(node as HTMLElement);
                    break;
                }
            }
        }
    });
}

// Find entry path by ID
function findEntryPath(entries: TocEntry[], id: number, path: number[] = []): number[] | null {
    for (let i = 0; i < entries.length; i++) {
        const entry = entries[i];
        if (!entry) continue;

        if (entry.id === id) return [...path, i];
        if (entry.children) {
            const found = findEntryPath(entry.children, id, [...path, i]);
            if (found) return found;
        }
    }
    return null;
}

// Helper to find entry by ID
function findEntryById(entries: TocEntry[], id: number): TocEntry | null {
    for (const entry of entries) {
        if (entry.id === id) return entry;
        if (entry.children) {
            const found = findEntryById(entry.children, id);
            if (found) return found;
        }
    }
    return null;
}

// Expand nodes along the path
function expandNodesAlongPath(treeRef: InstanceType<typeof TocTree>, path: number[]) {
    const nodeRefs = (treeRef as any).nodeRefs;
    if (!nodeRefs) return;

    let currentNodes = nodeRefs;
    for (let i = 0; i < path.length - 1; i++) {
        const nodeIndex = path[i];
        if (nodeIndex === undefined) continue;
        const node = currentNodes[nodeIndex];

        if (node && node.expand) {
            node.expand();
            if (node.childRefs) {
                currentNodes = node.childRefs;
            }
        }
    }
}

</script>

<style scoped>
.book-toc-tree-view {
    background-color: var(--bg-primary);
}

.book-toc-tree-view.compact-mode {
    background-color: rgba(var(--bg-primary-rgb), 0.95);
    width: fit-content;
    min-width: 150px;
    max-width: 90vw;
    border-left: 1px solid rgba(0, 0, 0, 0.1);
    height: 100%;
    position: absolute;
    right: 0;
    top: 0;
    z-index: 100;
}

.toc-content {
    min-height: 0;
}

.compact-mode .toc-content {
    width: max-content;
    padding-bottom: 40px;
    min-width: 100%;
}

.toc-section {
    flex: 1;
    min-height: 0;
}

.compact-mode .toc-section {
    width: 100%;
}

.alt-toc-section {
    border-top: 1px solid var(--border-color);
}

.search-bar {
    gap: 8px;
    padding: 6px 12px;
    flex-shrink: 0;
}

.compact-mode .search-bar {
    padding: 4px 8px;
    position: absolute;
    bottom: 0;
    left: 0;
    right: 0;
    background-color: var(--bg-secondary);
    border-top: 1px solid var(--border-color);
    z-index: 10;
}

.search-input-container {
    position: relative;
    display: flex;
    align-items: center;
    min-width: 0;
}

.search-input-field {
    padding-right: 32px;
    width: 100%;
}

.compact-mode .search-input-field {
    width: 100%;
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

:deep(.tree-node.highlight-current) {
    background-color: color-mix(in srgb, var(--accent-color) 12%, transparent) !important;
}
</style>
