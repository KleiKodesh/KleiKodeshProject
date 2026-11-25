<script setup lang="ts">
// Previous day / Hebrew date picker / next day / today: the day row of the calendar widgets.
import { IconChevronLeft20Regular, IconChevronRight20Regular, IconHome20Regular } from '@iconify-prerendered/vue-fluent'
import HebrewDatePicker from './HebrewDatePicker.vue'

const day = defineModel<Date>({ required: true })
function shiftDay(by: number) {
  const d = new Date(day.value)
  d.setDate(d.getDate() + by)
  day.value = d
}
</script>

<template>
  <div class="hdn">
    <button class="hdn-btn" type="button" :title="$t('hebrewCalendar.nav.prev')" @click="shiftDay(-1)"><IconChevronRight20Regular /></button>
    <HebrewDatePicker v-model="day" />
    <button class="hdn-btn" type="button" :title="$t('hebrewCalendar.nav.next')" @click="shiftDay(1)"><IconChevronLeft20Regular /></button>
    <button class="hdn-btn" type="button" :title="$t('hebrewCalendar.nav.today')" @click="day = new Date()"><IconHome20Regular /></button>
  </div>
</template>

<style scoped>
.hdn { display: flex; align-items: center; justify-content: center; gap: 2px; margin: 4px 8px 0; }
.hdn-btn {
  display: inline-flex; align-items: center; justify-content: center;
  width: 26px; height: 26px; padding: 0;
  background: none; border: 1px solid transparent; border-radius: 4px;
  color: var(--text-secondary); cursor: pointer;
}
.hdn-btn:hover { color: var(--text-primary); border-color: var(--border-color); }
</style>
