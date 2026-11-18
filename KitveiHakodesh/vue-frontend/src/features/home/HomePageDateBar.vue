<script setup lang="ts">
import { useWidgetWindowStore } from '@/features/widgets/widgetWindowStore'
import { t } from '@/i18n'
import { ref, computed, watch } from 'vue'
import { useNow, useWindowSize } from '@vueuse/core'
import { useHomeDailyLearningEntries } from './useHomeDailyLearning'
import { useSettingsStore } from '@/stores/settingsStore'
import { storeToRefs } from 'pinia'
import { useNextZman } from './useNextZman'
import { useHomeDateBarFit } from './useHomeDateBarFit'
import { dateInfo, loadDateInfo } from './homeDateInfo'

const props = defineProps<{
  /** Rendered by App.vue as the window's status bar (pinned) rather than under the home page. */
  statusBar?: boolean
}>()

const { showClock, dateBarPinned } = storeToRefs(useSettingsStore())

// Current clock time. Hidden when the floating fullscreen ClockWidget (App.vue)
// is already showing it — i.e. showClock && fullscreen — so the time is never
// displayed twice.
const now = useNow({ interval: 10_000 })
const { height: windowHeight } = useWindowSize()
const isFullscreen = computed(() => windowHeight.value >= screen.height)
const clockTime = computed(() =>
  now.value.toLocaleTimeString('he-IL', { hour: '2-digit', minute: '2-digit', hour12: false }),
)
const showBarClock = computed(() => !(showClock.value && isFullscreen.value))

const {
  next: nextZman,
  displayTime: nextZmanTime,
  tzeit: zmanTzeit,
  now: zmanNow,
} = useNextZman()

// The Hebrew date/daf yomi rolls over at צאת הכוכבים, not civil midnight. Reuse
// the zmanim engine's tzeit (single source of truth) rather than recomputing:
// once `now` is past today's tzeit, render the *next* civil day. Reactive on
// tzeit + now, so it rolls over the moment we cross nightfall.
const dateReference = computed(() => {
  const reference = new Date(zmanNow.value)
  const tzeit = zmanTzeit.value
  if (tzeit && zmanNow.value.getTime() >= tzeit.getTime())
    reference.setDate(reference.getDate() + 1)
  return reference
})
// Key on the calendar day so the `now` tick doesn't re-render the date; only an
// actual day change (incl. crossing tzeit) reloads.
watch(() => dateReference.value.toDateString(), () => loadDateInfo(dateReference.value), {
  immediate: true,
})

// "בעוד ..." phrasing: minutes only under an hour, otherwise hours (+ minutes).
const nextZmanCountdown = computed(() => {
  const total = nextZman.value?.minutesUntil ?? 0
  if (total < 60) return t('home.dateBar.inMinutes', { n: total })
  const hours = Math.floor(total / 60)
  const minutes = total % 60
  const hoursText = hours === 1 ? t('home.dateBar.oneHour') : hours === 2 ? t('home.dateBar.twoHours') : t('home.dateBar.nHours', { n: hours })
  if (minutes === 0) return t('home.dateBar.inHours', { hours: hoursText })
  return t('home.dateBar.inHoursMinutes', { hours: hoursText, n: minutes })
})

const dailyLearningEntries = useHomeDailyLearningEntries()

const dateBarRef = ref<HTMLElement | null>(null)
const hasNextZman = computed(() => !!nextZman.value)
const { showClockInBar, showZmanInBar, remeasure } = useHomeDateBarFit(
  dateBarRef,
  showBarClock,
  hasNextZman,
)

// Re-measure when the content itself changes (zman appears, learning loads, etc.).
watch([showBarClock, nextZman, () => dailyLearningEntries.value.length, clockTime], remeasure)

// ── Widgets ─────────────────────────────────────────────────────────────────
// Each bar item toggles its widget window (features/widgets) instead of a popup.
const widgets = useWidgetWindowStore()
</script>

<template>
  <div ref="dateBarRef" class="date-bar" :class="{ 'date-bar--status': props.statusBar }">
    <template v-if="showClockInBar">
      <div class="world-clock-wrap">
        <button
          class="bar-item bar-item--btn bar-clock"
          :class="{ on: widgets.isOpen('worldClock') }"
          :title="$t('home.worldClock.title')"
          @click="widgets.toggle('worldClock')"
        >
          {{ clockTime }}
        </button>
      </div>
      <span class="bar-sep">·</span>
    </template>
    <template v-if="nextZman && showZmanInBar">
      <div class="zman-wrap">
        <button
          class="bar-item bar-item--btn zman"
          :class="[
            `zman--${nextZman.urgency}`,
            { on: widgets.isOpen('zmanim'), 'zman--flash': nextZman.flash },
          ]"
          :title="$t('home.dateBar.nextZmanTitle', { countdown: nextZmanCountdown })"
          @click="widgets.toggle('zmanim')"
        >
          <span class="bar-lbl">{{ nextZman.label }}:</span> {{ nextZmanTime }}
        </button>
      </div>
      <span class="bar-sep">·</span>
    </template>
    <div class="calendar-wrap">
      <button
        class="bar-item bar-item--btn calendar-btn"
        :class="{ on: widgets.isOpen('calendar') }"
        :title="$t('home.dateBar.calendarTitle')"
        @click="widgets.toggle('calendar')"
      >
        {{ dateInfo.hebrewDate }}
      </button>
    </div>
    <template v-if="dailyLearningEntries.length">
      <span class="bar-sep">·</span>
      <div class="learning-wrap">
        <button
          class="bar-item bar-item--btn learning-btn"
          :class="{ on: widgets.isOpen('learning') }"
          :title="$t('home.dateBar.dailyLearningTitle')"
          @click="widgets.toggle('learning')"
        >
          {{ $t('home.dateBar.dailyLearning') }}
        </button>
      </div>
    </template>
    <!-- Shown on hover; pinned, the bar is rendered by App.vue under every page. -->
    <button
      class="pin-btn"
      :class="{ on: dateBarPinned }"
      :title="dateBarPinned ? $t('home.dateBar.unpin') : $t('home.dateBar.pin')"
      @click="dateBarPinned = !dateBarPinned"
    >
      <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        <path d="M12 17v5" />
        <path d="M9 10.76V5h6v5.76l2 2.24v2H7v-2z" :fill="dateBarPinned ? 'currentColor' : 'none'" />
        <path d="M8 5h8" />
      </svg>
    </button>
  </div>
</template>

<style scoped>
/* Always a single line; items are dropped by priority (see useHomeDateBarFit)
   rather than wrapping. */
.date-bar {
  display: flex;
  flex-wrap: nowrap;
  align-items: center;
  justify-content: center;
  gap: 4px;
  padding: 6px 12px;
  font-size: 11.5px;
  color: var(--text-secondary);
  overflow: hidden;
  white-space: nowrap;
  position: relative;
}
.pin-btn {
  position: absolute;
  inset-inline-end: 8px;
  top: 50%;
  transform: translateY(-50%);
  display: flex;
  padding: 3px;
  border: none;
  border-radius: 4px;
  background: none;
  color: var(--text-secondary);
  cursor: pointer;
  opacity: 0;
  transition: opacity 120ms;
}
.date-bar:hover .pin-btn,
.pin-btn:focus-visible {
  opacity: 1;
}
.pin-btn:hover {
  background: var(--hover-bg);
  color: var(--text-primary);
}
/* Pinned (date-bar--status) it is the same bar, just shown under every page. */
.bar-clock {
  font-variant-numeric: tabular-nums;
  letter-spacing: 0.03em;
}
.world-clock-wrap {
  position: relative;
  display: inline-flex;
}
.bar-sep {
  color: var(--text-secondary);
  opacity: 0.7;
  font-weight: 700;
}
.bar-item {
  color: var(--text-primary);
  white-space: nowrap;
  font-weight: 600;
}
.bar-lbl {
  font-weight: 600;
  color: var(--text-primary);
}
.bar-item--btn {
  background: none;
  border: none;
  padding: 0;
  font-size: inherit;
  font-family: inherit;
  cursor: pointer;
  color: var(--text-primary);
  white-space: nowrap;
}
.bar-item--btn:hover {
  color: var(--accent-color);
}
.bar-item--btn:hover .bar-lbl {
  color: inherit;
  opacity: 1;
}

/* ── Next-zman color cue: warms up as the time approaches ── */
.zman--soon,
.zman--soon .bar-lbl {
  color: #d98324;
  opacity: 1;
}
.zman--imminent,
.zman--imminent .bar-lbl {
  color: var(--status-danger);
  opacity: 1;
  font-weight: 700;
}
/* Pulse is reserved for deadline-critical zmanim (see CRITICAL_KEYS). Other
   imminent zmanim still turn red above, just without the flashing. */
.zman--flash {
  animation: zman-pulse 1.6s ease-in-out infinite;
}
@keyframes zman-pulse {
  0%,
  100% {
    opacity: 1;
  }
  50% {
    opacity: 0.45;
  }
}
@media (prefers-reduced-motion: reduce) {
  .zman--flash {
    animation: none;
  }
}

/* ── Next-zman popup (all times) ── */
.zman-wrap {
  position: relative;
  display: inline-flex;
}
.zman.on {
  color: var(--accent-color);
}
.learning-wrap {
  position: relative;
  display: inline-flex;
}
.learning-btn.on {
  color: var(--accent-color);
}
/* Density matches NextZmanPopup (12px rows, 1.4 line-height, 2px row padding). */
.calendar-wrap {
  position: relative;
  display: inline-flex;
}
.calendar-btn.on {
  color: var(--accent-color);
}
</style>
