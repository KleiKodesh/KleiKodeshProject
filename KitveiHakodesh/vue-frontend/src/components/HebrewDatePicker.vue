<script setup lang="ts">
// Reusable Hebrew date picker: a button showing the picked day's Hebrew date; clicking it opens
// a Hebrew-month grid (gematriya day, small civil day) with month back/forward. v-model is a civil Date.
import { computed, ref, shallowRef, useTemplateRef } from 'vue'
import { useElementBounding, useEventListener } from '@vueuse/core'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { hebYearGem as yearGem } from '@/features/hebrew-calendar/useMonthlyView'
import { HDate } from '@hebcal/hdate'
import { IconCalendarLtr20Regular, IconChevronLeft20Regular, IconChevronRight20Regular } from '@iconify-prerendered/vue-fluent'
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'

const model = defineModel<Date>({ required: true })
const hostWindow = useHostWindow()

const open = ref(false)
const btn = useTemplateRef<HTMLElement>('btn')
const pop = useTemplateRef<HTMLElement>('pop')
// Its own window/scroll listeners go on the window it is drawn in (VueUse's default is the main one).
const { bottom, right, update: updatePosition } = useElementBounding(btn, { windowResize: false, windowScroll: false })
useEventListener(hostWindow, 'resize', updatePosition, { passive: true })
useEventListener(hostWindow, 'scroll', updatePosition, { capture: true, passive: true })
const closer = useDropdownClose(pop, () => { open.value = false }, { toggleButton: btn })
/** Classic keys: arrows move the focused day (RTL), PageUp/Down a month, Enter picks, Esc closes. */
const focusDay = shallowRef(new Date())
useEventListener(hostWindow, 'keydown', (e: KeyboardEvent) => {
  if (!open.value) return
  if (e.key === 'Escape') { open.value = false; return }
  if (e.key === 'Enter') { pick(focusDay.value); e.preventDefault(); return }
  const step: Record<string, number> = { ArrowLeft: 1, ArrowRight: -1, ArrowUp: -7, ArrowDown: 7 }
  let hd = new HDate(focusDay.value)
  if (e.key in step) hd = hd.add(step[e.key]!, 'd')
  else if (e.key === 'PageUp' || e.key === 'PageDown') hd = sameDayInMonth(hd, e.key === 'PageUp' ? -1 : 1)
  else return
  e.preventDefault()
  focusDay.value = hd.greg()
  mode.value = 'days'
  shownMonth.value = new HDate(1, hd.getMonth(), hd.getFullYear())
})

/** The same day of the month a Hebrew month back/forward (the month's last day when it is shorter). */
function sameDayInMonth(hd: HDate, by: number): HDate {
  const first = new HDate(1, hd.getMonth(), hd.getFullYear())
  const target = by > 0 ? first.add(first.daysInMonth(), 'd') : first.subtract(1, 'd')
  const days = new HDate(1, target.getMonth(), target.getFullYear()).daysInMonth()
  return new HDate(Math.min(hd.getDate(), days), target.getMonth(), target.getFullYear())
}

const sameDay = (a: Date, b: Date) => a.toDateString() === b.toDateString()
const gemDay = (hd: HDate) => hd.renderGematriya().split(' ')[0] ?? ''

const label = computed(() => new HDate(model.value).renderGematriya())

/** First day of the Hebrew month shown in the grid. */
const shownMonth = shallowRef(new HDate(1, 1, 5787))
function toggle() {
  if (closer.justClosed.value) return
  if (!open.value) {
    const hd = new HDate(model.value)
    shownMonth.value = new HDate(1, hd.getMonth(), hd.getFullYear())
    focusDay.value = model.value
    mode.value = 'days'
    today.value = new Date() // the today ring stays right past midnight
  }
  open.value = !open.value
}
function shiftMonth(by: number) {
  const first = shownMonth.value
  const target = by > 0 ? first.add(first.daysInMonth(), 'd') : first.subtract(1, 'd')
  shownMonth.value = new HDate(1, target.getMonth(), target.getFullYear())
}

/** 'days' = month grid; 'months' = the year's months (click the title, like any date picker). */
const mode = ref<'days' | 'months'>('days')
const year = computed(() => shownMonth.value.getFullYear())
/** The year's months in calendar order (Tishrei first), with their gematriya names. */
const months = computed(() => {
  const y = year.value, out: Array<{ m: number; name: string }> = []
  const first = new HDate(1, 7, y)
  for (let d = first; d.getFullYear() === y; d = d.add(d.daysInMonth(), 'd')) {
    out.push({ m: d.getMonth(), name: d.renderGematriya().split(' ').slice(1, -1).join(' ') })
  }
  return out
})
function shiftYear(by: number) {
  const y = year.value + by
  const m = Math.min(shownMonth.value.getMonth(), HDate.monthsInYear(y))
  shownMonth.value = new HDate(1, m, y)
}
function pickMonth(m: number) { shownMonth.value = new HDate(1, m, year.value); mode.value = 'days' }
function headPrev() { if (mode.value === 'days') shiftMonth(-1); else shiftYear(-1) }
function headNext() { if (mode.value === 'days') shiftMonth(1); else shiftYear(1) }

const monthTitle = computed(() => {
  const parts = shownMonth.value.renderGematriya().split(' ')
  return parts.slice(1).join(' ')
})
const weekdays = ['א', 'ב', 'ג', 'ד', 'ה', 'ו', 'ש']

const cells = computed(() => {
  const first = shownMonth.value
  const lead = first.getDay()
  const out: Array<{ hd: HDate; greg: Date } | null> = Array.from({ length: lead }, () => null)
  for (let i = 0; i < first.daysInMonth(); i++) {
    const hd = first.add(i, 'd')
    out.push({ hd, greg: hd.greg() })
  }
  return out
})
const today = shallowRef(new Date())

function pick(d: Date) {
  model.value = d
  open.value = false
}
</script>

<template>
  <button ref="btn" class="hdp-btn" type="button" :aria-expanded="open" :title="$t('hebrewCalendar.datePicker')" @click="toggle">
    <IconCalendarLtr20Regular /><span>{{ label }}</span>
  </button>
  <Teleport :to="hostWindow.document.body">
    <div
      v-if="open"
      ref="pop"
      class="hdp-pop"
      dir="rtl"
      :style="{ top: `${bottom + 4}px`, left: `${Math.max(4, right - 224)}px` }"
    >
      <div class="hdp-head">
        <button type="button" class="hdp-nav" :title="$t('hebrewCalendar.nav.prev')" @click="headPrev"><IconChevronRight20Regular /></button>
        <button type="button" class="hdp-title" @click="mode = mode === 'days' ? 'months' : 'days'">{{ mode === 'days' ? monthTitle : yearGem(year) }}</button>
        <button type="button" class="hdp-nav" :title="$t('hebrewCalendar.nav.next')" @click="headNext"><IconChevronLeft20Regular /></button>
      </div>
      <div v-if="mode === 'months'" class="hdp-months">
        <button
          v-for="mo in months" :key="mo.m" type="button" class="hdp-day"
          :class="{ sel: mo.m === shownMonth.getMonth() }" @click="pickMonth(mo.m)"
        >{{ mo.name }}</button>
      </div>
      <div v-else class="hdp-grid">
        <span v-for="w in weekdays" :key="w" class="hdp-wd">{{ w }}</span>
        <template v-for="(c, i) in cells" :key="i">
          <span v-if="!c" />
          <button
            v-else
            type="button"
            class="hdp-day"
            :class="{ sel: sameDay(c.greg, model), today: sameDay(c.greg, today), focus: sameDay(c.greg, focusDay) }"
            @click="pick(c.greg)"
          >
            <span>{{ gemDay(c.hd) }}</span><small>{{ c.greg.getDate() }}</small>
          </button>
        </template>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
.hdp-btn {
  display: inline-flex; align-items: center; gap: 4px;
  height: 26px; padding: 0 6px;
  background: none; border: 1px solid transparent; border-radius: 4px;
  color: var(--text-primary); font-size: 12px; cursor: pointer; white-space: nowrap;
}
.hdp-btn:hover { border-color: var(--border-color); }
.hdp-pop {
  position: fixed; z-index: 10000; width: 224px; padding: 6px;
  background: var(--bg-primary, Canvas); color: var(--text-primary);
  border: 1px solid var(--border-color); border-radius: 6px;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.18);
}
.hdp-head { display: flex; align-items: center; justify-content: space-between; margin-bottom: 4px; }
.hdp-title { font-size: 13px; font-weight: 700; background: none; border: none; border-radius: 4px; color: inherit; cursor: pointer; padding: 2px 8px; }
.hdp-title:hover { background: var(--hover-bg, rgba(127, 127, 127, 0.15)); }
.hdp-months { display: grid; grid-template-columns: repeat(3, 1fr); gap: 4px; }
.hdp-months .hdp-day { padding: 8px 0; }
.hdp-day.focus { outline: 1px dashed var(--text-secondary); }
.hdp-nav {
  display: inline-flex; width: 24px; height: 24px; align-items: center; justify-content: center;
  padding: 0; background: none; border: none; border-radius: 4px; color: var(--text-secondary); cursor: pointer;
}
.hdp-nav:hover { background: var(--hover-bg, rgba(127, 127, 127, 0.15)); color: var(--text-primary); }
.hdp-grid { display: grid; grid-template-columns: repeat(7, 1fr); gap: 2px; }
.hdp-wd { text-align: center; font-size: 11px; color: var(--text-secondary); padding: 2px 0; }
.hdp-day {
  display: flex; flex-direction: column; align-items: center; padding: 2px 0;
  background: none; border: 1px solid transparent; border-radius: 4px;
  color: var(--text-primary); font-size: 12px; cursor: pointer; line-height: 1.1;
}
.hdp-day small { font-size: 9px; color: var(--text-secondary); }
.hdp-day:hover { background: var(--hover-bg, rgba(127, 127, 127, 0.15)); }
.hdp-day.today { border-color: var(--border-color); }
.hdp-day.sel { background: var(--accent-color, #0078d4); color: #fff; }
.hdp-day.sel small { color: inherit; }
</style>
