<template>
    <div class="hover-bg focus-accent click-effect c-pointer tree-node"
         @click="handleBookClick(book)"
         tabindex="0">
        <!-- Header: Title, Author, and Download Button -->
        <div class="flex-between book-header">
            <div class="flex-row title-author-line">
                <h3 class="book-title ellipsis"
                    :title="book.title">{{ book.title }}</h3>
                <span v-if="book.author"
                      class="book-author text-secondary smaller-em">{{ book.author }}</span>
            </div>
            <button class="flex-center c-pointer download-btn hover-bg reactive-icon"
                    :title="'הורד את ' + book.title"
                    @click.stop="trackDownload(book)">
                <Icon icon="fluent:arrow-download-20-regular" />
            </button>
        </div>

        <!-- Details: Tags on left (RTL), Other info on right (RTL) -->
        <div class="flex-between book-details">
            <!-- Left side in RTL (visually right): Dynamic Tags -->
            <div v-if="getBookTags(book).length > 0"
                 class="tags-section ellipsis">
                <span class="detail-icon">🏷️</span>
                <div class="tags-container ellipsis">
                    <span v-for="tag in getBookTags(book)"
                          :key="tag"
                          class="tag">{{ tag }}</span>
                </div>
            </div>

            <!-- Right side in RTL (visually left): Pages, Year, Place -->
            <div class="detail-items ellipsis">
                <span v-if="book.pages"
                      class="detail-item">
                    <span class="detail-icon">📄</span>
                    {{ book.pages }} עמודים
                </span>

                <span v-if="book.printingYear"
                      class="detail-item">
                    <span class="detail-icon">📅</span>
                    {{ book.printingYear }}
                </span>

                <span v-if="book.printingPlace"
                      class="detail-item">
                    <span class="detail-icon">📍</span>
                    {{ book.printingPlace }}
                </span>
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import type { HebrewBook } from '../types/HebrewBook'
import { useHebrewBooksStore } from '../stores/hebrewBooksStore'
import { Icon } from '@iconify/vue'
import { parseTagsFromCsv } from '../utils/hebrewBookTags'

defineProps<{
    book: HebrewBook
}>()

const emit = defineEmits<{
    bookClicked: [book: HebrewBook]
}>()

const store = useHebrewBooksStore()

const handleBookClick = (book: HebrewBook) => {
    emit('bookClicked', book)
    // Navigate to PDF view with Hebrew book
    store.openHebrewBookViewer(book.id, book.title)
}

const trackDownload = async (book: HebrewBook) => {
    // Track the interaction
    await store.trackBookInteraction(book.id)

    // Trigger download with save dialog
    store.downloadHebrewBook(book.id, book.title)
}

const getBookTags = (book: HebrewBook): string[] => {
    return parseTagsFromCsv(book._csvTags || '')
}
</script>

<style scoped>
.book-header {
    margin-bottom: 4px;
    gap: 12px;
    align-items: flex-start;
}

.title-author-line {
    gap: 8px;
    flex: 1;
    min-width: 0;
    flex-wrap: wrap;
    align-items: baseline;
}

.book-title {
    font-size: 15px;
    font-weight: 600;
    color: var(--text-primary);
    line-height: 1.4;
    margin: 0;
    text-align: right;
    flex-shrink: 1;
    min-width: 0;
}

.book-author {
    font-weight: 500;
    text-align: right;
    flex-shrink: 0;
}

.download-btn {
    width: 24px;
    height: 24px;
    border-radius: 2px;
    padding: 4px;
    transition: background-color 0.15s ease;
    flex-shrink: 0;
}

.download-btn svg {
    width: 16px;
    height: 16px;
}

.book-details {
    gap: 12px;
    align-items: center;
    height: 20px;
    /* Fixed height for consistency */
}

.detail-items {
    display: flex;
    gap: 12px;
    align-items: center;
    min-width: 0;
    flex-shrink: 1;
    font-size: 12px;
    color: var(--text-secondary);
}

.tags-section {
    display: flex;
    gap: 4px;
    align-items: center;
    min-width: 0;
    flex-shrink: 1;
    font-size: 12px;
}

.tags-container {
    display: flex;
    gap: 6px;
    align-items: center;
    min-width: 0;
    overflow: hidden;
    white-space: nowrap;
    text-overflow: ellipsis;
}

.detail-item {
    display: inline-flex;
    gap: 4px;
    align-items: center;
    flex-shrink: 0;
    white-space: nowrap;
    font-size: 12px;
    color: var(--text-secondary);
}

.detail-icon {
    font-size: 11px;
    opacity: 0.7;
    flex-shrink: 0;
}

.tag {
    color: var(--text-secondary);
    font-size: 12px;
    font-weight: 500;
    flex-shrink: 0;
}

.tag:not(:last-child)::after {
    content: ' •';
    margin-left: 4px;
    opacity: 0.5;
}
</style>
