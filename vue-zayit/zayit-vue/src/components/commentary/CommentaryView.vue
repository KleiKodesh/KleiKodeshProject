<template>
    <div class="flex-column height-fill commentary-container">
        <CommentaryContent
            :book-id="props.bookId"
            :selected-line-index="props.selectedLineIndex"
            :connection-type-id="selectedConnectionTypeId"
        />
    </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import CommentaryContent from './CommentaryContent.vue'
import { useTabStore } from '@/data/stores/tabStore'
import { useConnectionTypesStore } from '@/data/stores/connectionTypesStore'
import type { Book } from '@/data/types/Book'
import type { TocEntry } from '@/data/types/BookToc'

const props = withDefaults(defineProps<{
    bookId?: number
    selectedLineIndex?: number
    book?: Book
    flatTocEntries?: TocEntry[]
}>(), {
    bookId: undefined,
    selectedLineIndex: undefined,
    book: undefined,
    flatTocEntries: () => []
})

const emit = defineEmits<{
    (e: 'clearOtherSelections'): void
    (e: 'navigate-line', newIndex: number, tocEntryId?: number): void
}>()

const tabStore = useTabStore()
const connectionTypesStore = useConnectionTypesStore()

// Get selected connection type from tab state
const selectedConnectionTypeId = computed(() => {
    const activeTab = tabStore.activeTab
    return activeTab?.bookState?.commentaryFilterConnectionTypeId
})
</script>

<style scoped>
.commentary-container {
    position: relative;
    overflow: hidden;
}
</style>
