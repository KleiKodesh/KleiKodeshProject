<script setup lang="ts">
import { wantsNewTab } from '@/composables/useOpenInNewTab'
import { useHomeDailyLearningEntries } from '@/features/home/useHomeDailyLearning'

const entries = useHomeDailyLearningEntries()
</script>

<template>
  <div class="learning-widget">
    <button
      v-for="entry in entries"
      :key="entry.key"
      class="learning-row"
      :disabled="!entry.open"
      :title="entry.open ? undefined : $t('home.dateBar.bookNotInLibrary')"
      @click="entry.open?.(wantsNewTab($event))"
    >
      <span class="learning-label">{{ entry.label }}</span>
      <span class="learning-path">{{ entry.location }}</span>
    </button>
  </div>
</template>

<style scoped>
.learning-widget { display: flex; flex-direction: column; padding: 6px; overflow-y: auto; }
.learning-row {
  display: flex;
  justify-content: space-between;
  gap: 16px;
  padding: 6px 8px;
  background: none;
  border: none;
  border-radius: 4px;
  font-size: 13px;
  text-align: start;
  cursor: pointer;
  color: var(--text-primary);
}
.learning-row:hover:not(:disabled) { background: color-mix(in srgb, var(--text-primary) 6%, transparent); }
.learning-row:disabled { opacity: 0.45; cursor: default; }
.learning-label { font-weight: 600; }
.learning-path { color: var(--text-secondary); }
</style>
