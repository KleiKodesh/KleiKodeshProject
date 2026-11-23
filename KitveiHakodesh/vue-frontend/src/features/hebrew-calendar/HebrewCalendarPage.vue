<script setup lang="ts">
import PageCard from '@/components/PageCard.vue'
import { ref, watch, onMounted } from 'vue'
import { HDate } from '@hebcal/core'
import { lsGet, lsSet } from '@/utils/persistence'

const KEYS = {
  SETTINGS_CALENDAR_VIEW: 'calendar.viewMode',
} as const
import { useZmanim } from './useZmanim'
import { useWeeklyView } from './useWeeklyView'
import { useMonthlyView } from './useMonthlyView'
import CalendarHeader from './CalendarHeader.vue'
import WeeklyView from './WeeklyView.vue'
import MonthlyView from './MonthlyView.vue'
import ZmanimWidget from '@/features/widgets/ZmanimWidget.vue'
import DailyLearningWidget from '@/features/widgets/DailyLearningWidget.vue'

/** The extended calendar: today's zmanim and daily learning beside it (CALENDAR_EXTENDED). */
const props = defineProps<{ extended?: boolean }>()

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

// ── Date-picker selections ────────────────────────────────────────────────
// Each selection drives BOTH views (monthly month state + weekly week offset)
// so the calendar jumps regardless of which view is active and stays in sync
// when the user toggles between them.
function onSelectHebMonth(m: number) {
  monthly.jumpToHebrew(monthly.hebYear.value, m)
  weekly.goToDate(new HDate(1, m, monthly.hebYear.value).greg())
}
function onSelectHebYear(y: number) {
  monthly.jumpToHebrew(y, monthly.hebMonth.value)
  weekly.goToDate(new HDate(1, monthly.hebMonth.value, y).greg())
}
function onSelectGregMonth(m: number) {
  monthly.gregMonth.value = m
  weekly.goToDate(new Date(monthly.gregYear.value, m, 1))
}
function onSelectGregYear(y: number) {
  monthly.gregYear.value = y
  weekly.goToDate(new Date(y, monthly.gregMonth.value, 1))
}

onMounted(() => {
  const savedView = lsGet<ViewMode>(KEYS.SETTINGS_CALENDAR_VIEW)
  viewMode.value = savedView === 'monthly' ? 'monthly' : 'weekly'
  weekly.reset()
  monthly.reset()
  initZmanim()
})
</script>

<template>
  <div class="page" :class="{ 'page--extended': props.extended }">
    <PageCard class="calendar-card">
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
            @select-heb-month="onSelectHebMonth"
            @select-heb-year="onSelectHebYear"
            @select-greg-month="onSelectGregMonth"
            @select-greg-year="onSelectGregYear"
          />
          <WeeklyView v-if="viewMode === 'weekly'" :weekly="weekly" class="calendar-view" />
          <MonthlyView v-else :monthly="monthly" class="calendar-view" />
    </PageCard>
    <!-- Extended calendar: today's zmanim and daily learning beside it (below it in a narrow window). -->
    <aside v-if="props.extended" class="calendar-side">
      <section class="calendar-side-zmanim">
        <ZmanimWidget />
      </section>
      <section class="calendar-side-learning">
        <h3 class="calendar-side-title">{{ $t('layout.widgets.learning.label') }}</h3>
        <DailyLearningWidget />
      </section>
    </aside>
  </div>
</template>

<style scoped>
.page {
  display: flex;
  flex-direction: row;
  height: 100%;
  background: var(--bg-primary);
}

.calendar-card { flex: 1 1 auto; min-width: 0; }
.calendar-side {
  flex: 0 0 260px;
  min-height: 0;
  display: flex;
  flex-direction: column;
  border-inline-start: 1px solid var(--border-color);
  background: var(--bg-primary);
}
/* Half each, each scrolling on its own. */
.calendar-side-zmanim,
.calendar-side-learning { flex: 1 1 0; min-height: 0; overflow-y: auto; scrollbar-width: thin; }
.calendar-side-learning { border-top: 1px solid var(--border-color); }
.calendar-side-title {
  margin: 0;
  padding: 8px 12px 0;
  font-size: 13px;
  font-weight: 600;
  color: var(--text-primary);
}
/* Narrow window: zmanim and learning go under the calendar. */
@media (max-width: 640px) {
  .page--extended { flex-direction: column; overflow-y: auto; }
  .page--extended .calendar-card { min-height: 420px; }
  .calendar-side-zmanim,
  .calendar-side-learning { flex: none; overflow: visible; }
  .calendar-side { flex: none; border-inline-start: none; border-top: 1px solid var(--border-color); }
}

.calendar-view {
  flex: 1;
  min-height: 0;
}

</style>
