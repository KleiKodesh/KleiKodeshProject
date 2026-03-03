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

        <!-- Results list with virtualization -->
        <DynamicScroller v-else
                         ref="scrollerRef"
                         :items="results"
                         :min-item-size="100"
                         key-field="lineId"
                         class="scroller">
            <template #default="{ item, index, active }">
                <DynamicScrollerItem :item="item"
                                     :active="active"
                                     :data-index="index"
                                     :size-dependencies="[item.snippet]">
                    <div class="result-item">
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
                </DynamicScrollerItem>
            </template>
        </DynamicScroller>
    </div>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { Icon } from '@iconify/vue'
import { DynamicScroller, DynamicScrollerItem } from 'vue-virtual-scroller'
import { useSettingsStore } from '@/data/stores/settingsStore'
import { censorDivineNames } from '@/utils/censorDivineNames'
import type { BloomSearchResult } from '@/data/types/BloomSearch'

const props = defineProps<{
    results: BloomSearchResult[]
    searchQuery: string
    isSearching: boolean
    hasSearched: boolean
}>()

defineEmits<{
    resultClick: [result: BloomSearchResult]
}>()

const settingsStore = useSettingsStore()
const scrollerRef = ref<InstanceType<typeof DynamicScroller> | null>(null)

const containerStyles = computed(() => ({
    backgroundColor: 'var(--reading-bg-primary)',
    color: 'var(--reading-text-primary)'
}))

/**
 * Highlight search terms in snippet
 */
const highlightedSnippet = (snippet: string): string => {
    if (!props.searchQuery || !snippet) {
        return snippet
    }

    // Apply censoring if enabled
    let processedSnippet = snippet
    if (settingsStore.censorDivineNames) {
        processedSnippet = censorDivineNames(processedSnippet)
    }

    const terms = props.searchQuery.trim().split(/\s+/)
    let highlighted = processedSnippet

    terms.forEach((term) => {
        if (term.length > 0) {
            const escapedTerm = term.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
            const regex = new RegExp(`(${escapedTerm})`, 'gi')
            highlighted = highlighted.replace(regex, '<mark>$1</mark>')
        }
    })

    return highlighted
}

// Expose scroller ref for parent
defineExpose({
    scrollerRef
})
</script>

<style scoped>
.results-container {
    overflow: hidden;
    position: relative;
    flex: 1 1 0;
}

.scroller {
    height: 100%;
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

.result-snippet :deep(mark) {
    background-color: transparent;
    color: var(--accent-color);
    font-weight: 600;
    padding: 0;
}
</style>
