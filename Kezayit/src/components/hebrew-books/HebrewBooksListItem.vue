<script setup lang="ts">
import { computed } from 'vue'
import { IconArrowDownload20Regular } from '@iconify-prerendered/vue-fluent'
import type { HebrewBook } from './hebrewBooksCatalog'

const props = defineProps<{ book: HebrewBook; focused?: boolean }>()
const emit = defineEmits<{
  'book-clicked': [book: HebrewBook]
  'download-clicked': [book: HebrewBook]
}>()

const tags = computed(() =>
  (props.book._csvTags || '')
    .split(';')
    .map((t: string) => t.trim())
    .filter(Boolean),
)
</script>

<template>
  <div
    class="item"
    data-nav-item
    :class="{ 'is-focused': focused }"
    @click="emit('book-clicked', book)"
  >
    <div class="row-top">
      <div class="title-line">
        <span class="title">{{ book.title }}</span>
        <span v-if="book.author" class="author">{{ book.author }}</span>
      </div>
      <button
        class="dl-btn"
        :title="'הורד ' + book.title"
        tabindex="-1"
        @click.stop="emit('download-clicked', book)"
      >
        <IconArrowDownload20Regular />
      </button>
    </div>
    <div class="row-bottom">
      <div v-if="tags.length" class="tags">
        <span v-for="tag in tags" :key="tag" class="tag">{{ tag }}</span>
      </div>
      <div class="meta">
        <span v-if="book.pages">📄 {{ book.pages }}</span>
        <span v-if="book.printingYear">📅 {{ book.printingYear }}</span>
        <span v-if="book.printingPlace">📍 {{ book.printingPlace }}</span>
      </div>
    </div>
  </div>
</template>

<style scoped>
.item {
  padding: 6px 12px;
  cursor: pointer;
  border-bottom: 1px solid var(--border-color);
}
.item:hover {
  background: color-mix(in srgb, var(--text-primary) 6%, transparent);
}
.item:active {
  background: color-mix(in srgb, var(--text-primary) 10%, transparent);
}

.row-top {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 6px;
  margin-bottom: 2px;
}
.title-line {
  display: flex;
  flex-wrap: nowrap;
  align-items: baseline;
  gap: 6px;
  min-width: 0;
  overflow: hidden;
}
.title {
  font-size: 13px;
  font-weight: 600;
  color: var(--text-primary);
  direction: rtl;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.author {
  font-size: 11px;
  color: var(--text-secondary);
  white-space: nowrap;
  flex-shrink: 0;
}
.dl-btn {
  flex-shrink: 0;
  width: 24px;
  height: 24px;
  padding: 2px;
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--text-secondary);
}
.dl-btn:hover {
  color: var(--text-primary);
}

.row-bottom {
  display: flex;
  justify-content: space-between;
  align-items: center;
  font-size: 11px;
  color: var(--text-secondary);
  gap: 8px;
}
.tags {
  display: flex;
  gap: 4px;
  flex-wrap: nowrap;
  overflow: hidden;
}
.tag {
  white-space: nowrap;
}
.tag:not(:last-child)::after {
  content: ' •';
  margin-inline-start: 2px;
  opacity: 0.5;
}
.meta {
  display: flex;
  gap: 8px;
  flex-shrink: 0;
}
.meta span {
  white-space: nowrap;
}
</style>
