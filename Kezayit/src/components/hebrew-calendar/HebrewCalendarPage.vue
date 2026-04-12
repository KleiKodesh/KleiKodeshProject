<script setup lang="ts">
import { ref, onMounted } from 'vue'
import CalendarHeader from './CalendarHeader.vue'
import WeeklyCalendarList from './WeeklyCalendarList.vue'
import MonthlyCalendarGrid from './MonthlyCalendarGrid.vue'
import { useZmanim } from './useZmanim'
import { useHebrewCalendar } from './useHebrewCalendar'
import { useWeeklyCalendar } from './useWeeklyCalendar'

const { activeCity, cities, setCity, init: initZmanim } = useZmanim()

type ViewMode = 'weekly' | 'monthly'
const viewMode = ref<ViewMode>('weekly')

// Monthly composable — always alive so pickers work
const monthly = useHebrewCalendar()

// Weekly composable
const cityRef = {
  get value() {
    return activeCity.value
  },
}
const weekly = useWeeklyCalendar(cityRef)

function onPrev() {
  viewMode.value === 'weekly' ? weekly.prevWeek() : monthly.prevMonth()
}
function onNext() {
  viewMode.value === 'weekly' ? weekly.nextWeek() : monthly.nextMonth()
}
function onToday() {
  viewMode.value === 'weekly' ? weekly.goToToday() : monthly.goToToday()
}

onMounted(() => {
  // Reset to today's week/month on every visit — no stale navigation state
  viewMode.value = 'weekly'
  weekly.reset()
  monthly.reset()
  initZmanim()
})
</script>

<template>
  <div class="cal-page">
    <CalendarHeader
      :view-mode="viewMode"
      :heb-label="
        viewMode === 'weekly' ? weekly.week.value.hebrewRangeLabel : monthly.hebrewMonthLabel.value
      "
      :greg-label="
        viewMode === 'weekly' ? weekly.week.value.gregRangeLabel : monthly.gregorianMonthLabel.value
      "
      :display-month="monthly.displayMonth.value"
      :display-year="monthly.displayYear.value"
      :current-heb-month="monthly.currentHebMonth.value"
      :current-heb-year="monthly.currentHebYear.value"
      @prev="onPrev"
      @next="onNext"
      @today="onToday"
      @set-view="viewMode = $event"
      @select-heb-month="monthly.jumpToHebrew(monthly.currentHebYear.value, $event)"
      @select-heb-year="monthly.jumpToHebrew($event, monthly.currentHebMonth.value)"
      @select-greg-month="monthly.displayMonth.value = $event"
      @select-greg-year="monthly.displayYear.value = $event"
    />

    <WeeklyCalendarList
      v-if="viewMode === 'weekly'"
      :city="activeCity"
      :cities="cities"
      :weekly="weekly"
      @city-change="setCity"
    />
    <MonthlyCalendarGrid v-else :monthly="monthly" />
  </div>
</template>

<style scoped>
.cal-page {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
  box-sizing: border-box;
}
</style>
