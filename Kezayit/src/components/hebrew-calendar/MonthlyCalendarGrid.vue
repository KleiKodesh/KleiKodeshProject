<script setup lang="ts">
import type { useHebrewCalendar } from './useHebrewCalendar'

const props = defineProps<{
  monthly: ReturnType<typeof useHebrewCalendar>
}>()

const { weeks, dayNames } = props.monthly
</script>

<template>
  <div class="month-wrap">
    <!-- Day-of-week column headers -->
    <div class="dow-row">
      <div v-for="name in dayNames" :key="name" class="dow-cell">{{ name }}</div>
    </div>

    <!-- Grid -->
    <div class="grid-body">
      <div v-for="(week, wi) in weeks" :key="wi" class="grid-row">
        <div
          v-for="day in week.days"
          :key="day.date.toISOString()"
          class="day-cell"
          :class="{
            'is-today': day.isToday,
            'is-shabbat': day.isShabbat,
            'other-month': !day.isCurrentMonth,
            'has-holiday': day.holidays.length > 0,
          }"
        >
          <div class="cell-top">
            <span class="greg-num">{{ day.date.getDate() }}</span>
            <span class="heb-num">{{ day.hebrewDayStr }}</span>
          </div>
          <div v-if="day.holidays.length" class="cell-holidays">
            <span v-for="h in day.holidays" :key="h" class="cell-holiday">{{ h }}</span>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.month-wrap {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

/* ── Day-of-week row ─────────────────────────────────────────────────────── */
.dow-row {
  display: grid;
  grid-template-columns: repeat(7, 1fr);
  border-bottom: 1px solid var(--border-color);
  flex-shrink: 0;
}
.dow-cell {
  font-size: 10px;
  font-weight: 700;
  color: var(--text-secondary);
  text-align: center;
  padding: 4px 0;
  letter-spacing: 0.03em;
}

/* ── Grid ────────────────────────────────────────────────────────────────── */
.grid-body {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}
.grid-row {
  flex: 1;
  display: grid;
  grid-template-columns: repeat(7, 1fr);
  min-height: 0;
}
.day-cell {
  border-inline-end: 1px solid color-mix(in srgb, var(--border-color) 50%, transparent);
  border-bottom: 1px solid color-mix(in srgb, var(--border-color) 50%, transparent);
  padding: 3px 4px;
  display: flex;
  flex-direction: column;
  gap: 2px;
  overflow: hidden;
  min-height: 0;
}
.day-cell:last-child {
  border-inline-end: none;
}
.day-cell.is-shabbat {
  background: color-mix(in srgb, var(--text-secondary) 5%, transparent);
}
.day-cell.has-holiday {
  background: color-mix(in srgb, #f0a500 5%, transparent);
}
.day-cell.other-month {
  opacity: 0.35;
}
.day-cell.is-today {
  background: color-mix(in srgb, var(--accent-color, #0078d4) 10%, transparent);
}

/* ── Cell content ────────────────────────────────────────────────────────── */
.cell-top {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: 2px;
}
.greg-num {
  font-size: 13px;
  font-weight: 600;
  color: var(--text-primary);
  line-height: 1;
}
.heb-num {
  font-size: 10px;
  color: var(--text-secondary);
  line-height: 1;
}
.is-today .greg-num {
  font-weight: 700;
}
.is-today .heb-num {
}
.is-shabbat .greg-num,
.is-shabbat .heb-num {
  color: var(--accent-color, #0078d4);
}
.cell-holidays {
  display: flex;
  flex-direction: column;
  gap: 1px;
  overflow: hidden;
}
.cell-holiday {
  font-size: 9px;
  color: #f0a500;
  font-weight: 600;
  line-height: 1.3;
  word-break: break-word;
}
</style>
