<script setup lang="ts">
import { ref, computed, watch, nextTick } from 'vue'
import { useNow, useWindowSize } from '@vueuse/core'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { useSettingsStore } from '@/stores/settingsStore'
import { dbReady } from '@/webview-host/seforimDb'
import { storeToRefs } from 'pinia'
import NextZmanPopup from './NextZmanPopup.vue'
import { useNextZman } from './useNextZman'
import { useHomeDateBarFit } from './useHomeDateBarFit'
import { dateInfo, loadDateInfo } from './homeDateInfo'
import { navigateToDafYomi } from './dafYomiNavigation'

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

const dateBarRef = ref<HTMLElement | null>(null)
const hasNextZman = computed(() => !!nextZman.value)
const { showClockInBar, showZmanInBar, remeasure } = useHomeDateBarFit(
  dateBarRef,
  showBarClock,
  hasNextZman,
)

// Re-measure when the content itself changes (zman appears, daf loads, etc.).
watch([showBarClock, nextZman, () => dateInfo.value.dafYomi, clockTime], remeasure)

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

function computeZmanPopupAnchor() {
  const button = zmanButtonRef.value
  if (!button) return
  const rect = button.getBoundingClientRect()
  // Best-effort width (falls back to the popup's min-width before it mounts).
  const width = zmanPopupEl.value?.offsetWidth || 220
  const center = rect.left + rect.width / 2
  let left = center - width / 2
  const maxLeft = window.innerWidth - width - ZMAN_POPUP_MARGIN
  left = Math.min(Math.max(left, ZMAN_POPUP_MARGIN), Math.max(ZMAN_POPUP_MARGIN, maxLeft))
  zmanPopupLeft.value = left
  zmanPopupBottom.value = window.innerHeight - rect.top + ZMAN_POPUP_MARGIN
  // The popup opens upward from just above the bar; cap its height to the space
  // between the top margin and the bar so it never overflows the top.
  zmanPopupMaxHeight.value = Math.max(120, rect.top - ZMAN_POPUP_MARGIN * 2)
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

function openDafYomi() {
  // The button only renders when dafYomi is set, but narrow explicitly rather
  // than relying on the template guard.
  const dafYomi = dateInfo.value.dafYomi
  if (!dafYomi) return
  navigateToDafYomi(dafYomi, paneNavigation)
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
    <template v-if="dateInfo.dafYomi">
      <span class="bar-sep">·</span>
      <button v-if="dbReady" class="bar-item bar-item--btn" @click="openDafYomi">
        <span class="bar-lbl">דף יומי:</span> {{ dateInfo.dafYomi }}
      </button>
      <span v-else class="bar-item"
        ><span class="bar-lbl">דף יומי:</span> {{ dateInfo.dafYomi }}</span
      >
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
.zman-popup-anchor {
  position: fixed;
  z-index: 200;
}
</style>
