<script setup lang="ts">
import type { DictionarySection } from './useDictionary'

defineProps<{
  sections: DictionarySection[]
  loading: boolean
}>()

const emit = defineEmits<{ open: [bookId: number, title: string] }>()
</script>

<template>
  <div class="shelf">
    <div v-if="loading" class="shelf-loading">טוען...</div>
    <template v-else>
      <div v-for="section in sections" :key="section.title" class="shelf-section">
        <div class="shelf-section-title">{{ section.title }}</div>
        <button
          v-for="book in section.books"
          :key="book.id"
          class="shelf-book-row"
          @click="emit('open', book.id, book.title)"
        >
          <div class="shelf-book-info">
            <span class="shelf-book-title">{{ book.title }}</span>
            <span v-if="book.authors" class="shelf-book-author">{{ book.authors }}</span>
          </div>
        </button>
      </div>
    </template>
  </div>
</template>

<style scoped>
.shelf {
  padding: 4px 0 12px;
}

.shelf-loading {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 60px;
  font-size: 12px;
  color: var(--text-secondary);
}

.shelf-section {
  margin-bottom: 4px;
}

.shelf-section-title {
  height: 28px;
  display: flex;
  align-items: center;
  padding: 0 10px;
  font-size: 10px;
  font-weight: 600;
  color: var(--text-secondary);
  letter-spacing: 0.03em;
  user-select: none;
}

.shelf-book-row {
  display: flex;
  align-items: center;
  width: 100%;
  min-height: 52px;
  padding: 6px 16px;
  background: none;
  border: none;
  border-bottom: 1px solid var(--border-color);
  border-radius: 0;
  cursor: pointer;
  text-align: right;
}

.shelf-book-row:hover {
  background: color-mix(in srgb, var(--text-primary) 5%, transparent);
}

.shelf-book-row:active {
  background: color-mix(in srgb, var(--text-primary) 9%, transparent);
}

.shelf-book-info {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 1px;
  width: 100%;
}

.shelf-book-title {
  font-size: 14px;
  color: var(--text-primary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  max-width: 100%;
}

.shelf-book-author {
  font-size: 12px;
  color: var(--text-secondary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  max-width: 100%;
}
</style>
