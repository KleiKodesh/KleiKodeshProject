<script setup lang="ts">
import { ref, computed } from 'vue'
import type { City } from './useWeeklyCalendar'
import type { useWeeklyCalendar } from './useWeeklyCalendar'
import WeeklyDayRow from './WeeklyDayRow.vue'

const props = defineProps<{
  city: City
  cities: City[]
  weekly: ReturnType<typeof useWeeklyCalendar>
}>()
defineEmits<{ (e: 'city-change', city: City): void }>()

const { week } = props.weekly

const expandedDay = ref<number | null>(null)
const expandedLearning = ref<number | null>(null)

function toggleDay(dow: number) {
  expandedDay.value = expandedDay.value === dow ? null : dow
}
function toggleLearning(dow: number) {
  expandedLearning.value = expandedLearning.value === dow ? null : dow
}

const hasExpanded = computed(() => expandedDay.value !== null || expandedLearning.value !== null)
</script>

<template>
  <div class="wrap">
    <div class="list" :class="{ scrollable: hasExpanded }">
      <WeeklyDayRow
        v-for="day in week.days"
        :key="day.dayOfWeek"
        :day="day"
        :zmanim-open="expandedDay === day.dayOfWeek"
        :learning-open="expandedLearning === day.dayOfWeek"
        @toggle-zmanim="toggleDay(day.dayOfWeek)"
        @toggle-learning="toggleLearning(day.dayOfWeek)"
      />
    </div>
  </div>
</template>

<style scoped>
.wrap {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

/* Normal: 7 tiles share equal height, no scroll */
.list {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}
.list :deep(.day-tile) {
  flex: 1;
  min-height: 0;
}

/* Expanded: tiles take natural height, list scrolls */
.list.scrollable {
  overflow-y: auto;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
}
.list.scrollable :deep(.day-tile) {
  flex: none;
  min-height: 44px;
}
</style>
