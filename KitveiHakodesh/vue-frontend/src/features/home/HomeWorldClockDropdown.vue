<script setup lang="ts">
import { t } from '@/i18n'
// Bar clock button + world clock dropdown (current time in other time zones).
// Same dropdown pattern as the Limud Yomi menu in HomePageDateBar.vue.
import { ref, computed, nextTick } from 'vue'
import { useNow } from '@vueuse/core'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { computeBarPopupAnchor, type BarPopupAnchor } from './homeBarPopupAnchor'

defineProps<{ clockTime: string }>()

interface WorldClockZone {
  timeZone: string
  cityLabel: string
}

const WORLD_CLOCK_ZONES: WorldClockZone[] = [
  { timeZone: 'Asia/Jerusalem', cityLabel: t('home.worldClock.israel') },
  { timeZone: 'America/New_York', cityLabel: t('home.worldClock.newYork') },
  { timeZone: 'America/Los_Angeles', cityLabel: t('home.worldClock.losAngeles') },
  { timeZone: 'Europe/London', cityLabel: t('home.worldClock.london') },
  { timeZone: 'Europe/Paris', cityLabel: t('home.worldClock.paris') },
  { timeZone: 'Europe/Brussels', cityLabel: t('home.worldClock.antwerp') },
  { timeZone: 'Europe/Moscow', cityLabel: t('home.worldClock.moscow') },
  { timeZone: 'Africa/Johannesburg', cityLabel: t('home.worldClock.johannesburg') },
  { timeZone: 'America/Argentina/Buenos_Aires', cityLabel: t('home.worldClock.buenosAires') },
  { timeZone: 'Australia/Melbourne', cityLabel: t('home.worldClock.melbourne') },
]

const isWorldClockOpen = ref(false)
const now = useNow({ interval: 10_000 })

// One formatter pair per zone, built once. Time format matches the bar clock.
const zoneFormatters = WORLD_CLOCK_ZONES.map((zone) => ({
  ...zone,
  time: new Intl.DateTimeFormat('he-IL', {
    timeZone: zone.timeZone,
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  }),
  // en-CA gives a sortable YYYY-MM-DD, used only to compare calendar days.
  day: new Intl.DateTimeFormat('en-CA', { timeZone: zone.timeZone }),
}))
const localDayFormatter = new Intl.DateTimeFormat('en-CA')

function dayOffsetLabel(zoneDay: string, localDay: string): string {
  if (zoneDay === localDay) return ''
  return zoneDay > localDay ? t('home.worldClock.tomorrow') : t('home.worldClock.yesterday')
}

const worldClockRows = computed(() => {
  if (!isWorldClockOpen.value) return []
  const localDay = localDayFormatter.format(now.value)
  return zoneFormatters.map((zone) => ({
    timeZone: zone.timeZone,
    cityLabel: zone.cityLabel,
    time: zone.time.format(now.value),
    dayOffset: dayOffsetLabel(zone.day.format(now.value), localDay),
  }))
})

const worldClockBarItemRef = ref<HTMLElement | null>(null)
const worldClockButtonRef = ref<HTMLElement | null>(null)
const worldClockMenuRef = ref<HTMLElement | null>(null)
const worldClockMenuAnchor = ref<BarPopupAnchor>({ left: 0, bottom: 0, maxHeight: 0 })
const worldClockMenuStyle = computed(() => ({
  left: `${worldClockMenuAnchor.value.left}px`,
  bottom: `${worldClockMenuAnchor.value.bottom}px`,
  maxHeight: `${worldClockMenuAnchor.value.maxHeight}px`,
}))

function computeWorldClockMenuAnchor() {
  const button = worldClockButtonRef.value
  if (!button) return
  worldClockMenuAnchor.value = computeBarPopupAnchor(
    button,
    worldClockMenuRef.value?.offsetWidth || 220,
  )
}

const worldClockCloser = useDropdownClose(
  worldClockBarItemRef,
  () => {
    isWorldClockOpen.value = false
  },
  // The wrapper is the inline bar item; only the popup is fitted.
  { ignore: [worldClockMenuRef], fitToViewport: false },
)

function toggleWorldClockMenu() {
  if (worldClockCloser.justClosed.value) return
  const opening = !isWorldClockOpen.value
  isWorldClockOpen.value = opening
  if (opening) {
    computeWorldClockMenuAnchor()
    nextTick(() => {
      computeWorldClockMenuAnchor()
      worldClockMenuRef.value?.focus()
    })
  }
}

function closeWorldClockMenuAndRefocus() {
  isWorldClockOpen.value = false
  worldClockButtonRef.value?.focus()
}

function onWorldClockMenuKeydown(event: KeyboardEvent) {
  if (event.code !== 'Escape') return
  event.preventDefault()
  closeWorldClockMenuAndRefocus()
}
</script>

<template>
  <div ref="worldClockBarItemRef" class="world-clock-wrap">
    <button
      ref="worldClockButtonRef"
      class="bar-item bar-item--btn bar-clock"
      :class="{ on: isWorldClockOpen }"
      :title="$t('home.worldClock.title')"
      @click="toggleWorldClockMenu"
    >
      {{ clockTime }}
    </button>
  </div>
  <Teleport to="body">
    <div
      v-if="isWorldClockOpen"
      ref="worldClockMenuRef"
      class="world-clock-menu"
      :style="worldClockMenuStyle"
      tabindex="-1"
      @keydown="onWorldClockMenuKeydown"
    >
      <div
        v-for="row in worldClockRows"
        :key="row.timeZone"
        class="world-clock-item"
      >
        <span class="world-clock-city">{{ row.cityLabel }}</span>
        <span class="world-clock-time">
          <span v-if="row.dayOffset" class="world-clock-day">{{ row.dayOffset }}</span>
          {{ row.time }}
        </span>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
.world-clock-wrap {
  position: relative;
  display: inline-flex;
}
.bar-item--btn {
  background: none;
  border: none;
  padding: 0;
  font-size: inherit;
  font-family: inherit;
  font-weight: 600;
  cursor: pointer;
  color: var(--text-primary);
  white-space: nowrap;
}
.bar-item--btn:hover,
.bar-item--btn.on {
  color: var(--accent-color);
}
.bar-clock {
  font-variant-numeric: tabular-nums;
  letter-spacing: 0.03em;
}
/* Density matches NextZmanPopup (12px rows, 1.4 line-height, 2px row padding). */
.world-clock-menu {
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
.world-clock-item {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: 16px;
  padding: 2px 4px;
  font-size: 12px;
  line-height: 1.4;
  color: var(--text-primary);
}
.world-clock-city {
  color: var(--text-secondary);
}
.world-clock-time {
  font-weight: 600;
  font-variant-numeric: tabular-nums;
}
.world-clock-day {
  font-size: 11px;
  font-weight: 400;
  color: var(--text-secondary);
  margin-inline-end: 4px;
}
</style>
