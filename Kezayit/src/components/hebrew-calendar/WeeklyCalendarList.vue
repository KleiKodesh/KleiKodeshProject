<script setup lang="ts">
import { ref } from 'vue'
import type { City, WeekDay } from './useWeeklyCalendar'
import type { useWeeklyCalendar } from './useWeeklyCalendar'

const props = defineProps<{
  city: City
  cities: City[]
  weekly: ReturnType<typeof useWeeklyCalendar>
}>()
const emit = defineEmits<{ (e: 'city-change', city: City): void }>()

const { week } = props.weekly

const expandedDay = ref<number | null>(null)
function toggleDay(dow: number) {
  expandedDay.value = expandedDay.value === dow ? null : dow
}

// Reference uses short English abbreviations on the left side
const DAY_ABBR = ['SUN', 'MON', 'TUE', 'WED', 'THU', 'FRI', 'SAT']

const ZMANIM_LABELS: Array<{ key: keyof WeekDay['zmanim']; label: string }> = [
  { key: 'alot', label: 'עלות השחר' },
  { key: 'misheyakir', label: 'משיכיר' },
  { key: 'sunrise', label: 'הנץ החמה' },
  { key: 'sofShmaGra', label: 'סו״ז ק״ש גר״א' },
  { key: 'sofShmaMga', label: 'סו״ז ק״ש מג״א' },
  { key: 'sofTfillaGra', label: 'סו״ז תפילה גר״א' },
  { key: 'sofTfillaMga', label: 'סו״ז תפילה מג״א' },
  { key: 'chatzot', label: 'חצות' },
  { key: 'minchaGedola', label: 'מנחה גדולה' },
  { key: 'minchaKetana', label: 'מנחה קטנה' },
  { key: 'plag', label: 'פלג המנחה' },
  { key: 'sunset', label: 'שקיעה' },
  { key: 'tzeit', label: 'צאת הכוכבים' },
]
</script>

<template>
  <div class="cal-wrap">
    <!-- Day rows -->
    <div class="days-list" :class="{ 'has-expanded': expandedDay !== null }">
      <div
        v-for="day in week.days"
        :key="day.dayOfWeek"
        class="day-tile"
        :class="{
          'is-today': day.isToday,
          'is-shabbat': day.isShabbat,
          'is-friday': day.isFriday,
          'has-holiday': day.holidays.length > 0,
        }"
      >
        <div class="day-row">
          <!-- COL 1 — physical RIGHT: Hebrew day name + large gematriya -->
          <div class="heb-col">
            <span class="heb-day-name" :class="{ accent: day.isShabbat }">{{
              day.hebrewDayName
            }}</span>
            <span class="heb-day-gem" :class="{ accent: day.isShabbat && !day.isToday }">{{
              day.hebrewDayGem
            }}</span>
          </div>

          <!-- COL 2 — center: all event info -->
          <div class="center-col">
            <div class="events-line">
              <span v-if="day.parasha" class="event-parasha">{{ day.parasha }}</span>
              <span v-if="day.parasha && day.holidays.length" class="event-sep">·</span>
              <span v-for="(h, i) in day.holidays" :key="h" class="event-holiday"
                >{{ h }}<span v-if="i < day.holidays.length - 1" class="event-sep"> · </span></span
              >
              <span v-if="day.chanukahCandles" class="event-sep">·</span>
              <span v-if="day.chanukahCandles" class="event-chanukah">{{
                day.chanukahCandles
              }}</span>
              <span
                v-if="day.parasha || day.holidays.length || day.chanukahCandles"
                class="event-sep"
                >·</span
              >
              <button
                class="zmanim-link"
                :class="{ open: expandedDay === day.dayOfWeek }"
                @click="toggleDay(day.dayOfWeek)"
              >
                זמני היום
              </button>
            </div>
          </div>

          <div class="greg-col">
            <span class="day-abbr" :class="{ accent: day.isShabbat }">{{
              DAY_ABBR[day.dayOfWeek]
            }}</span>
            <span class="greg-day" :class="{ accent: day.isShabbat || day.isToday }">{{
              day.gregDay
            }}</span>
          </div>
        </div>

        <!-- Zmanim panel — tap row to expand -->
        <div v-if="expandedDay === day.dayOfWeek" class="zmanim-panel">
          <!-- Extra day info -->
          <div
            v-if="
              day.havdalah ||
              day.candleLighting ||
              day.omer ||
              day.shabbatMevarchim ||
              day.molad ||
              day.yomKippurKatan ||
              day.fastStart ||
              day.fastEnd ||
              day.dafYomi ||
              day.mishnaYomi ||
              day.nachYomi
            "
            class="zmanim-extra"
          >
            <span v-if="day.havdalah" class="havdalah-line">צאת שבת: {{ day.havdalah }}</span>
            <span v-if="day.candleLighting" class="candle-line"
              >הדלקת נרות: {{ day.candleLighting }}</span
            >
            <span v-if="day.omer" class="event-omer">{{ day.omer }}</span>
            <span v-if="day.yomKippurKatan" class="event-holiday">{{ day.yomKippurKatan }}</span>
            <span v-if="day.fastStart" class="event-fast-time">{{ day.fastStart }}</span>
            <span v-if="day.fastEnd" class="event-fast-time">{{ day.fastEnd }}</span>
            <span v-if="day.shabbatMevarchim" class="event-mevarchim">{{
              day.shabbatMevarchim
            }}</span>
            <span v-if="day.molad" class="event-molad">{{ day.molad }}</span>
            <span v-if="day.dafYomi" class="event-learning"
              ><span class="learning-label">דף יומי:</span> {{ day.dafYomi }}</span
            >
            <span v-if="day.mishnaYomi" class="event-learning"
              ><span class="learning-label">משנה יומית:</span> {{ day.mishnaYomi }}</span
            >
            <span v-if="day.nachYomi" class="event-learning"
              ><span class="learning-label">נ״ך יומי:</span> {{ day.nachYomi }}</span
            >
          </div>
          <div v-for="z in ZMANIM_LABELS" :key="z.key" class="z-row">
            <span class="z-label">{{ z.label }}</span>
            <span class="z-time">{{ day.zmanim[z.key] ?? '—' }}</span>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.cal-wrap {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

/* ── Days list ───────────────────────────────────────────────────────────── */
.days-list {
  flex: 1;
  overflow: hidden;
  display: flex;
  flex-direction: column;
}
.days-list.has-expanded {
  overflow-y: auto;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
}

/* ── Day tile ────────────────────────────────────────────────────────────── */
.day-tile {
  flex: 1;
  border-bottom: 1px dashed color-mix(in srgb, var(--border-color) 70%, transparent);
  display: flex;
  flex-direction: column;
  min-height: 0;
}
.has-expanded .day-tile {
  flex: none;
  min-height: 52px;
}
.zmanim-link {
  display: inline-flex;
  align-items: center;
  font-size: 12px;
  font-weight: 600;
  color: var(--accent-color, #0078d4);
  opacity: 0.7;
  cursor: pointer;
  -webkit-tap-highlight-color: transparent;
  border-radius: 4px;
  padding: 2px 4px;
}
.zmanim-link:hover {
  opacity: 1;
  background: color-mix(in srgb, var(--accent-color, #0078d4) 10%, transparent);
}
.zmanim-link.open {
  opacity: 1;
  background: color-mix(in srgb, var(--accent-color, #0078d4) 10%, transparent);
}
.day-tile.is-shabbat,
.day-tile.is-friday {
  background: color-mix(in srgb, var(--text-secondary) 6%, transparent);
}
.day-tile.is-today {
  border-inline-start: 3px solid var(--accent-color, #0078d4);
}
.day-tile.is-today.has-holiday {
  border-inline-start: 3px solid var(--accent-color, #0078d4);
  background: color-mix(in srgb, #f0a500 6%, transparent);
}
.day-tile.has-holiday {
  background: color-mix(in srgb, #f0a500 6%, transparent);
}
.day-tile.is-today.has-holiday {
  background: color-mix(in srgb, #f0a500 6%, transparent);
}

/* ── Main row — RTL flex, 4 columns ─────────────────────────────────────── */
.day-row {
  display: flex;
  flex-direction: row;
  align-items: center;
  flex: 1;
  padding: 0;
}

/* COL 1 — physical RIGHT (inline-start): Hebrew name + gematriya */
.heb-col {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  justify-content: center;
  width: 80px;
  flex-shrink: 0;
  padding-inline-end: 16px;
  padding-inline-start: 4px;
}
.heb-day-name {
  font-size: 10px;
  font-weight: 700;
  color: var(--text-secondary);
  line-height: 1.2;
  text-transform: uppercase;
  letter-spacing: 0.05em;
}
.heb-day-gem {
  font-size: 30px;
  font-weight: 300;
  line-height: 1;
  color: var(--text-primary);
}

/* COL 2 — center: all event info */
.center-col {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 6px;
  padding: 4px 8px;
  text-align: center;
  overflow: hidden;
}
.events-line {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: center;
  gap: 4px;
  overflow: hidden;
  max-width: 100%;
}
.event-sep {
  color: var(--text-secondary);
  opacity: 0.5;
  font-size: 12px;
}
.event-parasha {
  font-size: 12px;
  font-weight: 600;
  color: var(--text-primary);
}
.event-holiday {
  font-size: 12px;
  font-weight: 600;
  color: #f0a500;
}
.event-omer {
  font-size: 12px;
  color: var(--text-secondary);
}
.event-chanukah {
  font-size: 12px;
  font-weight: 600;
  color: #e8a020;
}
.event-mevarchim {
  font-size: 12px;
  color: var(--text-secondary);
  font-style: italic;
}
.event-molad {
  font-size: 12px;
  color: var(--text-secondary);
}
.event-fast-time {
  font-size: 12px;
  font-weight: 600;
  color: var(--text-secondary);
}
.event-learning {
  font-size: 12px;
  color: var(--text-secondary);
}
.learning-label {
  font-weight: 600;
  color: var(--text-primary);
}
.havdalah-line {
  font-size: 12px;
  font-weight: 600;
  color: #9c6fde;
}
.bottom-line {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 10px;
}
.candle-line {
  font-size: 12px;
  font-weight: 600;
  color: var(--accent-color, #0078d4);
}
.greg-col {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  justify-content: center;
  width: 80px;
  flex-shrink: 0;
  padding-inline-start: 16px;
  padding-inline-end: 4px;
}
.day-abbr {
  font-size: 10px;
  font-weight: 700;
  letter-spacing: 0.05em;
  color: var(--text-secondary);
  line-height: 1.2;
  text-transform: uppercase;
}
.greg-day {
  font-size: 30px;
  font-weight: 300;
  line-height: 1;
  color: var(--text-primary);
}

/* accent color helper */
.accent {
  color: var(--accent-color, #0078d4) !important;
}
.heb-day-gem.accent {
  font-weight: 500;
}
.greg-day.accent {
  font-weight: 500;
}

/* ── Zmanim panel ────────────────────────────────────────────────────────── */
.zmanim-panel {
  display: grid;
  grid-template-columns: 1fr auto;
  padding: 4px 14px 8px;
  border-top: 1px solid color-mix(in srgb, var(--border-color) 40%, transparent);
  overflow-y: auto;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
}
.zmanim-extra {
  grid-column: 1 / -1;
  display: flex;
  flex-direction: column;
  gap: 3px;
  padding: 6px 0 8px;
  border-bottom: 1px solid color-mix(in srgb, var(--border-color) 40%, transparent);
  margin-bottom: 4px;
}
.zmanim-extra {
  grid-column: 1 / -1;
  display: flex;
  flex-direction: column;
  gap: 3px;
  padding: 6px 0 8px;
  border-bottom: 1px solid color-mix(in srgb, var(--border-color) 40%, transparent);
  margin-bottom: 4px;
}
.z-row {
  display: contents;
}
.z-label {
  font-size: 12px;
  color: var(--text-secondary);
  padding: 3px 0;
  border-bottom: 1px solid color-mix(in srgb, var(--border-color) 20%, transparent);
}
.z-time {
  font-size: 12px;
  font-weight: 600;
  color: var(--text-primary);
  font-variant-numeric: tabular-nums;
  text-align: start;
  padding: 3px 0;
  border-bottom: 1px solid color-mix(in srgb, var(--border-color) 20%, transparent);
}

/* ── Footer ──────────────────────────────────────────────────────────────── */
</style>
