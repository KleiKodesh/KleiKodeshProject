<script setup lang="ts">
import { inject, shallowRef, watch, type Ref } from 'vue'
import { wantsNewTab } from '@/composables/useOpenInNewTab'
import { DAILY_LEARNING_OPENER_KEY, useHomeDailyLearningEntries, type HomeDailyLearningEntry } from '@/features/home/useHomeDailyLearning'
import { computeHomeDailyLearningItems } from '@/features/home/homeDailyLearningItems'

// The standalone calendar has no library to look the books up in: it lists every cycle
// and hands the click to its opener (a link into the app). Inside the app, as before.
/** The day to list (the separate calendar app's picked day); today when absent. */
const props = defineProps<{ date?: Date | null }>()
const standaloneOpener = inject(DAILY_LEARNING_OPENER_KEY, null)
const entries: Ref<HomeDailyLearningEntry[]> = standaloneOpener ? shallowRef([]) : useHomeDailyLearningEntries()
if (standaloneOpener) {
  let generation = 0
  watch(() => (props.date ?? new Date()).toDateString(), () => {
    const day = props.date ?? new Date()
    const mine = ++generation
    void computeHomeDailyLearningItems(day).then((items) => {
      if (mine !== generation) return // a later day was picked meanwhile
      entries.value = items.map((item) => ({
        key: item.key, label: item.label, location: item.location,
        open: (openInNewTab: boolean) => standaloneOpener(day, item.key, openInNewTab),
      }))
    })
  }, { immediate: true })
}
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
.learning-widget { display: flex; flex-direction: column; padding: 6px; }
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
