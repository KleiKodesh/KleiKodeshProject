<script setup lang="ts">
// Previous day / Hebrew date picker / next day / today: the day row of the calendar widgets.
// The default slot adds a widget's own buttons at the row's end, after a separator.
// `buttonsOnly`: the day is picked elsewhere (the calendar page): a title line instead - `title`
// at the start, the slot's buttons at the end (as the zmanim widget's title and settings button).
import { useSlots } from 'vue'
import { IconChevronLeft20Regular, IconChevronRight20Regular, IconHome20Regular } from '@iconify-prerendered/vue-fluent'
import HebrewDatePicker from './HebrewDatePicker.vue'

const day = defineModel<Date>({ required: true })
defineProps<{ buttonsOnly?: boolean; title?: string }>()
const slots = useSlots()
function shiftDay(by: number) {
  const d = new Date(day.value)
  d.setDate(d.getDate() + by)
  day.value = d
}
</script>

<template>
  <div class="hdn" :class="{ 'hdn--title': buttonsOnly }">
    <span v-if="buttonsOnly" class="hdn-title">{{ title }}</span>
    <template v-if="!buttonsOnly">
      <button class="hdn-btn" type="button" :title="$t('hebrewCalendar.nav.prev')" @click="shiftDay(-1)"><IconChevronRight20Regular /></button>
      <HebrewDatePicker v-model="day" />
      <button class="hdn-btn" type="button" :title="$t('hebrewCalendar.nav.next')" @click="shiftDay(1)"><IconChevronLeft20Regular /></button>
      <button class="hdn-btn" type="button" :title="$t('hebrewCalendar.nav.today')" @click="day = new Date()"><IconHome20Regular /></button>
    </template>
    <template v-if="slots.default">
      <span v-if="!buttonsOnly" class="hdn-sep" />
      <slot />
    </template>
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
.hdn--title { justify-content: flex-start; }
.hdn-title { flex: 1; min-width: 0; font-size: 13px; font-weight: 600; color: var(--text-primary); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.hdn-sep { width: 1px; height: 18px; margin: 0 4px; background: var(--border-color); }
</style>
