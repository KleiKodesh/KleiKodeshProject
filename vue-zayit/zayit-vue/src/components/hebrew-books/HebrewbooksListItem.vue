<template>
    <div
        class="hover-bg focus-accent click-effect c-pointer tree-node"
        tabindex="0"
        @click="emit('book-clicked', book)"
        @keydown.enter="emit('book-clicked', book)"
    >
        <!-- Title row -->
        <div class="flex-between book-header">
            <div class="flex-row title-line">
                <span class="book-title ellipsis" :title="book.title">{{ book.title }}</span>
                <span v-if="book.author" class="text-secondary smaller-em book-author">{{ book.author }}</span>
            </div>
            <button
                class="flex-center c-pointer reactive-icon"
                :title="'הורד את ' + book.title"
                tabindex="-1"
                @click.stop="emit('download-clicked', book)"
            >
                <Icon icon="fluent:arrow-download-20-regular" />
            </button>
        </div>

        <!-- Details row -->
        <div class="flex-between book-details">
            <div v-if="tags.length > 0" class="tags-row ellipsis">
                <span class="detail-icon">🏷️</span>
                <span v-for="tag in tags" :key="tag" class="tag">{{ tag }}</span>
            </div>
            <div class="meta-row">
                <span v-if="book.pages" class="meta-item">
                    <span class="detail-icon">📄</span>{{ book.pages }} עמודים
                </span>
                <span v-if="book.printingYear" class="meta-item">
                    <span class="detail-icon">📅</span>{{ book.printingYear }}
                </span>
                <span v-if="book.printingPlace" class="meta-item">
                    <span class="detail-icon">📍</span>{{ book.printingPlace }}
                </span>
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { Icon } from '@iconify/vue'
import type { HebrewBook } from '@/data/types/HebrewBook'

const props = defineProps<{ book: HebrewBook }>()

const emit = defineEmits<{
    'book-clicked': [book: HebrewBook]
    'download-clicked': [book: HebrewBook]
}>()

const tags = computed(() =>
    (props.book._csvTags || '').split(';').map(t => t.trim()).filter(Boolean)
)
</script>

<style scoped>
.book-header {
    margin-bottom: 4px;
    gap: 12px;
    align-items: flex-start;
}

.title-line {
    flex: 1;
    min-width: 0;
    flex-wrap: wrap;
    align-items: baseline;
    gap: 8px;
}

.book-title {
    font-size: 15px;
    font-weight: 600;
    color: var(--text-primary);
    text-align: right;
    min-width: 0;
}

.book-author {
    font-weight: 500;
    flex-shrink: 0;
}

.book-details {
    gap: 12px;
    align-items: center;
    font-size: 12px;
    color: var(--text-secondary);
}

.tags-row {
    display: flex;
    gap: 4px;
    align-items: center;
    min-width: 0;
    overflow: hidden;
}

.tag:not(:last-child)::after {
    content: ' •';
    margin-left: 4px;
    opacity: 0.5;
}

.meta-row {
    display: flex;
    gap: 12px;
    align-items: center;
    flex-shrink: 0;
}

.meta-item {
    display: inline-flex;
    gap: 4px;
    align-items: center;
    white-space: nowrap;
}

.detail-icon {
    font-size: 11px;
    opacity: 0.7;
}

/* CSS virtualization */
.tree-node {
    content-visibility: auto;
    contain-intrinsic-size: auto 64px;
}
</style>
