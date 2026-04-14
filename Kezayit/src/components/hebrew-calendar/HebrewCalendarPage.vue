<script setup lang="ts">
import { ref, watch, onMounted } from 'vue'
import { idbGet, idbSet, KEYS } from '@/utils/idbPersistence'
import { useZmanim } from './useZmanim'
import { useWeeklyView } from './useWeeklyView'
import { useMonthlyView } from './useMonthlyView'
import CalendarHeader from './CalendarHeader.vue'
import WeeklyView from './WeeklyView.vue'
import MonthlyView from './MonthlyView.vue'

type ViewMode = 'weekly' | 'monthly'

const viewMode = ref<ViewMode>('weekly')
watch(viewMode, (v) => idbSet(KEYS.SETTINGS_CALENDAR_VIEW, v))

const { activeCity, init: initZmanim } = useZmanim()

const cityRef = {
  get value() {
    return activeCity.value
  },
}
const weekly = useWeeklyView(cityRef)
const monthly = useMonthlyView()

function onPrev() {
  viewMode.value === 'weekly' ? weekly.prev() : monthly.prevMonth()
}
function onNext() {
  viewMode.value === 'weekly' ? weekly.next() : monthly.nextMonth()
}
function onToday() {
  viewMode.value === 'weekly' ? weekly.goToday() : monthly.goToday()
}

onMounted(async () => {
  const saved = await idbGet<ViewMode>(KEYS.SETTINGS_CALENDAR_VIEW)
  viewMode.value = saved === 'monthly' ? 'monthly' : 'weekly'
  weekly.reset()
  monthly.reset()
  initZmanim()
})
</script>

<template>
  <div class="page">
    <CalendarHeader
      :view-mode="viewMode"
      :hebrew-label="
        viewMode === 'weekly' ? weekly.week.value.hebrewLabel : monthly.hebrewLabel.value
      "
      :greg-label="viewMode === 'weekly' ? weekly.week.value.gregLabel : monthly.gregLabel.value"
      :heb-month="monthly.hebMonth.value"
      :heb-year="monthly.hebYear.value"
      :greg-month="monthly.gregMonth.value"
      :greg-year="monthly.gregYear.value"
      @prev="onPrev"
      @next="onNext"
      @today="onToday"
      @set-view="viewMode = $event"
      @select-heb-month="monthly.jumpToHebrew(monthly.hebYear.value, $event)"
      @select-heb-year="monthly.jumpToHebrew($event, monthly.hebMonth.value)"
      @select-greg-month="monthly.gregMonth.value = $event"
      @select-greg-year="monthly.gregYear.value = $event"
    />
    <WeeklyView v-if="viewMode === 'weekly'" :weekly="weekly" />
    <MonthlyView v-else :monthly="monthly" />
  </div>
</template>

<style scoped>
.page {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
  box-sizing: border-box;
}
</style>
