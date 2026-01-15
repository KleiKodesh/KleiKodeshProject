<template>
    <span v-if="inlineMode"
          dir="rtl"
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
        <span v-html="content + ' '"></span>
    </span>
    <div v-else
         dir="rtl"
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
import type { AltTocLineEntry } from '../data/tocBuilder'

const props = defineProps<{
    content: string
    lineIndex: number
    isSelected: boolean
    inlineMode: boolean
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
    font-family: var(--text-font);
    line-height: var(--line-height, 1.2);
}

/* Block mode (div) gets padding and block display */
div.book-line {
    padding: 0px 5px;
    display: block;
}

/* Inline mode (span) is inline by default */
span.book-line {
    display: inline;
}

.book-line :deep(h1),
.book-line :deep(h2),
.book-line :deep(h3),
.book-line :deep(h4),
.book-line :deep(h5),
.book-line :deep(h6) {
    font-family: var(--header-font);
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

.book-line :deep(h1) {
    position: relative;
}

/* Block mode selection - only for div elements when split pane is open */
div.book-line.selected.show-selection {
    position: relative;
}

/* Commented out - using background instead of ::after indicator
div.book-line.selected.show-selection::after {
    content: '';
    position: absolute;
    top: 50%;
    transform: translateY(-50%);
    right: -7px;
    width: 3px;
    height: 1em;
    background-color: var(--accent-color);
} */

.book-line.selected.show-selection {
    background-color: var(--hover-bg);
}

/* Alt TOC entries - subtle opacity to distinguish from main content */
.alt-toc-entry {
    opacity: 0.7;
}
</style>
