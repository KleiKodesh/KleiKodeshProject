<script setup lang="ts">
import type { CalendarDay } from './calendarTypes'
import type { DailyLearning } from '@/features/hebrew-calendar/hebrewCalendarLearning'

const props = defineProps<{
  day: CalendarDay
  zmanimOpen: boolean
  learningOpen: boolean
}>()

defineEmits<{
  (e: 'toggle-zmanim'): void
  (e: 'toggle-learning'): void
}>()

const DAY_ABBR = ['SUN', 'MON', 'TUE', 'WED', 'THU', 'FRI', 'SAT']

const ZMANIM_ROWS: Array<{ key: keyof CalendarDay['zmanim']; label: string }> = [
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

const LEARNING_ROWS: Array<{ key: keyof DailyLearning; label: string }> = [
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

const hasLearning = LEARNING_ROWS.some((r) => props.day.learning[r.key])
</script>

<template>
  <div
    class="day-row"
    :class="{
      today: day.isToday,
      shabbat: day.isShabbat,
      friday: day.isFriday,
      holiday: day.holidays.length > 0,
    }"
  >
    <!-- ── Summary row ── -->
    <div class="summary">
      <!-- Hebrew date — physical RIGHT -->
      <div class="date heb">
        <span class="day-name">{{ day.hebDayName }}</span>
        <span class="day-num" :class="{ accent: day.isShabbat && !day.isToday }">{{
          day.hebGem
        }}</span>
      </div>

      <!-- Center: events + action pills -->
      <div class="center">
        <span v-if="day.parasha" class="pill-dummy">{{ day.parasha }}</span>
        <template v-for="(h, i) in day.holidays" :key="h">
          <span class="ev holiday">{{ h }}</span>
        </template>
        <span v-if="day.chanukahCandles" class="ev chanukah">{{ day.chanukahCandles }}</span>
        <button class="pill" :class="{ on: zmanimOpen }" @click="$emit('toggle-zmanim')">
          זמני היום
        </button>
        <button
          v-if="hasLearning"
          class="pill green"
          :class="{ on: learningOpen }"
          @click="$emit('toggle-learning')"
        >
          לימוד יומי
        </button>
      </div>

      <!-- Gregorian date — physical LEFT -->
      <div class="date greg">
        <span class="day-name">{{ DAY_ABBR[day.dayOfWeek] }}</span>
        <span class="day-num" :class="{ accent: day.isToday || day.isShabbat }">{{
          day.gregDay
        }}</span>
      </div>
    </div>

    <!-- ── Zmanim panel ── -->
    <div v-if="zmanimOpen" class="panel">
      <p class="warning">⚠️ אין לסמוך על הזמנים כלל! הזמנים שונים מהותית מהלוח ״עיתים לבינה״!</p>
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
        class="extras"
      >
        <span v-if="day.havdalah" class="ev havdalah">צאת שבת: {{ day.havdalah }}</span>
        <span v-if="day.candleLighting" class="ev candle"
          >הדלקת נרות: {{ day.candleLighting }}</span
        >
        <span v-if="day.omer" class="ev muted">{{ day.omer }}</span>
        <span v-if="day.yomKippurKatan" class="ev holiday">{{ day.yomKippurKatan }}</span>
        <span v-if="day.fastStart" class="ev muted">{{ day.fastStart }}</span>
        <span v-if="day.fastEnd" class="ev muted">{{ day.fastEnd }}</span>
        <span v-if="day.shabbatMevarchim" class="ev muted italic">{{ day.shabbatMevarchim }}</span>
        <span v-if="day.molad" class="ev muted">{{ day.molad }}</span>
      </div>
      <div class="zmanim-grid">
        <template v-for="z in ZMANIM_ROWS" :key="z.key">
          <span class="zg-label">{{ z.label }}</span>
          <span class="zg-value">{{ day.zmanim[z.key] ?? '—' }}</span>
        </template>
      </div>
    </div>

    <!-- ── Learning panel ── -->
    <div v-if="learningOpen" class="panel">
      <div class="data-grid">
        <template v-for="r in LEARNING_ROWS" :key="r.key">
          <div v-if="day.learning[r.key]" class="data-row">
            <span class="gl">{{ r.label }}</span
            ><span class="gv">{{ day.learning[r.key] }}</span>
          </div>
        </template>
      </div>
    </div>
  </div>
</template>

<style scoped>
/* ── Row shell ── */
.day-row {
  display: flex;
  flex-direction: column;
  overflow: hidden;
  border-bottom: 1px dashed color-mix(in srgb, var(--border-color) 60%, transparent);
}
.day-row.shabbat,
.day-row.friday {
  background: color-mix(in srgb, var(--text-secondary) 5%, transparent);
}
.day-row.holiday {
  background: color-mix(in srgb, #f0a500 6%, transparent);
}
.day-row.today {
  border-inline-start: 3px solid var(--accent-color, #0078d4);
}

/* ── Summary ── */
.summary {
  display: flex;
  align-items: stretch;
  min-height: 0;
  flex: 1;
  padding-inline: 8px;
}

/* ── Date columns ── */
.date {
  display: flex;
  flex-direction: column;
  justify-content: center;
  flex-shrink: 0;
  width: 52px;
  padding: 2px 4px;
  overflow: hidden;
}
.date.heb {
  align-items: flex-start;
}
.date.greg {
  align-items: flex-end;
}

.day-name {
  font-size: 9px;
  font-weight: 700;
  color: var(--text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.04em;
  line-height: 1;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: clip;
}
.day-num {
  font-size: 26px;
  font-weight: 300;
  line-height: 1;
  color: var(--text-primary);
  white-space: nowrap;
}
.day-num.accent {
  color: var(--accent-color, #0078d4);
  font-weight: 500;
}

/* ── Center ── */
.center {
  flex: 1;
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: center;
  gap: 3px;
  padding: 4px 6px;
  overflow: hidden;
  min-width: 0;
}

/* ── Event text ── */
.sep {
  color: var(--text-secondary);
  opacity: 0.4;
  font-size: 11px;
}
.ev {
  font-size: 11px;
  line-height: 1;
  display: inline-block;
}
.holiday {
  font-weight: 600;
  color: #f0a500;
}
.chanukah {
  font-weight: 600;
  color: #e8a020;
}
.havdalah {
  font-weight: 600;
  color: #9c6fde;
}
.candle {
  font-weight: 600;
  color: var(--accent-color, #0078d4);
}
.muted {
  color: var(--text-secondary);
}
.italic {
  font-style: italic;
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
  white-space: nowrap;
  -webkit-tap-highlight-color: transparent;
  line-height: 1;
  display: inline-block;
  font-variant-numeric: normal;
}
.pill:hover,
.pill.on {
  opacity: 1;
  background: color-mix(in srgb, var(--accent-color, #0078d4) 10%, transparent);
}
.pill.green {
  color: #3a9e6e;
}
.pill.green:hover,
.pill.green.on {
  background: color-mix(in srgb, #3a9e6e 10%, transparent);
}

/* ── Dummy pill (non-interactive) ── */
.pill-dummy {
  font-size: 10px;
  font-weight: 700;
  color: var(--text-secondary);
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
  margin: 0 0 8px;
  text-align: center;
  line-height: 1.4;
}
.extras {
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding-bottom: 6px;
  margin-bottom: 6px;
  border-bottom: 1px solid color-mix(in srgb, var(--border-color) 40%, transparent);
  font-size: 12px;
}
.zmanim-grid {
  display: grid;
  grid-template-columns: 1fr auto;
}
.zg-label {
  font-size: 12px;
  color: var(--text-secondary);
  padding: 3px 0;
  border-bottom: 1px solid color-mix(in srgb, var(--border-color) 20%, transparent);
}
.zg-value {
  font-size: 12px;
  font-weight: 600;
  color: var(--text-primary);
  font-variant-numeric: tabular-nums;
  padding: 3px 0 3px 16px;
  border-bottom: 1px solid color-mix(in srgb, var(--border-color) 20%, transparent);
  text-align: start;
}

.data-grid {
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.data-row {
  line-height: 1.5;
}
.gl {
  font-size: 12px;
  font-weight: 600;
  color: var(--text-primary);
}
.gl::after {
  content: '\00a0';
}
.gv {
  font-size: 12px;
  color: var(--text-secondary);
  font-variant-numeric: tabular-nums;
}
</style>
