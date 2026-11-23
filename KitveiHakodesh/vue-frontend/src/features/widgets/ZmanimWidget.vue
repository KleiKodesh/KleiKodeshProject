<script setup lang="ts">
import { onMounted, shallowRef } from 'vue'
import NextZmanPopup from '@/features/home/NextZmanPopup.vue'
import { useNextZman } from '@/features/home/useNextZman'
import { placeNames } from '@/features/hebrew-calendar/zmanimPlaces'

const { rows, city, setCity } = useNextZman()

/** Place names in the UI language; index = place id (index 0 is empty). */
const names = shallowRef<readonly string[]>([])
onMounted(async () => { names.value = await placeNames() })

function onPick(e: Event) {
  setCity(Number((e.target as HTMLSelectElement).value))
}
</script>

<template>
  <div class="zmanim-widget">
    <select
      class="zw-city"
      :value="city?.id"
      :title="$t('settings.calendar.city')"
      @change="onPick"
    >
      <template v-for="(name, id) in names" :key="id">
        <option v-if="id > 0" :value="id">{{ name }}</option>
      </template>
    </select>
    <NextZmanPopup :rows="rows" :city-name="''" />
  </div>
</template>

<style scoped>
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
/* The popup's own frame goes: the widget window (or the calendar's side column) is the frame. */
.zmanim-widget :deep(.zman-popup) { border: none; box-shadow: none; border-radius: 0; max-height: none; }
.zmanim-widget :deep(.zp-city) { display: none; }
</style>
