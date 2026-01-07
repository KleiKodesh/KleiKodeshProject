<template>
    <div v-if="filteredEntries.length === 0"
         class="flex-center height-fill">
        <Icon icon="fluent:book-open-28-regular" />
        <span class="text-secondary">לא נמצאו תוצאות</span>
    </div>
    <div v-else
         ref="containerRef"
         class="flex-column overflow-y"
         @keydown="navigator?.handleKeyDown">

        <div v-for="entry in filteredEntries"
             :key="entry.id"
             class="flex-row hover-bg focus-accent click-effect c-pointer tree-node"
             tabindex="0"
             :style="{ paddingInlineStart: `${20}px` }"
             @click="selectEntry(entry)"
             @keydown.enter.prevent="selectEntry(entry)">
            <div class="flex-column flex-110 smaller-rem">
                <span class="bold">{{ entry.text }}</span>
                <span v-if="entry.path"
                      class="text-secondary smaller-em">{{ entry.path }}</span>
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { computed, ref, onMounted, onUnmounted, watch } from 'vue'
import type { TocEntry } from '../types/BookToc'
import { Icon } from '@iconify/vue'
import { KeyboardNavigator } from '../utils/KeyboardNavigator'

const props = defineProps<{
    tocEntries: TocEntry[]
    searchQuery: string
}>()

const emit = defineEmits<{
    selectLine: [lineIndex: number]
}>()

const containerRef = ref<HTMLElement>()
const navigator = ref<KeyboardNavigator>()
const debouncedQuery = ref('')
let debounceTimeout: number | null = null

// Debounce the search query
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

    // Remove quotation marks from search query
    const cleanQuery = debouncedQuery.value.replace(/"/g, '')
    const searchWords = cleanQuery.trim().toLowerCase().split(/\s+/)
    const results: TocEntry[] = []

    // Flatten all entries including nested children
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
        // Remove quotation marks from search text
        const searchText = `${entry.path || ''} ${entry.text}`.replace(/"/g, '').toLowerCase()
        if (searchWords.every(word => {
            // Use word boundaries that include Hebrew characters
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

onMounted(() => {
    if (containerRef.value) {
        navigator.value = new KeyboardNavigator(containerRef.value)
    }
})

onUnmounted(() => {
    navigator.value?.destroy()
    if (debounceTimeout) {
        clearTimeout(debounceTimeout)
    }
})

// Reinitialize navigator when search results change
watch(filteredEntries, () => {
    if (containerRef.value && navigator.value) {
        navigator.value.destroy()
        navigator.value = new KeyboardNavigator(containerRef.value)
    }
})
</script>
