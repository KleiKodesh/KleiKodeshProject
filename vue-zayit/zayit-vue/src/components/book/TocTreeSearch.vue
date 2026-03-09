<template>
    <div v-if="filteredEntries.length === 0"
         class="flex-center height-fill">
        <span class="text-secondary">לא נמצאו תוצאות</span>
    </div>
    <div v-else
         ref="containerRef"
         class="flex-column overflow-y">

        <div v-for="entry in filteredEntries"
             :key="entry.id"
             class="flex-row hover-bg focus-accent click-effect c-pointer tree-node"
             :class="{ 'compact': isCompactMode }"
             tabindex="0"
             :style="{ paddingInlineStart: `${isCompactMode ? 12 : 20}px` }"
             @click="selectEntry(entry)"
             @keydown.enter.prevent="selectEntry(entry)">
            <div class="flex-column flex-110 search-result-content">
                <span class="bold result-title"
                      :class="{ 'compact-text': isCompactMode }">{{ entry.text }}</span>
                <span v-if="entry.path"
                      class="text-secondary result-path"
                      :class="{ 'compact-path': isCompactMode }">{{ entry.path }}</span>
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import type { TocEntry } from '@/data/types/BookToc'
import { Icon } from '@iconify/vue'
import { useListKeyboardNavigation } from '@/components/shared/useListKeyboardNavigation'

const props = defineProps<{
    tocEntries: TocEntry[]
    searchQuery: string
    isCompactMode?: boolean
}>()

const emit = defineEmits<{
    selectLine: [lineIndex: number]
    returnFocus: []
}>()

const containerRef = ref<HTMLElement>()
const debouncedQuery = ref('')
let debounceTimeout: number | null = null

const { handleKeyDown } = useListKeyboardNavigation(containerRef, {
    onEscape: () => emit('returnFocus')
})

watch(() => props.searchQuery, (newValue) => {
    if (debounceTimeout) {
        clearTimeout(debounceTimeout)
    }

    debounceTimeout = window.setTimeout(() => {
        debouncedQuery.value = newValue
    }, 250)
}, { immediate: true })

const filteredEntries = computed(() => {
    if (!debouncedQuery.value.trim()) {
        return []
    }

    const cleanQuery = debouncedQuery.value.replace(/"/g, '')
    const searchWords = cleanQuery.trim().toLowerCase().split(/\s+/)
    const results: TocEntry[] = []

    const flattenEntries = (entries: TocEntry[]): TocEntry[] => {
        const flat: TocEntry[] = []
        for (const entry of entries) {
            flat.push(entry)
            if (entry.children && entry.children.length > 0) {
                flat.push(...flattenEntries(entry.children))
            }
        }
        return flat
    }

    const allEntries = flattenEntries(props.tocEntries)

    for (const entry of allEntries) {
        const searchText = `${entry.path || ''} ${entry.text}`.replace(/"/g, '').toLowerCase()
        if (searchWords.every(word => {
            const escapedWord = word.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
            const regex = new RegExp(`(?<![\\w\u0590-\u05FF])${escapedWord}(?![\\w\u0590-\u05FF])`, 'i')
            return regex.test(searchText)
        })) {
            results.push(entry)
            if (results.length === 100) {
                break
            }
        }
    }

    return results
})

const selectEntry = (entry: TocEntry) => {
    emit('selectLine', entry.lineIndex)
}
</script>

<style scoped>
.tree-node {
    padding: 12px 12px;
    gap: 4px;
}

.tree-node.compact {
    min-height: 40px;
    padding: 10px 12px;
}

.search-result-content {
    gap: 0;
}

.result-title {
    line-height: 1.3;
}

.result-path {
    font-size: 0.85em;
    line-height: 1.2;
}

.compact-text {
    font-size: 0.95em;
}

.compact-path {
    font-size: 0.8em;
}
</style>
