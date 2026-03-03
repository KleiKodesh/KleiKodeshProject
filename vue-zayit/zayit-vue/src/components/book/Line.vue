<template>
    <div dir="rtl"
         class="selectable line-1.6 justify book-line"
         :class="{ selected: isSelected }"
         :data-line-index="lineIndex"
         @click="handleClick">
        <!-- Alt TOC entries as proper HTML headings -->
        <template v-if="altTocEntries && altTocEntries.length > 0 && props.showAltToc !== false">
            <component v-for="(entry, index) in altTocEntries"
                       :key="`alt-toc-${lineIndex}-${index}`"
                       :is="getHeadingTag(entry.level)"
                       class="alt-toc-entry"
                       v-html="entry.text">
            </component>
        </template>
        <div v-html="content + ' '"></div>
    </div>
</template>

<script setup lang="ts">
import type { AltTocLineEntry } from '@/data/services/bookTocService'

const props = defineProps<{
    content: string
    lineIndex: number
    isSelected: boolean
    altTocEntries?: AltTocLineEntry[]
    showAltToc?: boolean
}>()

const emit = defineEmits<{
    lineClick: [lineIndex: number]
}>()

const handleClick = () => {
    emit('lineClick', props.lineIndex)
}

// Map TOC level to appropriate HTML heading tag with offset of 1
const getHeadingTag = (level: number): string => {
    // Add 1 to level so: level 1 → h2, level 2 → h3, etc.
    // Clamp to valid heading range (h2-h6, since h1 is reserved)
    const headingLevel = Math.max(2, Math.min(6, level + 2))
    return `h${headingLevel}`
}
</script>

<style scoped>
.book-line {
    padding: 0px 5px;
    font-family: var(--text-font);
    line-height: var(--line-height, 1.2);
}

.book-line :deep(h1),
.book-line :deep(h2),
.book-line :deep(h3),
.book-line :deep(h4),
.book-line :deep(h5),
.book-line :deep(h6) {
    font-family: var(--header-font);
}

.book-line :deep(h1) {
  position: relative;
}

.book-line :deep(h1)::after {
    content: '';
    position: absolute;
    bottom: 0;
    left: 0;
    right: 0;
    height: 1px;
    background-color: var(--hover-bg)
}



/* Selection styling when split pane is open */
.book-line.selected.show-selection {
    position: relative;
    background-color: var(--hover-bg);
}

/* In-book search term highlighting - use background */
.book-line :deep(mark) {
    background-color: rgba(245, 158, 11, 0.3);
    color: inherit;
}

.book-line :deep(mark.current) {
    background-color: rgba(245, 158, 11, 0.8) !important;
    font-weight: bold;
}

:root.dark .book-line :deep(mark) {
    background-color: rgba(251, 191, 36, 0.3);
    color: inherit;
}

:root.dark .book-line :deep(mark.current) {
    background-color: rgba(251, 191, 36, 0.8) !important;
    font-weight: bold;
}

/* Alt TOC entries - subtle opacity to distinguish from main content */
.alt-toc-entry {
    opacity: 0.7;
}
</style>
