<script setup lang="ts">
import { computed, inject, ref, shallowRef, watch } from 'vue'
import { t } from '@/i18n'
import { dbReady } from '@/webview-host/seforimDb'
import HebrewDayNav from '@/components/HebrewDayNav.vue'
import ExcelDocumentGlyph from '@/components/ExcelDocumentGlyph.vue'
import {
  IconArrowRepeatAll20Regular, IconArrowRepeatAllOff20Regular, IconCheckmarkCircle20Filled, IconCheckmarkCircle20Regular,
  IconFolderArrowRight20Regular, IconFolderOpen20Regular, IconSubtract16Regular,
} from '@iconify-prerendered/vue-fluent'
import { openFileInDefaultApp, revealFileInFolder } from '@/webview-host/bridge'
import { isoDay, useDailyLearningLog } from './dailyLearningLog'
import { lsGet, lsSet } from '@/utils/persistence'
import { wantsNewTab } from '@/composables/useOpenInNewTab'
import { DAILY_LEARNING_OPENER_KEY, useDailyLearningOpener } from '@/features/home/useHomeDailyLearning'
import { computeHomeDailyLearningItems } from '@/features/home/homeDailyLearningItems'
import type { HomeDailyLearningItem } from '@/features/home/homeDailyLearningTypes'
import { findBooksByName } from '@/webview-host/seforimApi'

// The standalone calendar has no library to look the books up in: it lists every cycle
// and hands the click to its opener (a link into the app). Inside the app, as before.
/** The day to list (the separate calendar app's picked day); today when absent. */
const props = defineProps<{ date?: Date | null }>()
const standaloneOpener = inject(DAILY_LEARNING_OPENER_KEY, null)
/** The day listed: the calendar's picked day, moved by the widget's own back/forward/today. */
const day = ref(props.date ?? new Date())
watch(() => props.date, (d) => { day.value = d ?? new Date() })

/**
 * Reviews in the "chavruta shas" way: each portion again 1, 8, 37 and 127 days after it was
 * learned (a day, a week, a month and three months after the time before). Daily cycles only:
 * a weekly one has no portion of its own on those days.
 */
const REVIEW_DAYS_BACK = [1, 8, 37, 127] as const
const WEEKLY_KEYS = new Set<string>(['weekly-parsha', 'pirkei-avot', 'daf-weekly'])
const SHOW_REVIEWS_KEY = 'widgets.dailyLearning.showReviews'
/** Reviews are off until the user turns them on (remembered). */
const showReviews = ref(lsGet<boolean>(SHOW_REVIEWS_KEY) ?? false)
watch(showReviews, (on) => lsSet(SHOW_REVIEWS_KEY, on))

const daysBack = (d: Date, n: number) => { const x = new Date(d); x.setDate(x.getDate() - n); return x }

/** One line of the list: a cycle's portion of the shown day, or one of its reviews. */
interface LearningLine {
  id: string
  /** The cycle's label: the log's "cycle" column, so a review counts on its own portion. */
  label: string
  /** "Review 1".."Review 4"; null on the day's own portion. */
  reviewLabel: string | null
  /** 0 = the day's own portion; 1..4 = that review of an earlier portion (its own count). */
  review: number
  location: string
  /** The day the portion belongs to (yyyy-MM-dd): marks go on that day's row. */
  date: string
  open: ((openInNewTab: boolean) => void) | null
}

// The shown day's cycles (and their reviews), opened through the opener. Inside the app, lines
// whose book is not in the library are greyed out (a failed lookup is not "missing": clickable).
const opener = useDailyLearningOpener()
const lines = shallowRef<LearningLine[]>([])
let generation = 0
watch([() => day.value.toDateString(), showReviews, dbReady], async () => {
  const mine = ++generation // also drops any lookup still running for the previous day
  const shown = day.value
  const reviewDates = showReviews.value ? REVIEW_DAYS_BACK.map((n) => daysBack(shown, n)) : []
  const [items, ...reviewItems] = await Promise.all([shown, ...reviewDates].map((d) => computeHomeDailyLearningItems(d)))
  if (mine !== generation) return // a later day was picked meanwhile

  const sources: Array<{ item: HomeDailyLearningItem; when: Date; reviewLabel: string | null; review: number }> = []
  for (const item of items!) {
    sources.push({ item, when: shown, reviewLabel: null, review: 0 })
    if (WEEKLY_KEYS.has(item.key)) continue
    reviewItems.forEach((past, i) => {
      const earlier = past.find((p) => p.key === item.key)
      if (earlier) sources.push({ item: earlier, when: reviewDates[i]!, reviewLabel: t(`home.learningLog.review${i + 1}`), review: i + 1 })
    })
  }
  const lineOf = ({ item, when, reviewLabel, review }: (typeof sources)[number], open: boolean): LearningLine => ({
    id: `${item.key}@${isoDay(when)}`,
    label: item.label,
    reviewLabel,
    review,
    location: item.location,
    date: isoDay(when),
    open: open ? (openInNewTab: boolean) => opener(when, item.key, openInNewTab) : null,
  })
  // Shown at once; the book lookup only greys out lines whose book comes back "missing".
  lines.value = sources.map((s) => lineOf(s, true))
  if (standaloneOpener) return
  // One service call for every line: matched in Core against its cached title index.
  void findBooksByName(sources.map((s) => s.item.target.books)).then((ids) => {
    if (mine === generation && ids.includes(0)) lines.value = sources.map((s, i) => lineOf(s, ids[i] !== 0))
  }, () => {}) // no answer: everything stays clickable
}, { immediate: true })

// Marking needs the app's service; the standalone calendar has none. Only the days on screen are read.
const loggedDays = computed(() => [day.value, ...(showReviews.value ? REVIEW_DAYS_BACK.map((n) => daysBack(day.value, n)) : [])].map(isoDay))
const log = standaloneOpener ? null : useDailyLearningLog(loggedDays)
</script>

<template>
  <div class="learning-widget">
    <!-- Stays at the top while the list scrolls: the day, then the widget's own buttons. -->
    <HebrewDayNav v-model="day" class="learning-top">
      <button
        class="mark-btn"
        type="button"
        :aria-pressed="showReviews"
        :title="$t(showReviews ? 'home.learningLog.hideReviews' : 'home.learningLog.showReviews')"
        @click="showReviews = !showReviews"
      ><IconArrowRepeatAllOff20Regular v-if="showReviews" /><IconArrowRepeatAll20Regular v-else /></button>
      <template v-if="log">
        <template v-if="log.path.value">
          <button class="mark-btn" type="button" :title="$t('home.learningLog.openExcel')" @click="openFileInDefaultApp(log.path.value)"><ExcelDocumentGlyph /></button>
          <button class="mark-btn" type="button" :title="$t('home.learningLog.showInFolder')" @click="revealFileInFolder(log.path.value)"><IconFolderOpen20Regular /></button>
        </template>
        <button class="mark-btn" type="button" :title="$t(log.path.value ? 'home.learningLog.move' : 'home.learningLog.pickFolder')" @click="log.chooseFolder()"><IconFolderArrowRight20Regular /></button>
      </template>
    </HebrewDayNav>
    <div v-for="line in lines" :key="line.id" class="learning-line" :class="{ review: line.reviewLabel }">
      <!-- A review keeps its own count, on the row of the portion it repeats (that portion's day). -->
      <template v-if="log">
        <button
          class="mark-btn"
          :class="{ done: log.timesOf(line.date, line.label, line.review) }"
          type="button"
          :title="$t('home.learningLog.mark')"
          @click="log.mark(line.date, line.label, line.location, line.review)"
        >
          <IconCheckmarkCircle20Filled v-if="log.timesOf(line.date, line.label, line.review)" /><IconCheckmarkCircle20Regular v-else />
          <span v-if="log.timesOf(line.date, line.label, line.review) > 1" class="mark-count">{{ log.timesOf(line.date, line.label, line.review) }}</span>
        </button>
        <button
          v-if="log.timesOf(line.date, line.label, line.review)"
          class="mark-btn unmark"
          type="button"
          :title="$t('home.learningLog.unmark')"
          @click="log.unmark(line.date, line.label, line.review)"
        ><IconSubtract16Regular /></button>
      </template>
      <button
        class="learning-row"
        :disabled="!line.open"
        :title="line.open ? undefined : $t('home.dateBar.bookNotInLibrary')"
        @click="line.open?.(wantsNewTab($event))"
      >
        <span class="learning-label">{{ line.reviewLabel ?? line.label }}</span>
        <span class="learning-path">{{ line.location }}</span>
      </button>
    </div>
    <p v-if="log" class="log-caption" :title="log.path.value">{{ log.path.value ? $t('home.learningLog.excelHint') : $t('home.learningLog.noFile') }}</p>
  </div>
</template>

<style scoped>
.learning-widget { display: flex; flex-direction: column; padding: 6px; }
.learning-line { display: flex; align-items: center; gap: 2px; }
/* A review sits under its cycle, a step in, quieter. */
.learning-line.review { padding-inline-start: 16px; }
.learning-line.review .learning-label { font-weight: 400; color: var(--text-secondary); }
.learning-row {
  flex: 1;
  min-width: 0;
  display: flex;
  justify-content: space-between;
  gap: 16px;
  padding: 6px 8px;
  background: none;
  border: none;
  border-radius: 4px;
  font-size: 13px;
  text-align: start;
  cursor: pointer;
  color: var(--text-primary);
}
.learning-row:hover:not(:disabled) { background: color-mix(in srgb, var(--text-primary) 6%, transparent); }
.learning-row:disabled { opacity: 0.45; cursor: default; }
.learning-label { font-weight: 600; }
.learning-path { color: var(--text-secondary); }
.mark-btn {
  flex: none; display: inline-flex; align-items: center; gap: 1px;
  height: 26px; min-width: 26px; padding: 0 3px; justify-content: center;
  background: none; border: 1px solid transparent; border-radius: 4px;
  color: var(--text-secondary); cursor: pointer;
}
.mark-btn:hover { color: var(--text-primary); border-color: var(--border-color); }
.mark-btn.done { color: #107c10; }
.mark-btn.unmark { min-width: 20px; }
.mark-count { font-size: 11px; font-weight: 700; }
/* The day bar and the caption stay in view; only the list between them scrolls (the widget window scrolls). */
.learning-top { position: sticky; top: 0; z-index: 1; margin: 0 -6px; padding: 4px 8px; background: var(--bg-primary); }
.log-caption {
  position: sticky; bottom: 0; margin: 6px -6px -6px; padding: 4px 14px;
  border-top: 1px solid var(--border-color); background: var(--bg-primary);
  font-size: 11px; color: var(--text-secondary); white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
}
</style>
