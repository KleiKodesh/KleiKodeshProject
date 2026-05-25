<script setup lang="ts">
import { ref, watch, onMounted } from 'vue'
import { lsGet, lsSet, KEYS } from '@/utils/persistence'
import { useZmanim } from './useZmanim'
import { useWeeklyView } from './useWeeklyView'
import { useMonthlyView } from './useMonthlyView'
import CalendarHeader from './CalendarHeader.vue'
import WeeklyView from './WeeklyView.vue'
import MonthlyView from './MonthlyView.vue'

type ViewMode = 'weekly' | 'monthly'

const viewMode = ref<ViewMode>('weekly')
watch(viewMode, (v) => lsSet(KEYS.SETTINGS_CALENDAR_VIEW, v))

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

onMounted(() => {
  const savedView = lsGet<ViewMode>(KEYS.SETTINGS_CALENDAR_VIEW)
  viewMode.value = savedView === 'monthly' ? 'monthly' : 'weekly'
  weekly.reset()
  monthly.reset()
  initZmanim(lsGet<string>(KEYS.SETTINGS_ZMANIM_CITY) ?? undefined)
})
</script>

<template>
  <div class="page">
    <div class="page-scroller">
      <div class="page-inner">
        <div class="card">
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
          <WeeklyView v-if="viewMode === 'weekly'" :weekly="weekly" class="calendar-view" />
          <MonthlyView v-else :monthly="monthly" class="calendar-view" />
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.page {
  display: flex;
  flex-direction: column;
  height: 100%;
  background: var(--bg-primary);
  direction: rtl;
}

/* Full-width scroller */
.page-scroller {
  flex: 1;
  overflow-y: auto;
  overflow-x: hidden;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
  display: flex;
  flex-direction: column;
}

/* Centered content column */
.page-inner {
  max-width: 680px;
  margin: 0 auto;
  padding: 8px;
  box-sizing: border-box;
  display: flex;
  flex-direction: column;
  flex: 1;
  width: 100%;
}

/* Card styling */
.card {
  display: flex;
  flex-direction: column;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  overflow: hidden;
  flex: 1;
  min-height: 300px;
}

.calendar-view {
  flex: 1;
  min-height: 0;
}

/* Mobile / Android responsiveness */
@media (max-width: 600px) {
  .page-inner {
    padding: 0;
    max-width: 100%;
  }

  .card {
    border-radius: 0;
    border: none;
  }
}
</style>
