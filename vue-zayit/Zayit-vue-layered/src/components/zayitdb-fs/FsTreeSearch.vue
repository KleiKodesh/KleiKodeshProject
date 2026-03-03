<template>
    <div v-if="filteredBooks.length === 0"
         class="flex-center height-fill">
        <Icon icon="fluent:book-open-24-regular" />
        <span class="text-secondary">לא נמצאו תוצאות</span>
    </div>
    <div v-else
         ref="containerRef"
         class="search-results-container">
        <div v-for="book in filteredBooks"
             :key="book.id"
             class="search-result flex-row hover-bg focus-accent click-effect c-pointer reactive-icon"
             tabindex="0"
             @click="selectBook(book)"
             @keydown.enter.prevent="selectBook(book)">
            <div class="flex-column flex-110 smaller-rem">
                <span class="bold">{{ book.title }}</span>
                <span v-if="book.path"
                      class="text-secondary smaller-em">{{ book.path }}</span>
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import type { Book } from '@/data/types/Book'
import { hasConnections } from '@/data/types/Book'
import { Icon } from '@iconify/vue'
import { useListKeyboardNavigation } from '@/components/shared/useListKeyboardNavigation'
import { useBookViewer } from '@/components/book/useBookViewer'

const props = defineProps<{
    books: Book[]
    searchQuery: string
}>()

const emit = defineEmits<{
    returnFocus: []
}>()

const containerRef = ref<HTMLElement>()
const { openBookToc } = useBookViewer()
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

const filteredBooks = computed(() => {
    if (!debouncedQuery.value.trim()) {
        return []
    }

    const searchWords = debouncedQuery.value.trim().toLowerCase().split(/\s+/)
    const results: Book[] = []

    for (const book of props.books) {
        const searchText = `${book.path || ''} ${book.title}`.toLowerCase()
        if (searchWords.every(word => searchText.includes(word))) {
            results.push(book)
            if (results.length === 250) {
                break
            }
        }
    }

    return results
})

const selectBook = (book: Book) => {
    openBookToc(book.title, book.id, hasConnections(book))
}
</script>

<style scoped>
.search-results-container {
    display: flex;
    flex-direction: column;
    overflow-y: auto;
    height: 100%;
}

.search-result {
    display: flex !important;
    flex-direction: row !important;
    align-items: flex-start !important;
    gap: 12px;
    padding: 8px 12px;
    min-height: 44px;
    touch-action: manipulation;
    flex-shrink: 0;
}

.search-result .flex-column {
    word-break: break-word;
    overflow-wrap: break-word;
    flex: 1 1 auto;
}
</style>
