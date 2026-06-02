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
  (props.book.categories || '')
    .split(';')
    .map((t: string) => t.trim())
    .filter(Boolean),
)

const tooltip = computed(() => {
  const lines: string[] = []
  if (props.book.title) lines.push(props.book.title)
  if (props.book.author) lines.push(props.book.author)
  if (tags.value.length) lines.push(tags.value.join(' • '))
  if (props.book.pages) lines.push(`📄 ${props.book.pages}`)
  if (props.book.printingYear) lines.push(`📅 ${props.book.printingYear}`)
  if (props.book.printingPlace) lines.push(`📍 ${props.book.printingPlace}`)
  return lines.join('\n')
})
</script>

<template>
  <div
    class="item"
    data-nav-item
    :class="{ 'is-focused': focused }"
    :title="tooltip"
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
  flex-wrap: wrap;
  align-items: baseline;
  gap: 2px 6px;
  min-width: 0;
}
.title {
  font-size: 13px;
  font-weight: 600;
  color: var(--text-primary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  flex-shrink: 0;
  max-width: 100%;
}
.author {
  font-size: 11px;
  color: var(--text-secondary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  flex-shrink: 1;
  min-width: 0;
  max-width: 100%;
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
