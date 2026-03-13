<template>
    <div class="results-container"
         :style="containerStyles">
        <!-- Empty state - no search -->
        <div v-if="!hasSearched"
             class="empty-state flex-column flex-center">
            <Icon icon="fluent:search-sparkle-24-filled"
                  width="64"
                  height="64"
                  class="empty-icon" />
        </div>

        <!-- No results after search completed -->
        <div v-else-if="results.length === 0 && !isSearching"
             class="empty-state flex-column flex-center">
            <Icon icon="fluent:search-sparkle-24-filled"
                  width="64"
                  height="64"
                  class="empty-icon" />
            <div class="empty-message">לא נמצאו תוצאות</div>
        </div>

        <!-- Results list with Virtua virtualization -->
        <VList v-else
               ref="vListRef"
               class="scroll-container"
               :data="results"
               style="height: 100%; overflow-y: auto;"
               @scroll="handleScroll">
            <template #default="{ item, index }">
                <div :data-result-index="index"
                     class="result-item">
                    <div class="result-header"
                         @click="$emit('resultClick', item)">
                        <span class="book-title">{{ item.bookTitle }}</span>
                        <span v-if="item.tocText"
                              class="toc-separator">›</span>
                        <span v-if="item.tocText"
                              class="toc-text">{{ item.tocText }}</span>
                    </div>
                    <div class="result-snippet"
                         v-html="highlightedSnippet(item.snippet)"></div>
                </div>
            </template>
        </VList>
    </div>
</template>

<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import { VList } from 'virtua/vue'
import { Icon } from '@iconify/vue'
import type { BloomSearchResult } from '@/data/types/BloomSearch'
import { useSearchResultsList } from './useSearchResultsList'

const props = defineProps<{
    results: BloomSearchResult[]
    searchQuery: string
    isSearching: boolean
    hasSearched: boolean
}>()

defineEmits<{
    resultClick: [result: BloomSearchResult]
}>()

const vListRef = ref<InstanceType<typeof VList> | null>(null)

// Use composable for business logic
const {
    containerStyles,
    highlightedSnippet,
    saveScrollPosition,
    restoreScrollPosition,
    handleScroll
} = useSearchResultsList(
    () => props.searchQuery,
    vListRef
)

// Setup scroll listener and restore position
onMounted(async () => {
    await restoreScrollPosition()
})

onUnmounted(() => {
    saveScrollPosition()
})

// Expose for parent
defineExpose({
    vListRef
})
</script>

<style scoped>
.results-container {
    overflow: hidden;
    position: relative;
    flex: 1 1 0;
}

.scroll-container {
    height: 100%;
    overflow-y: auto;
    overflow-x: hidden;
    outline: none;
}

.empty-state {
    height: 100%;
    padding: 2rem;
    gap: 1rem;
    justify-content: center;
    align-items: center;
}

.empty-icon {
    color: var(--color-text-tertiary);
    opacity: 0.3;
}

.empty-message {
    font-size: 1.1rem;
    color: var(--color-text-secondary);
}

.result-item {
    padding: 8px 16px;
    border-bottom: 1px solid var(--color-border);
}

.result-header {
    display: flex;
    align-items: center;
    gap: 6px;
    margin-bottom: 6px;
    font-family: var(--header-font);
    font-weight: 500;
    cursor: pointer;
    padding: 4px 0;
    width: fit-content;
    position: relative;
    text-decoration: none;
    transition: opacity 0.15s ease;
}

.result-header:hover {
    opacity: 0.7;
}

.result-header::after {
    content: '';
    position: absolute;
    bottom: 0;
    right: -1%;
    left: -1%;
    height: 1px;
    background-color: currentColor;
    opacity: 0.2;
}

.result-header:hover .book-title {
    opacity: 1;
}

.book-title {
    color: var(--accent-color);
}

.toc-separator {
    color: var(--color-text-tertiary);
    font-size: 0.9rem;
}

.toc-text {
    color: var(--text-secondary);
    font-size: 0.9rem;
}

.result-snippet {
    font-family: var(--text-font);
    font-size: var(--font-size, 100%);
    line-height: var(--line-height, 1.5);
    color: var(--color-text-secondary);
    direction: rtl;
    text-align: justify;
    cursor: text;
    user-select: text;
}

.result-snippet :deep(.search-match) {
    color: var(--accent-color);
    font-weight: 600;
}
</style>
