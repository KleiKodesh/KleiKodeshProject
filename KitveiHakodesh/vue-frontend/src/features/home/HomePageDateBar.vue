<script setup lang="ts">
import { t } from '@/i18n'
import { ref, computed, watch, nextTick, defineAsyncComponent } from 'vue'
import { useNow, useWindowSize } from '@vueuse/core'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { useListKeys } from '@/composables/useListKeyNav'
import { wantsNewTab } from '@/composables/useOpenInNewTab'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { useSettingsStore } from '@/stores/settingsStore'
import { storeToRefs } from 'pinia'
import NextZmanPopup from './NextZmanPopup.vue'
import { useNextZman } from './useNextZman'
import { useHomeDateBarFit } from './useHomeDateBarFit'
import { computeBarPopupAnchor, type BarPopupAnchor } from './homeBarPopupAnchor'
import HomeWorldClockDropdown from './HomeWorldClockDropdown.vue'
// Lazy — the month grid pulls in @hebcal/core, kept out of the home bundle.
const HomeCalendarPopup = defineAsyncComponent(() => import('./HomeCalendarPopup.vue'))
import { dateInfo, loadDateInfo } from './homeDateInfo'
import { useHomeDailyLearningEntries, type HomeDailyLearningEntry } from './useHomeDailyLearning'
import { cityDisplayName } from '@/features/hebrew-calendar/cityDisplayName'

const props = defineProps<{
  /** Rendered by App.vue as the window's status bar (pinned) rather than under the home page. */
  statusBar?: boolean
}>()

const paneNavigation = usePaneNavigation()
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
  rows: zmanRows,
  city: zmanCity,
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
// Keyboard nav walks only the rows that can open (their book is in the library).
const navigableLearningEntries = computed(() => dailyLearningEntries.value.filter((entry) => entry.open))

const dateBarRef = ref<HTMLElement | null>(null)
const hasNextZman = computed(() => !!nextZman.value)
const { showClockInBar, showZmanInBar, remeasure } = useHomeDateBarFit(
  dateBarRef,
  showBarClock,
  hasNextZman,
)

// Re-measure when the content itself changes (zman appears, learning loads, etc.).
watch([showBarClock, nextZman, () => dailyLearningEntries.value.length, clockTime], remeasure)

// ── Zmanim popup ──────────────────────────────────────────────────────────────

const isZmanPopupOpen = ref(false)
const zmanBarItemRef = ref<HTMLElement | null>(null)
const zmanButtonRef = ref<HTMLElement | null>(null)
const zmanPopupRef = ref<InstanceType<typeof NextZmanPopup> | null>(null)
const zmanPopupEl = computed<HTMLElement | null>(
  () => (zmanPopupRef.value?.$el as HTMLElement) ?? null,
)

const zmanPopupLeft = ref(0)
const zmanPopupBottom = ref(0)
const zmanPopupMaxHeight = ref(0)
const zmanPopupStyle = computed(() => ({
  left: `${zmanPopupLeft.value}px`,
  bottom: `${zmanPopupBottom.value}px`,
  maxHeight: `${zmanPopupMaxHeight.value}px`,
}))

function computeZmanPopupAnchor() {
  const button = zmanButtonRef.value
  if (!button) return
  // Best-effort width (falls back to the popup's min-width before it mounts).
  const anchor = computeBarPopupAnchor(button, zmanPopupEl.value?.offsetWidth || 220)
  zmanPopupLeft.value = anchor.left
  zmanPopupBottom.value = anchor.bottom
  zmanPopupMaxHeight.value = anchor.maxHeight
}

const zmanCloser = useDropdownClose(
  zmanBarItemRef,
  () => {
    isZmanPopupOpen.value = false
  },
  // The wrapper is the inline bar item; only the popup is fitted.
  { ignore: [zmanPopupEl], fitToViewport: false },
)

function toggleZmanPopup() {
  if (zmanCloser.justClosed.value) return
  const opening = !isZmanPopupOpen.value
  isZmanPopupOpen.value = opening
  if (opening) {
    computeZmanPopupAnchor()
    // Re-clamp once the popup has rendered and its real width is known.
    nextTick(computeZmanPopupAnchor)
  }
}

// ── Calendar popup ────────────────────────────────────────────────────────────

const isCalendarOpen = ref(false)
const calendarBarItemRef = ref<HTMLElement | null>(null)
const calendarButtonRef = ref<HTMLElement | null>(null)
const calendarPopupRef = ref<HTMLElement | null>(null)
const calendarAnchor = ref<BarPopupAnchor>({ left: 0, bottom: 0, maxHeight: 0 })
const calendarPopupStyle = computed(() => ({
  left: `${calendarAnchor.value.left}px`,
  bottom: `${calendarAnchor.value.bottom}px`,
  maxHeight: `${calendarAnchor.value.maxHeight}px`,
}))

function computeCalendarAnchor() {
  const button = calendarButtonRef.value
  if (!button) return
  calendarAnchor.value = computeBarPopupAnchor(button, calendarPopupRef.value?.offsetWidth || 440)
}

const calendarCloser = useDropdownClose(
  calendarBarItemRef,
  () => {
    isCalendarOpen.value = false
  },
  // The wrapper is the inline bar item; only the popup is fitted.
  { ignore: [calendarPopupRef], fitToViewport: false },
)

function toggleCalendarPopup() {
  if (calendarCloser.justClosed.value) return
  const opening = !isCalendarOpen.value
  isCalendarOpen.value = opening
  if (opening) {
    computeCalendarAnchor()
    nextTick(computeCalendarAnchor)
  }
}

function openHebrewCalendar() {
  isCalendarOpen.value = false
  paneNavigation.navigateToDestination('/hebrew-calendar')
}

// ── Daily learning dropdown ───────────────────────────────────────────────────

const isLearningOpen = ref(false)
const learningBarItemRef = ref<HTMLElement | null>(null)
const learningButtonRef = ref<HTMLElement | null>(null)
const learningMenuRef = ref<HTMLElement | null>(null)
const learningMenuAnchor = ref<BarPopupAnchor>({ left: 0, bottom: 0, maxHeight: 0 })
const learningMenuStyle = computed(() => ({
  left: `${learningMenuAnchor.value.left}px`,
  bottom: `${learningMenuAnchor.value.bottom}px`,
  maxHeight: `${learningMenuAnchor.value.maxHeight}px`,
}))

function computeLearningMenuAnchor() {
  const button = learningButtonRef.value
  if (!button) return
  learningMenuAnchor.value = computeBarPopupAnchor(button, learningMenuRef.value?.offsetWidth || 220)
}

const learningCloser = useDropdownClose(
  learningBarItemRef,
  () => {
    isLearningOpen.value = false
  },
  // The wrapper is the inline bar item; only the popup is fitted.
  { ignore: [learningMenuRef], fitToViewport: false },
)

function toggleLearningMenu() {
  if (learningCloser.justClosed.value) return
  const opening = !isLearningOpen.value
  isLearningOpen.value = opening
  if (opening) {
    learningFocusedIndex.value = 0
    computeLearningMenuAnchor()
    nextTick(() => {
      computeLearningMenuAnchor()
      learningMenuRef.value?.focus()
    })
  }
}

function openLearningItem(entry: HomeDailyLearningEntry, openInNewTab: boolean) {
  if (!entry.open) return
  isLearningOpen.value = false
  entry.open(openInNewTab)
}

// Arrow/Home/End/Enter (Ctrl+Enter = new tab) via the shared list-keys helper.
const { focusedIndex: learningFocusedIndex } = useListKeys(
  learningMenuRef,
  () => navigableLearningEntries.value.length,
  (index, openInNewTab) => {
    const entry = navigableLearningEntries.value[index]
    if (entry) openLearningItem(entry, openInNewTab)
  },
)

function onLearningMenuKeydown(event: KeyboardEvent) {
  if (event.code !== 'Escape') return
  event.preventDefault()
  isLearningOpen.value = false
  learningButtonRef.value?.focus()
}
</script>

<template>
  <div ref="dateBarRef" class="date-bar" :class="{ 'date-bar--status': props.statusBar }">
    <template v-if="showClockInBar">
      <HomeWorldClockDropdown :clock-time="clockTime" />
      <span class="bar-sep">·</span>
    </template>
    <template v-if="nextZman && showZmanInBar">
      <div ref="zmanBarItemRef" class="zman-wrap">
        <button
          ref="zmanButtonRef"
          class="bar-item bar-item--btn zman"
          :class="[
            `zman--${nextZman.urgency}`,
            { on: isZmanPopupOpen, 'zman--flash': nextZman.flash },
          ]"
          :title="$t('home.dateBar.nextZmanTitle', { countdown: nextZmanCountdown })"
          @click="toggleZmanPopup"
        >
          <span class="bar-lbl">{{ nextZman.label }}:</span> {{ nextZmanTime }}
        </button>
      </div>
      <Teleport to="body">
        <div v-if="isZmanPopupOpen" class="zman-popup-anchor" :style="zmanPopupStyle">
          <NextZmanPopup ref="zmanPopupRef" :rows="zmanRows" :city-name="cityDisplayName(zmanCity)" />
        </div>
      </Teleport>
      <span class="bar-sep">·</span>
    </template>
    <div ref="calendarBarItemRef" class="calendar-wrap">
      <button
        ref="calendarButtonRef"
        class="bar-item bar-item--btn calendar-btn"
        :class="{ on: isCalendarOpen }"
        :title="$t('home.dateBar.calendarTitle')"
        @click="toggleCalendarPopup"
      >
        {{ dateInfo.hebrewDate }}
      </button>
    </div>
    <Teleport to="body">
      <div
        v-if="isCalendarOpen"
        ref="calendarPopupRef"
        class="calendar-popup-anchor"
        :style="calendarPopupStyle"
      >
        <HomeCalendarPopup @open-full="openHebrewCalendar" />
      </div>
    </Teleport>
    <template v-if="dailyLearningEntries.length">
      <span class="bar-sep">·</span>
      <div ref="learningBarItemRef" class="learning-wrap">
        <button
          ref="learningButtonRef"
          class="bar-item bar-item--btn learning-btn"
          :class="{ on: isLearningOpen }"
          :title="$t('home.dateBar.dailyLearningTitle')"
          @click="toggleLearningMenu"
        >
          {{ $t('home.dateBar.dailyLearning') }}
        </button>
      </div>
      <Teleport to="body">
        <div
          v-if="isLearningOpen"
          ref="learningMenuRef"
          class="learning-menu"
          :style="learningMenuStyle"
          tabindex="-1"
          @keydown="onLearningMenuKeydown"
        >
          <button
            v-for="entry in dailyLearningEntries"
            :key="entry.key"
            class="learning-menu-item"
            :class="{
              focused:
                !!entry.open && navigableLearningEntries.indexOf(entry) === learningFocusedIndex,
            }"
            :disabled="!entry.open"
            :data-nav-item="entry.open ? '' : undefined"
            :title="entry.open ? undefined : $t('home.dateBar.bookNotInLibrary')"
            tabindex="-1"
            @click="openLearningItem(entry, wantsNewTab($event))"
            @mouseenter="entry.open && (learningFocusedIndex = navigableLearningEntries.indexOf(entry))"
          >
            <span class="learning-menu-label">{{ entry.label }}</span>
            <span class="learning-menu-path">{{ entry.location }}</span>
          </button>
        </div>
      </Teleport>
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
/* Status-bar mode, after classic Outlook / Office: full width, short, two groups at the
   two edges (learning + date at the start, zman + clock + pin at the end), quiet grey
   regular text, no colours, no separators - the chrome surface is the only edge. Reordered with `order` so the
   home layout's markup stays as is. */
.date-bar--status {
  height: 20px;
  padding: 0 4px;
  gap: 0;
  justify-content: flex-start;
  font-size: 12px;
  background: var(--bg-secondary);
}
.date-bar--status .bar-sep {
  display: none;
}
.date-bar--status .learning-wrap { order: 1; }
.date-bar--status .calendar-wrap { order: 2; }
.date-bar--status .zman-wrap { order: 3; margin-inline-start: auto; }
.date-bar--status :deep(.world-clock-wrap) { order: 4; }
.date-bar--status:not(:has(.zman-wrap)) :deep(.world-clock-wrap) { margin-inline-start: auto; }
.date-bar--status .pin-btn {
  order: 5;
  position: static;
  transform: none;
  margin-inline-start: 2px;
}
.date-bar--status:not(:has(.zman-wrap, :deep(.world-clock-wrap))) .pin-btn { margin-inline-start: auto; }
.date-bar--status :deep(.bar-item--btn),
.date-bar--status .bar-lbl {
  font-weight: 400;
  color: var(--text-secondary);
}
.date-bar--status :deep(.bar-item--btn) {
  display: inline-flex;
  align-items: center;
  gap: 0.3em;
  height: 18px;
  line-height: 18px;
  padding: 0 8px;
  border-radius: 3px;
}
.date-bar--status :deep(.bar-item--btn:hover) {
  background: color-mix(in srgb, var(--text-primary) 5%, transparent);
  color: var(--text-primary);
}
.date-bar--status .zman .bar-lbl {
  color: inherit;
}
.date-bar--status .zman--flash {
  animation: none;
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
.learning-menu {
  position: fixed;
  z-index: 200;
  min-width: 220px;
  overflow-y: auto;
  padding: 6px 8px;
  display: flex;
  flex-direction: column;
  background: var(--bg-primary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  box-shadow: 0 6px 24px rgba(0, 0, 0, 0.28);
  outline: none;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
}
.learning-menu-item {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 16px;
  padding: 2px 4px;
  background: none;
  border: none;
  border-radius: 4px;
  font-family: inherit;
  font-size: 12px;
  line-height: 1.4;
  text-align: start;
  cursor: pointer;
  color: var(--text-primary);
  white-space: nowrap;
}
.learning-menu-item:disabled {
  cursor: default;
  opacity: 0.45;
}
.learning-menu-item:not(:disabled):hover,
.learning-menu-item.focused {
  background: var(--hover-bg);
}
.learning-menu-label {
  color: var(--text-secondary);
}
.learning-menu-path {
  font-weight: 600;
}
.zman-popup-anchor,
.calendar-popup-anchor {
  position: fixed;
  z-index: 200;
}
.calendar-popup-anchor {
  display: flex;
}
.calendar-wrap {
  position: relative;
  display: inline-flex;
}
.calendar-btn.on {
  color: var(--accent-color);
}
</style>
