<script setup lang="ts">
import { computed } from 'vue'
import { HDate, HebrewCalendar, flags } from '@hebcal/core'
import type { useMonthlyView } from './useMonthlyView'

const props = defineProps<{
  monthly: ReturnType<typeof useMonthlyView>
}>()

const HEB_DAY_ABBR = ['א׳', 'ב׳', 'ג׳', 'ד׳', 'ה׳', 'ו׳', 'ש׳']
const GREG_DAY_ABBR = ['SUN', 'MON', 'TUE', 'WED', 'THU', 'FRI', 'SAT']

interface GridCell {
  date: Date
  gregDay: number
  hebGem: string
  isToday: boolean
  isShabbat: boolean
  isCurrentMonth: boolean
  holidays: string[]
  parasha: string | null
}

const today0 = new Date()
today0.setHours(0, 0, 0, 0)

function strip(s: string) {
  return s.replace(/[\u0591-\u05BD\u05BF-\u05C7]/g, '')
}

function dayGem(hd: HDate): string {
  return (hd.renderGematriya().split(' ')[0] ?? String(hd.getDate())).replace(/[\u05F3\u05F4]/g, '')
}

const grid = computed<GridCell[]>(() => {
  const year = props.monthly.gregYear.value
  const month = props.monthly.gregMonth.value

  const firstDay = new Date(year, month, 1)
  const lastDay = new Date(year, month + 1, 0)

  // Start from Sunday of the first week
  const start = new Date(firstDay)
  start.setDate(start.getDate() - start.getDay())

  // End on Saturday of the last week
  const end = new Date(lastDay)
  end.setDate(end.getDate() + (6 - end.getDay()))

  // Fetch events for the whole grid range
  const events = HebrewCalendar.calendar({
    start: new HDate(start),
    end: new HDate(end),
    sedrot: true,
    il: true,
  })

  const byDate = new Map<string, typeof events>()
  for (const e of events) {
    const k = e.getDate().greg().toISOString().slice(0, 10)
    if (!byDate.has(k)) byDate.set(k, [])
    byDate.get(k)!.push(e)
  }

  const cells: GridCell[] = []
  const cur = new Date(start)

  while (cur <= end) {
    const date = new Date(cur)
    date.setHours(0, 0, 0, 0)
    const hd = new HDate(date)
    const key = date.toISOString().slice(0, 10)
    const dayEvents = byDate.get(key) ?? []

    const holidays: string[] = []
    let parasha: string | null = null

    for (const e of dayEvents) {
      const f = e.getFlags()
      if (f & flags.PARSHA_HASHAVUA) {
        parasha = strip(e.render('he')).replace(/^פרשת\s+/, '')
      } else if (
        f &
        (flags.CHAG |
          flags.MINOR_FAST |
          flags.MAJOR_FAST |
          flags.ROSH_CHODESH |
          flags.SPECIAL_SHABBAT |
          flags.MODERN_HOLIDAY |
          flags.CHOL_HAMOED |
          flags.MINOR_HOLIDAY |
          flags.CHANUKAH_CANDLES)
      ) {
        holidays.push(strip(e.render('he')))
      }
    }

    cells.push({
      date,
      gregDay: date.getDate(),
      hebGem: dayGem(hd),
      isToday: date.getTime() === today0.getTime(),
      isShabbat: date.getDay() === 6,
      isCurrentMonth: date.getMonth() === month,
      holidays,
      parasha,
    })

    cur.setDate(cur.getDate() + 1)
  }

  return cells
})
</script>

<template>
  <div class="monthly">
    <!-- Day-of-week header -->
    <div class="dow-header">
      <div v-for="label in GREG_DAY_ABBR" :key="label" class="dow-cell">{{ label }}</div>
    </div>

    <!-- Grid -->
    <div class="grid">
      <div
        v-for="cell in grid"
        :key="cell.date.toISOString()"
        class="cell"
        :class="{
          today: cell.isToday,
          shabbat: cell.isShabbat,
          outside: !cell.isCurrentMonth,
          holiday: cell.holidays.length > 0,
        }"
      >
        <div class="cell-header">
          <span class="greg-num">{{ cell.gregDay }}</span>
          <span class="heb-gem">{{ cell.hebGem }}</span>
        </div>
        <div class="cell-events">
          <span v-if="cell.parasha" class="ev parasha">{{ cell.parasha }}</span>
          <span v-for="h in cell.holidays" :key="h" class="ev holiday">{{ h }}</span>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.monthly {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  padding: 4px;
}

/* Day-of-week header */
.dow-header {
  display: grid;
  grid-template-columns: repeat(7, 1fr);
  margin-bottom: 2px;
}
.dow-cell {
  font-size: 9px;
  font-weight: 700;
  color: var(--text-secondary);
  text-transform: uppercase;
  text-align: center;
  padding: 2px 0;
}

/* Grid */
.grid {
  flex: 1;
  display: grid;
  grid-template-columns: repeat(7, 1fr);
  grid-auto-rows: 1fr;
  gap: 1px;
  overflow: hidden;
}

/* Cell */
.cell {
  display: flex;
  flex-direction: column;
  border: 1px solid color-mix(in srgb, var(--border-color) 50%, transparent);
  border-radius: 3px;
  padding: 2px 3px;
  overflow: hidden;
  min-height: 0;
  background: var(--bg-primary);
}
.cell.outside {
  opacity: 0.35;
}
.cell.shabbat {
  background: color-mix(in srgb, var(--text-secondary) 5%, transparent);
}
.cell.holiday {
  background: color-mix(in srgb, #f0a500 6%, transparent);
}
.cell.today {
  border-color: var(--accent-color, #0078d4);
  border-width: 2px;
}

.cell-header {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: 2px;
}
.greg-num {
  font-size: 13px;
  font-weight: 300;
  color: var(--text-primary);
  line-height: 1;
}
.cell.today .greg-num {
  color: var(--accent-color, #0078d4);
  font-weight: 600;
}
.heb-gem {
  font-size: 9px;
  color: var(--text-secondary);
  line-height: 1;
}

.cell-events {
  display: flex;
  flex-direction: column;
  gap: 1px;
  overflow: hidden;
  flex: 1;
}
.ev {
  font-size: 9px;
  line-height: 1.2;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.parasha {
  color: var(--text-primary);
  font-weight: 600;
}
.holiday {
  color: #f0a500;
  font-weight: 600;
}
.candle {
  color: var(--accent-color, #0078d4);
}
.havdalah {
  color: #9c6fde;
}
</style>
