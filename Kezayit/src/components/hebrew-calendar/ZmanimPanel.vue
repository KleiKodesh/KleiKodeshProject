<script setup lang="ts">
import { onMounted, ref } from 'vue'
import {
  IconLocation20Regular,
  IconChevronDown20Regular,
  IconDismiss20Regular,
} from '@iconify-prerendered/vue-fluent'
import { useZmanim } from './useZmanim'
import type { City } from './useZmanim'

const { activeCity, selectedCity, locationStatus, formattedEntries, cities, init, setCity } =
  useZmanim()

const showCityPicker = ref(false)

onMounted(() => init())

function pickCity(city: City) {
  setCity(city)
  showCityPicker.value = false
}

function clearManual() {
  setCity(null)
  showCityPicker.value = false
}
</script>

<template>
  <div class="zmanim-panel">
    <!-- Header -->
    <div class="zmanim-header">
      <span class="zmanim-title">זמני היום</span>
      <button class="location-btn" @click="showCityPicker = !showCityPicker">
        <IconLocation20Regular />
        <span class="location-name">{{ activeCity.name }}</span>
        <IconChevronDown20Regular class="chevron" :class="{ open: showCityPicker }" />
      </button>
    </div>

    <!-- City picker dropdown -->
    <div v-if="showCityPicker" class="city-picker">
      <button
        v-if="selectedCity && locationStatus !== 'fallback'"
        class="city-row auto-row"
        @click="clearManual"
      >
        <IconLocation20Regular />
        <span>זיהוי אוטומטי</span>
        <IconDismiss20Regular class="dismiss-icon" />
      </button>
      <button
        v-for="city in cities"
        :key="city.name"
        class="city-row"
        :class="{ active: activeCity.name === city.name }"
        @click="pickCity(city)"
      >
        {{ city.name }}
      </button>
    </div>

    <!-- Location status hint -->
    <div v-if="locationStatus === 'fallback' && !selectedCity" class="location-hint">
      לא ניתן לזהות מיקום — מוצגת ירושלים. בחר עיר ידנית.
    </div>

    <!-- Zmanim list -->
    <div class="zmanim-list">
      <div v-for="entry in formattedEntries" :key="entry.label" class="zmanim-row">
        <span class="zmanim-label">{{ entry.label }}</span>
        <span class="zmanim-time">{{ entry.time }}</span>
      </div>
    </div>
  </div>
</template>

<style scoped>
.zmanim-panel {
  display: flex;
  flex-direction: column;
  gap: 0;
  background: var(--bg-secondary);
  border-radius: 8px;
  overflow: hidden;
}

/* Header */
.zmanim-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 8px 12px;
  border-bottom: 1px solid var(--border-color);
}
.zmanim-title {
  font-size: 13px;
  font-weight: 600;
  color: var(--text-primary);
}
.location-btn {
  display: flex;
  align-items: center;
  gap: 4px;
  font-size: 12px;
  color: var(--text-secondary);
  border-radius: 4px;
  padding: 2px 6px;
}
.location-btn:hover {
  color: var(--text-primary);
}
.location-name {
  max-width: 80px;
  overflow: hidden;
  white-space: nowrap;
  text-overflow: ellipsis;
}
.chevron {
  transition: transform 0.15s;
}
.chevron.open {
  transform: rotate(180deg);
}

/* City picker */
.city-picker {
  display: flex;
  flex-direction: column;
  max-height: 300px;
  overflow-y: auto;
  border-bottom: 1px solid var(--border-color);
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
}
.city-row {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 0 12px;
  height: 44px;
  font-size: 14px;
  color: var(--text-primary);
  text-align: start;
  border-radius: 0;
}
.city-row:hover {
  background: color-mix(in srgb, var(--text-primary) 6%, transparent);
}
.city-row.active {
  color: var(--accent-color, #0078d4);
  background: color-mix(in srgb, var(--accent-color, #0078d4) 10%, transparent);
}
.auto-row {
  color: var(--text-secondary);
  font-size: 13px;
}
.dismiss-icon {
  margin-inline-start: auto;
  opacity: 0.5;
}

/* Hint */
.location-hint {
  font-size: 11px;
  color: var(--text-secondary);
  padding: 6px 12px;
  border-bottom: 1px solid var(--border-color);
}

/* Zmanim rows */
.zmanim-list {
  display: flex;
  flex-direction: column;
}
.zmanim-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0 12px;
  height: 32px;
  font-size: 13px;
  border-bottom: 1px solid color-mix(in srgb, var(--border-color) 50%, transparent);
}
.zmanim-row:last-child {
  border-bottom: none;
}
.zmanim-label {
  color: var(--text-secondary);
}
.zmanim-time {
  font-variant-numeric: tabular-nums;
  color: var(--text-primary);
  font-weight: 500;
}
</style>
