<script setup lang="ts">
import PageCard from '@/components/PageCard.vue'
import { ref, watch, onMounted, computed } from 'vue'
import { HDate } from '@hebcal/core'
import { uiLanguage } from '@/i18n'
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

/**
 * The extended calendar (the separate calendar app): a click on a day picks it, and that
 * day's zmanim and daily learning show beside the calendar. Opens on the month.
 */
const props = defineProps<{ extended?: boolean }>()

type ViewMode = 'weekly' | 'monthly'

const viewMode = ref<ViewMode>('weekly')

const selectedDate = ref(new Date())
/** undefined = days are not pickable (the in-app calendar widget). */
const pickedDate = computed(() => (props.extended ? selectedDate.value : undefined))
const selectedLabel = computed(() => {
  const d = selectedDate.value
  const hd = new HDate(d)
  const heb = uiLanguage === 'en' ? hd.render('en') : hd.renderGematriya(true)
  return `${heb} · ${d.toLocaleDateString(uiLanguage === 'en' ? 'en-US' : 'he-IL', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' })}`
})
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
  selectedDate.value = new Date()
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
  const fallback: ViewMode = props.extended ? 'monthly' : 'weekly'
  viewMode.value = savedView === 'monthly' || savedView === 'weekly' ? savedView : fallback
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
          <WeeklyView v-if="viewMode === 'weekly'" :weekly="weekly" :selected-date="pickedDate" class="calendar-view" @select-day="selectedDate = $event" />
          <MonthlyView v-else :monthly="monthly" :selected-date="pickedDate" class="calendar-view" @select-day="selectedDate = $event" />
    </PageCard>
    <!-- Extended calendar: the picked day's zmanim and daily learning beside it (below it in a narrow window). -->
    <aside v-if="props.extended" class="calendar-side">
      <div class="calendar-side-date">{{ selectedLabel }}</div>
      <section class="calendar-side-zmanim">
        <ZmanimWidget :date="selectedDate" />
      </section>
      <section class="calendar-side-learning">
        <DailyLearningWidget :date="selectedDate" />
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
/* Extended: the month takes two shares of the width, the side column one (never under 260px). */
.page--extended .calendar-card { flex: 2 1 0; }
/* The separate calendar app is the calendar itself: no card around it, at any width (as PageCard
   already does in a narrow window) - painted straight onto the window, edge to edge. */
.page--extended .calendar-card { max-width: none; padding: 0; }
.page--extended .calendar-card :deep(.page-card) { border: none; border-radius: 0; background: var(--bg-primary); }
.calendar-side {
  flex: 1 1 0;
  min-width: 260px;
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
.calendar-side-date {
  flex: none;
  padding: 10px 12px 6px;
  font-size: 14px;
  font-weight: 700;
  color: var(--text-primary);
  border-bottom: 1px solid var(--border-color);
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
