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
  <SettingRow :label="$t('settings.theme.section')">
    <ThemePicker />
  </SettingRow>
  <ThemeBackgroundTintSlider />
  <SliderSetting
    :label="$t('settings.theme.zoom')"
    v-model="appZoom"
    :min="0.5"
    :max="1.5"
    :step="0.05"
  />
  <SettingRow :label="$t('settings.theme.pdf')" :hint="$t('settings.theme.pdfHint')">
    <ToggleGroup
      :model-value="pdfPageFilters"
      :options="[
        { label: $t('settings.yes'), value: true },
        { label: $t('settings.no'), value: false },
      ]"
      @update:model-value="applyPdfPageFilters"
    />
  </SettingRow>
</template>

