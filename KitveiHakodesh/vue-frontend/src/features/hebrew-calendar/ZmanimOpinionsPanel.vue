<script setup lang="ts">
// Lets the user tick which opinion lines the zmanim tool shows, grouped by zman.
import { useI18n } from 'vue-i18n'
import { zmanLabelKey } from './calendarTypes'
import { isCustomSelection, resetZmanShown, setZmanShown, useShownZmanNames, ZMAN_GROUP_LIST } from './zmanimShown'

const { t } = useI18n()
const shown = useShownZmanNames()
</script>

<template>
  <div class="opinions" :aria-label="t('hebrewCalendar.chooseOpinions')">
    <fieldset v-for="[group, names] in ZMAN_GROUP_LIST" :key="group" class="op-group">
      <legend>{{ t(`hebrewCalendar.zmanGroup.${group}`) }}</legend>
      <label v-for="name in names" :key="name" class="op-item">
        <input
          type="checkbox"
          :checked="shown.has(name)"
          @change="setZmanShown(shown, name, ($event.target as HTMLInputElement).checked)"
        />
        <span>{{ t(zmanLabelKey(name)) }}</span>
      </label>
    </fieldset>
    <button v-if="isCustomSelection" class="op-reset" type="button" @click="resetZmanShown()">
      {{ t('hebrewCalendar.resetOpinions') }}
    </button>
  </div>
</template>

<style scoped>
.opinions {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
  gap: 6px 12px;
  margin-top: 6px;
  padding: 6px;
  border: 1px solid color-mix(in srgb, var(--border-color) 60%, transparent);
  border-radius: 6px;
}
.op-group {
  border: none;
  margin: 0;
  padding: 0;
}
.op-group legend {
  font-size: 11px;
  font-weight: 700;
  color: var(--text-primary);
  padding: 0 0 2px;
}
.op-item {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 11px;
  color: var(--text-secondary);
  cursor: pointer;
  line-height: 1.5;
}
.op-reset {
  grid-column: 1 / -1;
  justify-self: start;
  font-size: 11px;
  background: none;
  border: 1px solid var(--border-color);
  border-radius: 4px;
  padding: 2px 8px;
  color: var(--text-primary);
  cursor: pointer;
}
</style>
