<script setup lang="ts">
import type { WeekDay } from './useWeeklyCalendar'

defineProps<{
  day: WeekDay
  zmanimOpen: boolean
  learningOpen: boolean
}>()

defineEmits<{
  (e: 'toggle-zmanim'): void
  (e: 'toggle-learning'): void
}>()

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

const LEARNING_ROWS: Array<{ key: keyof WeekDay; label: string }> = [
  { key: 'dafYomi', label: 'דף יומי' },
  { key: 'dirshuAmudYomi', label: 'עמוד יומי (דירשו)' },
  { key: 'yerushalmiVilna', label: 'ירושלמי יומי' },
  { key: 'mishnaYomi', label: 'משנה יומית' },
  { key: 'nachYomi', label: 'נ״ך יומי' },
  { key: 'perekYomi', label: 'פרק יומי' },
  { key: 'rambam1', label: 'רמב״ם יומי (פרק)' },
  { key: 'rambam3', label: 'רמב״ם יומי (ג׳ פרקים)' },
  { key: 'seferHaMitzvot', label: 'ספר המצוות' },
  { key: 'kitzurShulchanAruch', label: 'קיצור שולחן ערוך' },
  { key: 'arukhHaShulchan', label: 'ערוך השולחן יומי' },
  { key: 'chofetzChaim', label: 'חפץ חיים' },
  { key: 'psalms', label: 'תהילים יומי' },
]
</script>

<template>
  <div
    class="day-tile"
    :class="{
      today: day.isToday,
      shabbat: day.isShabbat,
      friday: day.isFriday,
      holiday: day.holidays.length > 0,
    }"
  >
    <!-- ── Main row ── -->
    <div class="row">
      <!-- Hebrew date (physical RIGHT) -->
      <div class="date-col date-col--he">
        <span class="date-label">{{ day.hebrewDayName }}</span>
        <span class="date-num" :class="{ accent: day.isShabbat && !day.isToday }">{{
          day.hebrewDayGem
        }}</span>
      </div>

      <!-- Center: events + pills -->
      <div class="center">
        <div class="events">
          <span v-if="day.parasha" class="ev-parasha">{{ day.parasha }}</span>
          <span v-if="day.parasha && day.holidays.length" class="sep">·</span>
          <template v-for="(h, i) in day.holidays" :key="h">
            <span class="ev-holiday">{{ h }}</span>
            <span v-if="i < day.holidays.length - 1" class="sep">·</span>
          </template>
          <span v-if="day.chanukahCandles" class="sep">·</span>
          <span v-if="day.chanukahCandles" class="ev-chanukah">{{ day.chanukahCandles }}</span>
          <span v-if="day.parasha || day.holidays.length || day.chanukahCandles" class="sep"
            >·</span
          >
          <button class="pill" :class="{ active: zmanimOpen }" @click="$emit('toggle-zmanim')">
            זמני היום
          </button>
          <button
            v-if="LEARNING_ROWS.some((r) => day[r.key])"
            class="pill pill--green"
            :class="{ active: learningOpen }"
            @click="$emit('toggle-learning')"
          >
            לימוד יומי
          </button>
        </div>
      </div>

      <!-- Gregorian date (physical LEFT) -->
      <div class="date-col date-col--greg">
        <span class="date-label">{{ DAY_ABBR[day.dayOfWeek] }}</span>
        <span class="date-num" :class="{ accent: day.isToday || day.isShabbat }">{{
          day.gregDay
        }}</span>
      </div>
    </div>

    <!-- ── Zmanim panel ── -->
    <div v-if="zmanimOpen" class="panel">
      <div class="warning">
        ⚠️ אין לסמוך על הזמנים כלל! הזמנים שונים מהותית מהלוח ״עיתים לבינה״!
      </div>
      <div
        v-if="
          day.havdalah ||
          day.candleLighting ||
          day.omer ||
          day.shabbatMevarchim ||
          day.molad ||
          day.yomKippurKatan ||
          day.fastStart ||
          day.fastEnd
        "
        class="extra"
      >
        <span v-if="day.havdalah" class="ev-havdalah">צאת שבת: {{ day.havdalah }}</span>
        <span v-if="day.candleLighting" class="ev-candle"
          >הדלקת נרות: {{ day.candleLighting }}</span
        >
        <span v-if="day.omer" class="ev-omer">{{ day.omer }}</span>
        <span v-if="day.yomKippurKatan" class="ev-holiday">{{ day.yomKippurKatan }}</span>
        <span v-if="day.fastStart" class="ev-fast">{{ day.fastStart }}</span>
        <span v-if="day.fastEnd" class="ev-fast">{{ day.fastEnd }}</span>
        <span v-if="day.shabbatMevarchim" class="ev-mevarchim">{{ day.shabbatMevarchim }}</span>
        <span v-if="day.molad" class="ev-molad">{{ day.molad }}</span>
      </div>
      <div class="grid">
        <template v-for="z in ZMANIM_LABELS" :key="z.key">
          <span class="grid-label">{{ z.label }}</span>
          <span class="grid-value">{{ day.zmanim[z.key] ?? '—' }}</span>
        </template>
      </div>
    </div>

    <!-- ── Learning panel ── -->
    <div v-if="learningOpen" class="panel">
      <div class="grid">
        <template v-for="r in LEARNING_ROWS" :key="r.key">
          <template v-if="day[r.key]">
            <span class="grid-label">{{ r.label }}</span>
            <span class="grid-value">{{ day[r.key] }}</span>
          </template>
        </template>
      </div>
    </div>
  </div>
</template>

<style scoped>
/* ── Tile ── */
.day-tile {
  display: flex;
  flex-direction: column;
  overflow: hidden;
  border-bottom: 1px dashed color-mix(in srgb, var(--border-color) 70%, transparent);
}
.day-tile.shabbat,
.day-tile.friday {
  background: color-mix(in srgb, var(--text-secondary) 6%, transparent);
}
.day-tile.holiday {
  background: color-mix(in srgb, #f0a500 6%, transparent);
}
.day-tile.today {
  border-inline-start: 3px solid var(--accent-color, #0078d4);
}
.day-tile.today.holiday {
  background: color-mix(in srgb, #f0a500 6%, transparent);
}

/* ── Main row ── */
.row {
  display: flex;
  align-items: stretch;
  flex: 1;
  min-height: 0;
}

/* ── Date columns ── */
.date-col {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  width: 56px;
  overflow: hidden;
  padding: 2px 4px;
}
.date-col--he {
  align-items: flex-start;
}
.date-col--greg {
  align-items: flex-end;
}

.date-label {
  font-size: 9px;
  font-weight: 700;
  color: var(--text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.04em;
  line-height: 1;
  white-space: nowrap;
}
.date-num {
  font-size: 26px;
  font-weight: 300;
  line-height: 1;
  color: var(--text-primary);
  white-space: nowrap;
}
.date-num.accent {
  color: var(--accent-color, #0078d4);
  font-weight: 500;
}

/* ── Center ── */
.center {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 4px 6px;
  overflow: hidden;
  min-width: 0;
}
.events {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: center;
  gap: 3px;
  overflow: hidden;
}
.sep {
  color: var(--text-secondary);
  opacity: 0.4;
  font-size: 11px;
}
.ev-parasha {
  font-size: 12px;
  font-weight: 600;
  color: var(--text-primary);
}
.ev-holiday {
  font-size: 12px;
  font-weight: 600;
  color: #f0a500;
}
.ev-chanukah {
  font-size: 12px;
  font-weight: 600;
  color: #e8a020;
}
.ev-havdalah {
  font-size: 12px;
  font-weight: 600;
  color: #9c6fde;
}
.ev-candle {
  font-size: 12px;
  font-weight: 600;
  color: var(--accent-color, #0078d4);
}
.ev-omer {
  font-size: 12px;
  color: var(--text-secondary);
}
.ev-mevarchim {
  font-size: 12px;
  color: var(--text-secondary);
  font-style: italic;
}
.ev-molad {
  font-size: 12px;
  color: var(--text-secondary);
}
.ev-fast {
  font-size: 12px;
  font-weight: 600;
  color: var(--text-secondary);
}

/* ── Pills ── */
.pill {
  font-size: 11px;
  font-weight: 600;
  color: var(--accent-color, #0078d4);
  opacity: 0.75;
  border-radius: 4px;
  padding: 2px 5px;
  cursor: pointer;
  -webkit-tap-highlight-color: transparent;
  white-space: nowrap;
}
.pill:hover,
.pill.active {
  opacity: 1;
  background: color-mix(in srgb, var(--accent-color, #0078d4) 10%, transparent);
}
.pill--green {
  color: #3a9e6e;
}
.pill--green:hover,
.pill--green.active {
  background: color-mix(in srgb, #3a9e6e 10%, transparent);
}

/* ── Expand panels ── */
.panel {
  padding: 6px 14px 10px;
  border-top: 1px solid color-mix(in srgb, var(--border-color) 40%, transparent);
}
.warning {
  font-size: 11px;
  font-weight: 600;
  color: #c8a000;
  background: color-mix(in srgb, #f0a500 12%, transparent);
  border: 1px solid color-mix(in srgb, #f0a500 40%, transparent);
  border-radius: 4px;
  padding: 5px 10px;
  margin-bottom: 8px;
  text-align: center;
  line-height: 1.4;
}
.extra {
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding-bottom: 6px;
  margin-bottom: 4px;
  border-bottom: 1px solid color-mix(in srgb, var(--border-color) 40%, transparent);
  font-size: 12px;
}
.grid {
  display: grid;
  grid-template-columns: 1fr auto;
}
.grid-label {
  font-size: 12px;
  color: var(--text-secondary);
  padding: 3px 0;
  border-bottom: 1px solid color-mix(in srgb, var(--border-color) 20%, transparent);
}
.grid-value {
  font-size: 12px;
  font-weight: 600;
  color: var(--text-primary);
  font-variant-numeric: tabular-nums;
  padding: 3px 0;
  border-bottom: 1px solid color-mix(in srgb, var(--border-color) 20%, transparent);
  text-align: start;
  padding-inline-start: 12px;
}
</style>
