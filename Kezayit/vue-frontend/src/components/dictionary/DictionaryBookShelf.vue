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
        <div class="shelf-cards">
          <button
            v-for="book in section.books"
            :key="book.id"
            class="shelf-card"
            @click="emit('open', book.id, book.title)"
          >
            <span class="shelf-card-title">{{ book.title }}</span>
            <span v-if="book.authors" class="shelf-card-author">{{ book.authors }}</span>
          </button>
        </div>
      </div>
    </template>
  </div>
</template>

<style scoped>
.shelf {
  padding: 8px 10px 16px;
  direction: rtl;
}

.shelf-loading {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 60px;
  font-size: 13px;
  color: var(--text-secondary);
}

.shelf-section {
  margin-bottom: 12px;
}

.shelf-section-title {
  font-size: 11px;
  font-weight: 600;
  color: var(--text-secondary);
  letter-spacing: 0.04em;
  padding-bottom: 4px;
  border-bottom: 1px solid var(--border-color);
  margin-bottom: 4px;
}

.shelf-cards {
  display: flex;
  flex-direction: column;
  gap: 3px;
}

.shelf-card {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  width: 100%;
  padding: 6px 10px;
  background: var(--bg-secondary);
  border: none;
  border-radius: 6px;
  cursor: pointer;
  direction: rtl;
  gap: 1px;
}

.shelf-card:hover {
  background: color-mix(in srgb, var(--accent-color) 8%, var(--bg-secondary));
}

.shelf-card:active {
  background: color-mix(in srgb, var(--accent-color) 14%, var(--bg-secondary));
}

.shelf-card-title {
  font-size: 13px;
  font-weight: 600;
  color: var(--text-primary);
}

.shelf-card-author {
  font-size: 11px;
  color: var(--text-secondary);
}
</style>
