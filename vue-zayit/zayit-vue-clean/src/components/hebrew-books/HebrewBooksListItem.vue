<script setup lang="ts">
import { computed } from 'vue'
import { IconArrowDownload20Regular } from '@iconify-prerendered/vue-fluent'
import type { HebrewBook } from './hebrewBooksService'

const props = defineProps<{ book: HebrewBook; focused?: boolean }>()
const emit = defineEmits<{
  'book-clicked': [book: HebrewBook]
  'download-clicked': [book: HebrewBook]
}>()

const tags = computed(() =>
  (props.book._csvTags || '').split(';').map(t => t.trim()).filter(Boolean)
)
</script>

<template>
  <div class="item" data-nav-item :class="{ 'is-focused': focused }" @click="emit('book-clicked', book)">
    <div class="row-top">
      <div class="title-line">
        <span class="title">{{ book.title }}</span>
        <span v-if="book.author" class="author">{{ book.author }}</span>
      </div>
      <button class="dl-btn" :title="'הורד ' + book.title" tabindex="-1" @click.stop="emit('download-clicked', book)">
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
  padding: 10px 16px;
  cursor: pointer;
  border-bottom: 1px solid var(--border-color);
  content-visibility: auto;
  contain-intrinsic-size: auto 64px;
}
.item:hover { background: var(--bg-secondary); }

.row-top {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 8px;
  margin-bottom: 4px;
}
.title-line {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 6px;
  min-width: 0;
}
.title {
  font-size: 14px;
  font-weight: 600;
  color: var(--text-primary);
  direction: rtl;
}
.author {
  font-size: 12px;
  color: var(--text-secondary);
}
.dl-btn {
  flex-shrink: 0;
  width: 28px;
  height: 28px;
  padding: 4px;
  background: none;
  border: none;
  border-radius: 6px;
  cursor: pointer;
  color: var(--text-secondary);
  display: flex;
  align-items: center;
  justify-content: center;
}
.dl-btn:hover { background: color-mix(in srgb, var(--text-primary) 10%, transparent); color: var(--text-primary); }
.dl-btn:active { background: color-mix(in srgb, var(--text-primary) 16%, transparent); color: var(--text-primary); transform: scale(0.92); }

.row-bottom {
  display: flex;
  justify-content: space-between;
  align-items: center;
  font-size: 11px;
  color: var(--text-secondary);
  gap: 8px;
}
.tags { display: flex; gap: 6px; flex-wrap: nowrap; overflow: hidden; }
.tag { white-space: nowrap; }
.tag:not(:last-child)::after { content: ' •'; margin-left: 4px; opacity: 0.5; }
.meta { display: flex; gap: 10px; flex-shrink: 0; }
.meta span { white-space: nowrap; }
</style>
