<script setup lang="ts">
import { ref, computed } from 'vue'
import DayRow from './DayRow.vue'
import type { useWeeklyView } from './useWeeklyView'

const props = defineProps<{
  weekly: ReturnType<typeof useWeeklyView>
}>()

const openZmanim = ref<number | null>(null)
const openLearning = ref<number | null>(null)

function toggleZmanim(dow: number) {
  openLearning.value = null
  openZmanim.value = openZmanim.value === dow ? null : dow
}
function toggleLearning(dow: number) {
  openZmanim.value = null
  openLearning.value = openLearning.value === dow ? null : dow
}

const expanded = computed(() => openZmanim.value !== null || openLearning.value !== null)
</script>

<template>
  <div class="weekly" :class="{ expanded }">
    <DayRow
      v-for="day in weekly.week.value.days"
      :key="day.dayOfWeek"
      :day="day"
      :zmanim-open="openZmanim === day.dayOfWeek"
      :learning-open="openLearning === day.dayOfWeek"
      @toggle-zmanim="toggleZmanim(day.dayOfWeek)"
      @toggle-learning="toggleLearning(day.dayOfWeek)"
    />
  </div>
</template>

<style scoped>
.weekly {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

/* Normal: 7 rows share equal height */
.weekly :deep(.day-row) {
  flex: 1;
  min-height: 0;
}

/* Expanded: rows take natural height, list scrolls */
.weekly.expanded {
  overflow-y: auto;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
}
.weekly.expanded :deep(.day-row) {
  flex: none;
  min-height: 44px;
}
</style>
