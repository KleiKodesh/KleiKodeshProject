<script setup lang="ts">
import { onMounted } from 'vue'
import { useMonthlyView } from '@/features/hebrew-calendar/useMonthlyView'
import MonthlyView from '@/features/hebrew-calendar/MonthlyView.vue'

// Compact month grid for the home date bar. The full calendar page stays one
// click away via `open-full`.
const emit = defineEmits<{ 'open-full': [] }>()

const monthly = useMonthlyView()
onMounted(() => monthly.reset())
</script>

<template>
  <div class="calendar-popup">
    <div class="calendar-popup-header">
      <button class="nav-btn" :title="$t('home.calendar.prevMonth')" @click="monthly.prevMonth()">‹</button>
      <button class="title-btn" :title="$t('home.calendar.backToToday')" @click="monthly.goToday()">
        <span class="heb">{{ monthly.hebrewLabel.value }}</span>
        <span class="greg">{{ monthly.gregLabel.value }}</span>
      </button>
      <button class="nav-btn" :title="$t('home.calendar.nextMonth')" @click="monthly.nextMonth()">›</button>
      <button class="full-btn" :title="$t('home.calendar.openFull')" @click="emit('open-full')">
        {{ $t('home.calendar.full') }}
      </button>
    </div>
    <MonthlyView :monthly="monthly" class="calendar-popup-grid" />
  </div>
</template>

<style scoped>
.calendar-popup {
  width: 440px;
  max-width: calc(100vw - 16px);
  height: 380px;
  max-height: inherit;
  display: flex;
  flex-direction: column;
  padding: 6px 8px;
  background: var(--bg-primary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  box-shadow: 0 6px 24px rgba(0, 0, 0, 0.28);
}
.calendar-popup-header {
  display: flex;
  align-items: center;
  gap: 6px;
  padding-bottom: 4px;
  font-size: 12px;
}
.nav-btn,
.title-btn,
.full-btn {
  background: none;
  border: none;
  font-family: inherit;
  font-size: inherit;
  color: var(--text-primary);
  cursor: pointer;
  border-radius: 4px;
  padding: 2px 6px;
}
.nav-btn {
  font-size: 16px;
  line-height: 1;
}
.nav-btn:hover,
.title-btn:hover,
.full-btn:hover {
  background: var(--hover-bg);
}
.title-btn {
  display: flex;
  gap: 8px;
  font-weight: 600;
}
.greg {
  color: var(--text-secondary);
  font-weight: 400;
}
.full-btn {
  margin-inline-start: auto;
  color: var(--accent-color);
}
.calendar-popup-grid {
  flex: 1;
  min-height: 0;
}
</style>
