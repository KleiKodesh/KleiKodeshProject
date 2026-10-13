<script setup lang="ts">
import { ref, computed, watch, nextTick } from 'vue'
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
import { dateInfo, loadDateInfo } from './homeDateInfo'
import { useHomeDailyLearningEntries, type HomeDailyLearningEntry } from './useHomeDailyLearning'

const paneNavigation = usePaneNavigation()
const { showClock } = storeToRefs(useSettingsStore())

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
  if (total < 60) return `בעוד ${total} דקות`
  const hours = Math.floor(total / 60)
  const minutes = total % 60
  const hoursText = hours === 1 ? 'שעה' : hours === 2 ? 'שעתיים' : `${hours} שעות`
  if (minutes === 0) return `בעוד ${hoursText}`
  return `בעוד ${hoursText} ו-${minutes} דקות`
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

// Fixed-position anchor: centered over the trigger, then clamped so it never
// escapes the viewport, and pinned just above the bottom bar.
const ZMAN_POPUP_MARGIN = 8
const zmanPopupLeft = ref(0)
const zmanPopupBottom = ref(0)
const zmanPopupMaxHeight = ref(0)
const zmanPopupStyle = computed(() => ({
  left: `${zmanPopupLeft.value}px`,
  bottom: `${zmanPopupBottom.value}px`,
  maxHeight: `${zmanPopupMaxHeight.value}px`,
}))

interface BarPopupAnchor {
  left: number
  bottom: number
  maxHeight: number
}

function computeBarPopupAnchor(button: HTMLElement, popupWidth: number): BarPopupAnchor {
  const rect = button.getBoundingClientRect()
  const center = rect.left + rect.width / 2
  let left = center - popupWidth / 2
  const maxLeft = window.innerWidth - popupWidth - ZMAN_POPUP_MARGIN
  left = Math.min(Math.max(left, ZMAN_POPUP_MARGIN), Math.max(ZMAN_POPUP_MARGIN, maxLeft))
  return {
    left,
    bottom: window.innerHeight - rect.top + ZMAN_POPUP_MARGIN,
    // The popup opens upward from just above the bar; cap its height to the space
    // between the top margin and the bar so it never overflows the top.
    maxHeight: Math.max(120, rect.top - ZMAN_POPUP_MARGIN * 2),
  }
}

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
  { ignore: [zmanPopupEl] },
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

function openHebrewCalendar() {
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
  learningMenuAnchor.value = computeBarPopupAnchor(button, learningMenuRef.value?.offsetWidth || 240)
}

const learningCloser = useDropdownClose(
  learningBarItemRef,
  () => {
    isLearningOpen.value = false
  },
  { ignore: [learningMenuRef] },
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
  <div ref="dateBarRef" class="date-bar">
    <template v-if="showClockInBar">
      <span class="bar-item bar-clock">{{ clockTime }}</span>
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
          :title="`${nextZmanCountdown} · לחץ לכל הזמנים`"
          @click="toggleZmanPopup"
        >
          <span class="bar-lbl">{{ nextZman.label }}:</span> {{ nextZmanTime }}
        </button>
      </div>
      <Teleport to="body">
        <div v-if="isZmanPopupOpen" class="zman-popup-anchor" :style="zmanPopupStyle">
          <NextZmanPopup ref="zmanPopupRef" :rows="zmanRows" :city-name="zmanCity.name" />
        </div>
      </Teleport>
      <span class="bar-sep">·</span>
    </template>
    <button class="bar-item bar-item--btn" @click="openHebrewCalendar">
      {{ dateInfo.hebrewDate }}
    </button>
    <template v-if="dailyLearningEntries.length">
      <span class="bar-sep">·</span>
      <div ref="learningBarItemRef" class="learning-wrap">
        <button
          ref="learningButtonRef"
          class="bar-item bar-item--btn learning-btn"
          :class="{ on: isLearningOpen }"
          @click="toggleLearningMenu"
        >
          לימוד יומי
        </button>
      </div>
      <Teleport to="body">
        <div
          v-if="isLearningOpen"
          ref="learningMenuRef"
          class="learning-menu"
          :style="learningMenuStyle"
          dir="rtl"
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
            :title="entry.open ? undefined : 'הספר אינו בספרייה'"
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
  gap: 6px;
  padding: 8px 16px;
  font-size: 11.5px;
  color: var(--text-secondary);
  overflow: hidden;
  white-space: nowrap;
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
.bar-clock {
  font-variant-numeric: tabular-nums;
  letter-spacing: 0.03em;
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
.learning-menu {
  position: fixed;
  z-index: 200;
  min-width: 240px;
  overflow-y: auto;
  padding: 4px;
  display: flex;
  flex-direction: column;
  background: var(--bg-primary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.18);
}
.learning-menu-item {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 2px;
  padding: 6px 10px;
  background: none;
  border: none;
  border-radius: 6px;
  font-family: inherit;
  text-align: start;
  cursor: pointer;
  color: var(--text-primary);
}
.learning-menu {
  outline: none;
}
.learning-menu-item:disabled {
  cursor: default;
  opacity: 0.5;
}
.learning-menu-item:not(:disabled):hover,
.learning-menu-item.focused {
  background: var(--hover-bg);
}
.learning-menu-label {
  font-size: 12.5px;
  font-weight: 600;
}
.learning-menu-path {
  font-size: 11px;
  color: var(--text-secondary);
}
.zman-popup-anchor {
  position: fixed;
  z-index: 200;
}
</style>
