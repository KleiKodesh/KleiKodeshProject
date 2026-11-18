<script setup lang="ts">
import { computed } from 'vue'
import { useNow } from '@vueuse/core'
import { t } from '@/i18n'

const ZONES = [
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

const now = useNow({ interval: 10_000 })
const formatters = ZONES.map((zone) => ({
  ...zone,
  time: new Intl.DateTimeFormat('he-IL', { timeZone: zone.timeZone, hour: '2-digit', minute: '2-digit', hour12: false }),
  // en-CA gives a sortable YYYY-MM-DD, used only to compare calendar days.
  day: new Intl.DateTimeFormat('en-CA', { timeZone: zone.timeZone }),
}))
const localDay = new Intl.DateTimeFormat('en-CA')

const rows = computed(() => {
  const today = localDay.format(now.value)
  return formatters.map((zone) => {
    const zoneDay = zone.day.format(now.value)
    return {
      timeZone: zone.timeZone,
      cityLabel: zone.cityLabel,
      time: zone.time.format(now.value),
      dayOffset: zoneDay === today ? '' : zoneDay > today ? t('home.worldClock.tomorrow') : t('home.worldClock.yesterday'),
    }
  })
})
</script>

<template>
  <div class="world-clock-widget">
    <div v-for="row in rows" :key="row.timeZone" class="world-clock-item">
      <span class="world-clock-city">{{ row.cityLabel }}</span>
      <span class="world-clock-time">
        <span v-if="row.dayOffset" class="world-clock-day">{{ row.dayOffset }}</span>
        {{ row.time }}
      </span>
    </div>
  </div>
</template>

<style scoped>
.world-clock-widget { display: flex; flex-direction: column; padding: 8px 12px; overflow-y: auto; }
.world-clock-item {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: 16px;
  padding: 3px 0;
  font-size: 13px;
  color: var(--text-primary);
}
.world-clock-city { color: var(--text-secondary); }
.world-clock-time { font-weight: 600; font-variant-numeric: tabular-nums; }
.world-clock-day { font-size: 11px; font-weight: 400; color: var(--text-secondary); margin-inline-end: 4px; }
</style>
