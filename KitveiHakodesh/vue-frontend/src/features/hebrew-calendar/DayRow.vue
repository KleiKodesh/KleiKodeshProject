<script setup lang="ts">
import { useI18n } from 'vue-i18n'
const { t } = useI18n()
import type { CalendarDay } from './calendarTypes'
import type { DailyLearning } from '@/features/hebrew-calendar/hebrewCalendarLearning'
import type { HomeDailyLearningItem } from '@/features/home/homeDailyLearningTypes'
import { useDailyLearningOpener } from '@/features/home/useHomeDailyLearning'
import { wantsNewTab } from '@/composables/useOpenInNewTab'

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
  // chatzotNight of date D is the midpoint of the night ending at D's sunrise;
  // in winter it can fall before civil midnight, on date D-1.
  { key: 'chatzotNight', label: t('hebrewCalendar.zmanim.chatzotNight') },
  { key: 'alot', label: t('hebrewCalendar.zmanim.alot') },
  { key: 'misheyakir', label: t('hebrewCalendar.zmanim.misheyakir') },
  { key: 'sunrise', label: t('hebrewCalendar.zmanim.sunrise') },
  // MGA's day starts at alot, so each MGA deadline lands before its GRA
  // counterpart (shma-MGA → shma-GRA → tfilla-MGA → tfilla-GRA).
  { key: 'sofShmaMga', label: t('hebrewCalendar.zmanim.sofShmaMga') },
  { key: 'sofShmaGra', label: t('hebrewCalendar.zmanim.sofShmaGra') },
  { key: 'sofTfillaMga', label: t('hebrewCalendar.zmanim.sofTfillaMga') },
  { key: 'sofTfillaGra', label: t('hebrewCalendar.zmanim.sofTfillaGra') },
  { key: 'chatzot', label: t('hebrewCalendar.zmanim.chatzot') },
  { key: 'minchaGedola', label: t('hebrewCalendar.zmanim.minchaGedola') },
  { key: 'minchaKetana', label: t('hebrewCalendar.zmanim.minchaKetana') },
  { key: 'plag', label: t('hebrewCalendar.zmanim.plag') },
  { key: 'sunset', label: t('hebrewCalendar.zmanim.sunset') },
  { key: 'tzeit', label: t('hebrewCalendar.zmanim.tzeit') },
]

// `homeKey` is the same cycle in the home date-bar dropdown, which knows how to open it.
const LEARNING_ROWS: Array<{ key: keyof DailyLearning; homeKey: HomeDailyLearningItem['key']; label: string }> = [
  { key: 'dafYomi', homeKey: 'daf-yomi', label: t('hebrewCalendar.learning.dafYomi') },
  { key: 'dirshuAmudYomi', homeKey: 'amud-yomi', label: t('hebrewCalendar.learning.dirshuAmudYomi') },
  { key: 'yerushalmiVilna', homeKey: 'yerushalmi-yomi', label: t('hebrewCalendar.learning.yerushalmiVilna') },
  { key: 'mishnaYomi', homeKey: 'mishnah-yomit', label: t('hebrewCalendar.learning.mishnaYomi') },
  { key: 'nachYomi', homeKey: 'nach-yomi', label: t('hebrewCalendar.learning.nachYomi') },
  { key: 'perekYomi', homeKey: 'perek-yomi', label: t('hebrewCalendar.learning.perekYomi') },
  { key: 'rambam1', homeKey: 'rambam-1', label: t('hebrewCalendar.learning.rambam1') },
  { key: 'rambam3', homeKey: 'rambam-3', label: t('hebrewCalendar.learning.rambam3') },
  { key: 'kitzurShulchanAruch', homeKey: 'kitzur-shulchan-aruch', label: t('hebrewCalendar.learning.kitzurShulchanAruch') },
  { key: 'chofetzChaim', homeKey: 'chofetz-chaim', label: t('hebrewCalendar.learning.chofetzChaim') },
  { key: 'psalms', homeKey: 'daily-psalms', label: t('hebrewCalendar.learning.psalms') },
]

const openLearning = useDailyLearningOpener()

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
        <span v-if="day.parasha" class="chip parasha">{{ day.parasha }}</span>
        <template v-for="(h, i) in day.holidays" :key="h">
          <span class="chip holiday">{{ h }}</span>
        </template>
        <span v-if="day.chanukahCandles" class="chip chanukah">{{ day.chanukahCandles }}</span>
        <button class="chip pill" :class="{ on: zmanimOpen }" @click="$emit('toggle-zmanim')">
          {{ $t('hebrewCalendar.day.zmanim') }}
        </button>
        <button
          v-if="hasLearning"
          class="chip pill green"
          :class="{ on: learningOpen }"
          @click="$emit('toggle-learning')"
        >
          {{ $t('hebrewCalendar.day.learning') }}
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
        <span v-if="day.havdalah" class="ev havdalah">{{ $t('hebrewCalendar.day.havdalah', { time: day.havdalah }) }}</span>
        <span v-if="day.candleLighting" class="ev candle"
          >{{ $t('hebrewCalendar.day.candleLighting', { time: day.candleLighting }) }}</span
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
          <button
            v-if="day.learning[r.key]"
            class="data-row"
            @click="openLearning(day.date, r.homeKey, wantsNewTab($event))"
            @auxclick.middle.prevent="openLearning(day.date, r.homeKey, true)"
          >
            <span class="gl">{{ r.label }}</span
            ><span class="gv">{{ day.learning[r.key] }}</span>
          </button>
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
  background: color-mix(in srgb, var(--status-warning) 6%, transparent);
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
/* English (LTR) reads Gregorian first: it takes the inline-start side. */
:root[dir='ltr'] .date.greg {
  order: -1;
  align-items: flex-start;
}
:root[dir='ltr'] .date.heb {
  order: 1;
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

/* ── Event text (panel rows) ── */
.ev {
  font-size: 11px;
  line-height: 1;
  display: inline-block;
}
.ev.holiday {
  font-weight: 600;
  color: var(--status-warning);
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

/* ── Center chips — one shared box; variants set only colour + hover ── */
.chip {
  font-size: 11px;
  font-weight: 600;
  line-height: 1;
  padding: 2px 5px;
  border-radius: 4px;
  white-space: nowrap;
  display: inline-block;
  font-variant-numeric: normal;
}
button.chip {
  cursor: pointer;
  -webkit-tap-highlight-color: transparent;
}

/* colour variants */
.chip.parasha {
  color: var(--text-secondary);
}
.chip.holiday {
  color: var(--status-warning);
}
.chip.chanukah {
  color: #e8a020;
}
.chip.pill {
  color: var(--accent-color, #0078d4);
}
.chip.pill.green {
  color: #3a9e6e;
}

/* interactive-only hover / active */
.chip.pill:hover,
.chip.pill.on {
  background: color-mix(in srgb, var(--accent-color, #0078d4) 10%, transparent);
}
.chip.pill.green:hover,
.chip.pill.green.on {
  background: color-mix(in srgb, #3a9e6e 10%, transparent);
}

/* ── Expand panels ── */
.panel {
  padding: 6px 14px 10px;
  border-top: 1px solid color-mix(in srgb, var(--border-color) 40%, transparent);
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
  all: unset;
  line-height: 1.5;
  cursor: pointer;
  border-radius: 4px;
  padding-inline: 4px;
  text-align: start;
}
.data-row:hover,
.data-row:focus-visible {
  background: color-mix(in srgb, #3a9e6e 10%, transparent);
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
