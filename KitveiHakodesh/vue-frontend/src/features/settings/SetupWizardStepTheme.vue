<script setup lang="ts">
import { storeToRefs } from 'pinia'
import { useSettingsStore } from '@/stores/settingsStore'
import SettingRow from './SettingRow.vue'
import SliderSetting from './SliderSetting.vue'
import ThemePicker from './ThemePicker.vue'
import ThemeBackgroundTintSlider from './ThemeBackgroundTintSlider.vue'
import ToggleGroup from './ToggleGroup.vue'

const settings = useSettingsStore()
const { appZoom, pdfPageFilters } = storeToRefs(settings)

// Same wiring as the settings page (SettingsPageThemeAndApplicationSection).
function applyPdfPageFilters(value: boolean) {
  if (value !== pdfPageFilters.value) settings.togglePdfPageFilters()
}
</script>

<template>
  <SettingRow label="ערכת נושא">
    <ThemePicker />
  </SettingRow>
  <ThemeBackgroundTintSlider />
  <SliderSetting
    label="גודל תצוגה"
    v-model="appZoom"
    :min="0.5"
    :max="1.5"
    :step="0.05"
  />
  <SettingRow label="החל ערכת נושא על דפי PDF" hint="מחיל את צבעי ערכת הנושא על תוכן דפי PDF">
    <ToggleGroup
      :model-value="pdfPageFilters"
      :options="[
        { label: 'כן', value: true },
        { label: 'לא', value: false },
      ]"
      @update:model-value="applyPdfPageFilters"
    />
  </SettingRow>
</template>

