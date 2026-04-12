<script setup lang="ts">
import { computed } from 'vue'
import { IconOpen24Regular } from '@iconify-prerendered/vue-fluent'
import type { DictEntryContent } from './useDictionarySearch'

const props = defineProps<{ entry: DictEntryContent }>()
const emit = defineEmits<{ openInViewer: [] }>()

// ספר השרשים entries are 2 lines joined with \n — render as separate blocks
const html = computed(() => props.entry.html.replace(/\n/g, '<br><br>'))
</script>

<template>
  <div class="entry-view">
    <div class="entry-headword">{{ entry.headword }}</div>
    <div class="entry-content" dir="rtl" v-html="html" />
    <div class="entry-footer">
      <button class="entry-open-btn" @click="emit('openInViewer')">
        <IconOpen24Regular />
        <span>פתח בקורא</span>
      </button>
    </div>
  </div>
</template>

<style scoped>
.entry-view {
  display: flex;
  flex-direction: column;
  height: 100%;
}

.entry-headword {
  padding: 16px 16px 4px;
  font-size: 22px;
  font-weight: 700;
  color: var(--accent-color);
  direction: rtl;
  flex-shrink: 0;
}

.entry-content {
  flex: 1;
  padding: 20px 16px;
  font-size: 16px;
  line-height: 1.8;
  color: var(--text-primary);
  overflow-y: auto;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
}

.entry-content :deep(b) {
  font-weight: 700;
}

.entry-content :deep(big) {
  font-size: 1.25em;
  font-weight: 700;
  color: var(--accent-color);
}

.entry-content :deep(h3) {
  font-size: 1.3em;
  font-weight: 700;
  color: var(--accent-color);
  margin: 0 0 12px;
}

.entry-content :deep(small) {
  font-size: 0.82em;
  color: var(--text-secondary);
}

.entry-content :deep(span[dir='ltr']) {
  direction: ltr;
  display: inline-block;
  color: var(--text-secondary);
  font-style: italic;
  font-size: 0.9em;
}

.entry-footer {
  flex-shrink: 0;
  padding: 10px 16px;
  border-top: 1px solid var(--border-color);
  background: var(--bg-secondary);
}

.entry-open-btn {
  display: flex;
  align-items: center;
  gap: 6px;
  height: 40px;
  padding: 0 14px;
  border-radius: 4px;
  font-size: 13px;
  color: var(--text-secondary);
}

.entry-open-btn:hover {
  color: var(--text-primary);
  background: color-mix(in srgb, var(--text-primary) 6%, transparent);
}
</style>
