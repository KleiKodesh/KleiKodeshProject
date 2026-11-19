<script setup lang="ts">
import NextZmanPopup from '@/features/home/NextZmanPopup.vue'
import { useNextZman } from '@/features/home/useNextZman'
import { cityLabelWithCountry, citiesByCountry } from '@/features/hebrew-calendar/cityDisplayName'
import { CITIES } from '@/features/hebrew-calendar/calendarTypes'

const { rows, city, setCity } = useNextZman()

const sortedCities = citiesByCountry(CITIES)

function onPick(e: Event) {
  const found = CITIES.find((c) => c.name === (e.target as HTMLSelectElement).value)
  if (found) setCity(found)
}
</script>

<template>
  <div class="zmanim-widget">
    <select
      class="zw-city"
      :value="city.name"
      :title="$t('settings.calendar.city')"
      @change="onPick"
    >
      <option v-for="c in sortedCities" :key="c.name" :value="c.name">{{ cityLabelWithCountry(c) }}</option>
    </select>
    <NextZmanPopup :rows="rows" :city-name="''" />
  </div>
</template>

<style scoped>
.zmanim-widget { overflow-y: auto; }
.zw-city {
  display: block;
  width: calc(100% - 16px);
  margin: 8px 8px 0;
  height: 26px;
  font-size: 12px;
  color: var(--text-primary);
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  cursor: pointer;
}
.zw-city:hover { border-color: var(--accent-color); }
/* The popup's own frame goes: the widget window is the frame now. */
.zmanim-widget :deep(.zman-popup) { border: none; box-shadow: none; border-radius: 0; max-height: none; }
.zmanim-widget :deep(.zp-city) { display: none; }
</style>
